using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Rpc;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace Miningcore.Blockchain.Bitcoin.RSK;

/// <summary>
/// Manages RSK (Rootstock) merge mining alongside a Bitcoin-family parent pool.
///
/// RSK uses a different protocol from standard AuxPoW chains:
///   - Work fetched via mnr_getWork (returns blockHashForMergedMining + target)
///   - Submission via mnr_submitBitcoinBlock (header + coinbase + Bitcoin tx Merkle branch)
///
/// RSK does NOT use the multi-chain aux Merkle tree.  Instead its block hash is committed
/// as an OP_RETURN coinbase output: OP_RETURN + "RSKBLOCK:" (9 bytes) + hash (32 bytes).
/// RSKj searches the full serialised coinbase transaction for this pattern.
/// Using an output (rather than the scriptSig) means no coinbase scriptSig bytes are
/// consumed by RSK, so it works on coins with strict 100-byte scriptSig limits.
/// </summary>
public class RskManager : IDisposable
{
    private readonly ILogger logger;
    private readonly RskChainConfig config;
    private readonly RpcClient rpc;
    private volatile AuxBlockData currentAuxBlock;
    private readonly SemaphoreSlim fetchLock = new(1, 1);
    private volatile bool invalidated = true;
    private long lastFetchTicks;
    private readonly TimeSpan fetchInterval;

    public RskManager(RskChainConfig config, JsonSerializerSettings serializerSettings, IMessageBus messageBus)
    {
        this.config = config;
        logger = LogManager.GetLogger(config.Id);

        if(config.Daemons == null || config.Daemons.Length == 0)
            throw new ArgumentException($"RskChain '{config.Id}' has no daemons configured");

        rpc = new RpcClient(config.Daemons.First(), serializerSettings, messageBus, config.Id);
        fetchInterval = TimeSpan.FromSeconds(config.PollIntervalSeconds > 0 ? config.PollIntervalSeconds : 5);
    }

    public void Dispose() => fetchLock.Dispose();

    public RskChainConfig Config => config;
    public AuxBlockData CurrentAuxBlock => currentAuxBlock;
    public RpcClient Rpc => rpc;

    /// <summary>
    /// Polls mnr_getWork if the cache has expired or been invalidated.
    /// mnr_getWork returns { blockHashForMergedMining, target, feesPaidToMiner, notify }.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var elapsed = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - Interlocked.Read(ref lastFetchTicks));
        if(!invalidated && elapsed < fetchInterval)
            return;

        if(!await fetchLock.WaitAsync(0))
            return;

        try
        {
            var response = await rpc.ExecuteAsync<JToken>(logger, "mnr_getWork", ct, null);

            if(response.Error != null)
            {
                if(config.SilentErrors)
                    logger.Debug(() => $"[{config.Id}] mnr_getWork failed: {response.Error.Message}");
                else
                    logger.Warn(() => $"[{config.Id}] mnr_getWork failed: {response.Error.Message}");
                return;
            }

            var result = response.Response;
            if(result == null || result.Type == JTokenType.Null)
                return;

            // RSKj returns hex strings prefixed with "0x"
            var hashHex = StripHexPrefix(result["blockHashForMergedMining"]?.Value<string>());
            var targetHex = StripHexPrefix(result["target"]?.Value<string>());

            if(string.IsNullOrEmpty(hashHex) || hashHex.Length != 64)
            {
                logger.Warn(() => $"[{config.Id}] mnr_getWork returned unexpected blockHashForMergedMining: {hashHex}");
                return;
            }

            var block = new AuxBlockData
            {
                // Hash is kept in big-endian (as returned by RSKj). The OP_RETURN commitment writes
                // it directly; RSKj searches for the same byte sequence. Do NOT reverse here.
                Hash = hashHex,
                ChainId = config.ChainId,
                Target = targetHex,
                FetchedAt = DateTimeOffset.UtcNow,
            };

            if(!string.IsNullOrEmpty(block.Target) && block.Target.Length == 64)
            {
                // RSKj returns target in big-endian; uint256 stores LE internally, so reverse.
                block.TargetValue = new uint256(block.Target.HexToByteArray().Reverse().ToArray());
            }
            else
            {
                logger.Warn(() => $"[{config.Id}] mnr_getWork returned unexpected target (cannot set TargetValue): '{targetHex}'");
            }

            currentAuxBlock = block;
            Interlocked.Exchange(ref lastFetchTicks, DateTimeOffset.UtcNow.Ticks);
            invalidated = false;

            logger.Debug(() => $"[{config.Id}] RSK work: hash={hashHex[..16]}...");
        }
        catch(Exception ex)
        {
            if(config.SilentErrors)
                logger.Debug(() => $"[{config.Id}] mnr_getWork exception: {ex.Message}");
            else
                logger.Warn(() => $"[{config.Id}] mnr_getWork exception: {ex.Message}");
        }
        finally
        {
            fetchLock.Release();
        }
    }

    /// <summary>
    /// Submits a merged Bitcoin block to the RSK node.
    /// RSKj extracts the RSK commitment from the coinbase and verifies it independently.
    /// </summary>
    /// <param name="blockHashForMergedMining">The hash from mnr_getWork (no 0x prefix)</param>
    /// <param name="headerHex">80-byte Bitcoin header (hex)</param>
    /// <param name="coinbaseTxHex">Full coinbase transaction (hex)</param>
    /// <param name="merkleBranch">Bitcoin coinbase-to-Merkle-root branch</param>
    public async Task<bool> SubmitBitcoinBlockAsync(
        string blockHashForMergedMining,
        string headerHex,
        string coinbaseTxHex,
        IList<byte[]> merkleBranch,
        CancellationToken ct)
    {
        // RSKj expects each branch hash as a hex string (no 0x prefix)
        var branchHex = merkleBranch.Select(h => h.ToHexString()).ToArray();

        // RSKj RPC: mnr_submitBitcoinBlock(blockHashForMergedMining, header, coinbase, merkleBranch[])
        var response = await rpc.ExecuteAsync<JToken>(logger, "mnr_submitBitcoinBlock", ct,
            new object[] { blockHashForMergedMining, headerHex, coinbaseTxHex, branchHex });

        if(response.Error != null)
        {
            logger.Warn(() => $"[{config.Id}] mnr_submitBitcoinBlock failed: {response.Error.Message}");
            return false;
        }

        bool accepted;
        if(response.Response == null || response.Response.Type == JTokenType.Null)
            accepted = true;
        else if(response.Response.Type == JTokenType.Boolean)
            accepted = response.Response.Value<bool>();
        else
        {
            var text = response.Response.ToString();
            accepted = string.IsNullOrEmpty(text);
            if(!accepted)
                logger.Warn(() => $"[{config.Id}] RSK block rejected: {text}");
        }

        if(accepted)
            logger.Info(() => $"[{config.Id}] RSK merged block accepted! hash={blockHashForMergedMining[..16]}...");

        invalidated = true;
        return accepted;
    }

    private static string StripHexPrefix(string s)
    {
        if(s == null) return null;
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
    }
}
