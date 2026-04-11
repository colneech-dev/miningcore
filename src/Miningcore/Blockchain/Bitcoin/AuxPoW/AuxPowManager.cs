using System.Globalization;
using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Miningcore.Configuration;
using Miningcore.Messaging;
using Miningcore.Rpc;
using NBitcoin;
using Newtonsoft.Json;
using Miningcore.Extensions;
using Newtonsoft.Json.Linq;
using NLog;

namespace Miningcore.Blockchain.Bitcoin.AuxPoW;

/// <summary>
/// Manages auxiliary chain (e.g. Namecoin) merge mining for a primary Bitcoin-family pool.
/// Polls each aux chain daemon via getauxblock, stores current work, and submits
/// merged blocks when a share meets the aux chain's difficulty.
/// </summary>
public class AuxPowManager
{
    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    private readonly AuxChainConfig config;
    private readonly RpcClient rpc;
    private volatile AuxBlockData currentAuxBlock;
    private readonly SemaphoreSlim fetchLock = new(1, 1);
    private DateTimeOffset lastFetch = DateTimeOffset.MinValue;
    private static readonly TimeSpan FetchInterval = TimeSpan.FromSeconds(10);

    public AuxPowManager(AuxChainConfig config, JsonSerializerSettings serializerSettings, IMessageBus messageBus)
    {
        this.config = config;
        rpc = new RpcClient(config.Daemons.First(), serializerSettings, messageBus, config.Id);
    }

    public AuxChainConfig Config => config;
    public AuxBlockData CurrentAuxBlock => currentAuxBlock;

    /// <summary>
    /// Fetches fresh aux work from the daemon if the cache has expired.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        if(DateTimeOffset.UtcNow - lastFetch < FetchInterval)
            return;

        if(!await fetchLock.WaitAsync(0, ct))
            return;

        try
        {
            var response = await rpc.ExecuteAsync<JToken>(logger, "getauxblock", ct);

            if(response.Error != null)
            {
                logger.Warn(() => $"[{config.Id}] getauxblock failed: {response.Error.Message}");
                return;
            }

            var block = new AuxBlockData
            {
                Hash = response.Response["hash"]?.Value<string>(),
                ChainId = response.Response["chainid"]?.Value<int>() ?? config.ChainId,
                PreviousBlockHash = response.Response["previousblockhash"]?.Value<string>(),
                CoinbaseValue = response.Response["coinbasevalue"]?.Value<long>() ?? 0,
                Bits = response.Response["bits"]?.Value<string>(),
                Target = response.Response["target"]?.Value<string>(),
                Height = response.Response["height"]?.Value<int>() ?? 0,
                FetchedAt = DateTimeOffset.UtcNow,
            };

            if(!string.IsNullOrEmpty(block.Target))
                block.TargetValue = new uint256(block.Target.HexToByteArray().Reverse().ToArray());

            currentAuxBlock = block;
            lastFetch = DateTimeOffset.UtcNow;

            logger.Debug(() => $"[{config.Id}] AuxBlock refreshed: height={block.Height} target={block.Target}");
        }
        catch(Exception ex)
        {
            logger.Warn(() => $"[{config.Id}] getauxblock exception: {ex.Message}");
        }
        finally
        {
            fetchLock.Release();
        }
    }

    /// <summary>
    /// Submits a merged block to the aux chain daemon.
    /// Called when a parent chain share meets the aux chain's difficulty.
    /// </summary>
    /// <param name="auxHash">The aux block hash (from getauxblock)</param>
    /// <param name="auxPoWHex">The serialized AuxPoW proof (parent header + merkle branch)</param>
    public async Task<bool> SubmitAuxBlockAsync(string auxHash, string auxPoWHex, CancellationToken ct)
    {
        var response = await rpc.ExecuteAsync<JToken>(logger, "submitauxblock", ct, new object[] { auxHash, auxPoWHex });

        if(response.Error != null)
        {
            logger.Warn(() => $"[{config.Id}] submitauxblock failed: {response.Error.Message}");
            return false;
        }

        var accepted = response.Response?.Value<bool>() ?? false;

        if(accepted)
            logger.Info(() => $"[{config.Id}] Merged block accepted by {config.Name}!");
        else
            logger.Warn(() => $"[{config.Id}] Merged block rejected by {config.Name}");

        // Invalidate current aux block to force refresh on next share
        lastFetch = DateTimeOffset.MinValue;

        return accepted;
    }
}
