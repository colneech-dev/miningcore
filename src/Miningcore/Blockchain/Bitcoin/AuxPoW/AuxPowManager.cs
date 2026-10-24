using System.Globalization;
using System.Reactive.Linq;
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
public class AuxPowManager : IDisposable
{
    private readonly ILogger logger;

    private readonly AuxChainConfig config;
    private readonly RpcClient rpc;
    private volatile AuxBlockData currentAuxBlock;
    private readonly SemaphoreSlim fetchLock = new(1, 1);
    private volatile bool auxBlockInvalidated = true;
    private long lastFetchTicks;
    private long lastFailureTicks;   // a failed poll is not retried before fetchInterval has passed (the invalidated flag stays set on failure, so without this a down daemon was polled every second, plus once per share)
    private readonly Dictionary<string, GbtChildBlock> children = new();
    private readonly Queue<string> childOrder = new();
    private readonly TimeSpan fetchInterval;

    // Key of the last target warning emitted, so a daemon that keeps reporting the same
    // unusable target is warned about once per distinct value instead of on every poll.
    private string lastTargetWarningKey;

    public AuxPowManager(AuxChainConfig config, JsonSerializerSettings serializerSettings, IMessageBus messageBus)
    {
        this.config = config;
        logger = LogManager.GetLogger(config.Id);

        if(config.Daemons == null || config.Daemons.Length == 0)
            throw new ArgumentException($"AuxChain '{config.Id}' has no daemons configured");

        rpc = new RpcClient(config.Daemons.First(), serializerSettings, messageBus, config.Id);
        fetchInterval = TimeSpan.FromSeconds(config.PollIntervalSeconds > 0 ? config.PollIntervalSeconds : 10);
    }

    public void Dispose()
    {
        fetchLock.Dispose();
    }

    public AuxChainConfig Config => config;
    public AuxBlockData CurrentAuxBlock => currentAuxBlock;

    // Network hashrate of the aux chain as reported by its own daemon (getnetworkhashps), cached so the API never
    // waits on an RPC and a daemon without the method costs one failed call every few minutes, not one per poll.
    private static readonly TimeSpan networkHashrateInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan networkHashrateRetryInterval = TimeSpan.FromMinutes(5);
    private long networkHashrateTicks;       // ticks of the last attempt (success or failure)
    private int networkHashrateBusy;
    private volatile bool networkHashrateFailed;
    private double? networkHashrate;
    private DateTimeOffset? networkHashrateAt;

    /// <summary>Hashes per second the aux chain's daemon reports for the whole network, or null when unknown.</summary>
    public double? NetworkHashrate => networkHashrate;

    /// <summary>When <see cref="NetworkHashrate"/> was last read from the daemon.</summary>
    public DateTimeOffset? NetworkHashrateAt => networkHashrateAt;

    /// <summary>
    /// Reads the network hashrate from the aux daemon at most once a minute (every five minutes after a failure).
    /// Never throws; the result is exposed through <see cref="NetworkHashrate"/>.
    /// </summary>
    public async Task RefreshNetworkHashrateAsync(CancellationToken ct)
    {
        var wait = networkHashrateFailed ? networkHashrateRetryInterval : networkHashrateInterval;
        if(TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - Interlocked.Read(ref networkHashrateTicks)) < wait)
            return;

        if(Interlocked.CompareExchange(ref networkHashrateBusy, 1, 0) != 0)
            return;

        try
        {
            Interlocked.Exchange(ref networkHashrateTicks, DateTimeOffset.UtcNow.Ticks);

            var response = await rpc.ExecuteAsync<JToken>(logger, "getnetworkhashps", ct, null, timeout: TimeSpan.FromSeconds(10));
            var value = response.Error == null ? ParseHashrate(response.Response) : null;

            networkHashrateFailed = value == null;
            if(value != null)
            {
                networkHashrate = value;
                networkHashrateAt = DateTimeOffset.UtcNow;
            }
        }
        catch(Exception ex)
        {
            networkHashrateFailed = true;
            logger.Debug(() => $"[{config.Id}] getnetworkhashps failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref networkHashrateBusy, 0);
        }
    }

    /// <summary>
    /// Reads a hashrate from an RPC result: a JSON number, or a numeric string. Anything that is not a finite,
    /// positive number (zero means the daemon has no estimate yet) is null.
    /// </summary>
    internal static double? ParseHashrate(JToken token)
    {
        if(token == null)
            return null;

        double value;

        if(token.Type is JTokenType.Float or JTokenType.Integer)
            value = token.Value<double>();
        else if(token.Type == JTokenType.String && double.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            value = parsed;
        else
            return null;

        return double.IsFinite(value) && value > 0 ? value : null;
    }

    /// <summary>
    /// Subscribes to the aux chain daemon's ZMQ hashblock topic if configured.
    /// On each notification, immediately invalidates the cached aux block so the
    /// next RefreshAsync call fetches fresh work without waiting for the poll interval.
    /// </summary>
    public void StartZmqSubscription(CancellationToken ct)
    {
        if(string.IsNullOrEmpty(config.ZmqBlockNotifySocket))
            return;

        var topic = !string.IsNullOrEmpty(config.ZmqBlockNotifyTopic)
            ? config.ZmqBlockNotifyTopic
            : BitcoinConstants.ZmqPublisherTopicBlockHash;

        var portMap = new Dictionary<DaemonEndpointConfig, (string Socket, string Topic)>
        {
            [config.Daemons.First()] = (config.ZmqBlockNotifySocket, topic)
        };

        logger.Info(() => $"[{config.Id}] Subscribing to ZMQ aux block notifications from {config.ZmqBlockNotifySocket}");

        rpc.ZmqSubscribe(logger, ct, portMap)
            .Subscribe(
                msg =>
                {
                    using(msg)
                    {
                        auxBlockInvalidated = true;
                        logger.Debug(() => $"[{config.Id}] ZMQ: new aux block signalled, invalidating cache");
                        _ = RefreshAsync(ct);
                    }
                },
                ex => logger.Warn(() => $"[{config.Id}] ZMQ subscription error: {ex.Message}"),
                ct);
    }

    /// <summary>
    /// Fetches fresh aux work from the daemon if the cache has expired.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        // independent of the work fetch below and self-throttled; the API reads the cached value
        _ = RefreshNetworkHashrateAsync(ct);

        var elapsed = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - Interlocked.Read(ref lastFetchTicks));
        if(!auxBlockInvalidated && elapsed < fetchInterval)
            return;

        var sinceFailure = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - Interlocked.Read(ref lastFailureTicks));
        if(sinceFailure < fetchInterval)
            return;

        if(!await fetchLock.WaitAsync(0))
            return;

        // Failure is recorded at the failure sites (an RPC error, an exception) and not inferred from the absence of a
        // success marker: a refresh path that returns early on success (e.g. the child-template path merged from main)
        // would otherwise be counted as a failure and delay the next, ZMQ-triggered refresh.
        var failed = false;

        try
        {
            var method = config.GetAuxBlockMethod;
            var args = BuildWorkRequestArgs(config);

            // a slow aux daemon must not hold the refresh loop for the full default timeout
            var response = await rpc.ExecuteAsync<JToken>(logger, method, ct, args, timeout: TimeSpan.FromSeconds(10));

            if(response.Error != null)
            {
                if(config.SilentErrors)
                    logger.Debug(() => $"[{config.Id}] {method} failed: {response.Error.Message}");
                else
                    logger.Warn(() => $"[{config.Id}] {method} failed: {response.Error.Message}");

                failed = true;
                return;
            }

            if(method == "getblocktemplate")
            {
                var child = GbtChildBuilder.Build(response.Response, config.Address, config.Bech32Prefix, config.ChainId, config.CoinbaseTag);

                // keep the last few children so a share solved against slightly stale work can still be submitted
                lock(children)
                {
                    // unchanged work (only curtime moved): keep the existing child so the job isn't churned
                    var prev = currentAuxBlock != null && children.TryGetValue(currentAuxBlock.Hash, out var p) ? p : null;
                    if(prev != null && prev.Fingerprint == child.Fingerprint && DateTimeOffset.UtcNow - currentAuxBlock.FetchedAt < TimeSpan.FromSeconds(90))
                    {
                        Interlocked.Exchange(ref lastFetchTicks, DateTimeOffset.UtcNow.Ticks);
                        auxBlockInvalidated = false;
                        return;
                    }

                    children[child.Hash] = child;
                    childOrder.Enqueue(child.Hash);
                    while(childOrder.Count > 8)
                        children.Remove(childOrder.Dequeue());
                }

                var tb = new AuxBlockData
                {
                    Hash = child.Hash,
                    ChainId = child.ChainId,
                    PreviousBlockHash = child.PreviousBlockHash,
                    CoinbaseValue = child.CoinbaseValue,
                    Bits = child.Bits,
                    Target = child.Target,
                    Height = child.Height,
                    FetchedAt = DateTimeOffset.UtcNow,
                };
                // gbt "target" is big-endian display hex; bits disambiguates
                tb.TargetValue = ResolveTarget(tb.Target, tb.Bits);

                currentAuxBlock = tb;
                Interlocked.Exchange(ref lastFetchTicks, DateTimeOffset.UtcNow.Ticks);
                auxBlockInvalidated = false;
                logger.Debug(() => $"[{config.Id}] Child template built: height={tb.Height} hash={tb.Hash}");
                return;
            }

            var block = new AuxBlockData
            {
                Hash = response.Response["hash"]?.Value<string>(),
                ChainId = ParseInt(response.Response["chainid"]) ?? config.ChainId,
                SlotByNonce = config.SlotByNonce,
                PreviousBlockHash = response.Response["previousblockhash"]?.Value<string>(),
                CoinbaseValue = response.Response["coinbasevalue"]?.Value<long>() ?? 0,
                Bits = response.Response["bits"]?.Value<string>(),
                Target = response.Response["target"]?.Value<string>()
                    ?? response.Response["_target"]?.Value<string>(),
                Height = ParseInt(response.Response["height"]) ?? 0,
                FetchedAt = DateTimeOffset.UtcNow,
            };

            block.TargetValue = ResolveTarget(block.Target, block.Bits, out var targetResolution);
            LogTargetResolution(block, targetResolution);

            currentAuxBlock = block;
            Interlocked.Exchange(ref lastFetchTicks, DateTimeOffset.UtcNow.Ticks);
            auxBlockInvalidated = false;

            logger.Debug(() => $"[{config.Id}] AuxBlock refreshed: height={block.Height} target={block.Target}");
        }
        catch(Exception ex)
        {
            failed = true;

            if(config.SilentErrors)
                logger.Debug(() => $"[{config.Id}] {config.GetAuxBlockMethod} exception: {ex.Message}");
            else
                logger.Warn(() => $"[{config.Id}] {config.GetAuxBlockMethod} exception: {ex.Message}");
        }
        finally
        {
            if(failed)
                Interlocked.Exchange(ref lastFailureTicks, DateTimeOffset.UtcNow.Ticks);

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
        var submitMethod = config.SubmitAuxBlockMethod;
        object[] submitArgs = { auxHash, auxPoWHex };

        if(config.GetAuxBlockMethod == "getblocktemplate")
        {
            GbtChildBlock child;
            lock(children)
                children.TryGetValue(auxHash, out child);

            if(child == null)
            {
                logger.Warn(() => $"[{config.Id}] No stored child template for {auxHash} — cannot assemble block");
                return false;
            }

            submitMethod = "submitblock";
            submitArgs = new object[] { GbtChildBuilder.BuildSubmitHex(child, auxPoWHex) };
        }

        var response = await rpc.ExecuteAsync<JToken>(logger, submitMethod, ct, submitArgs);

        if(response.Error != null)
        {
            logger.Warn(() => $"[{config.Id}] {submitMethod} failed: {response.Error.Message}");
            return false;
        }

        // Determine acceptance from the response value:
        //   null / JNull  → accepted (Namecoin-family daemons return null on success;
        //                   actual rejections come back as RPC errors handled above)
        //   boolean true  → explicitly accepted
        //   boolean false → explicitly rejected
        //   string ""     → accepted (Bitcoin Core submitblock pattern)
        //   string <text> → rejected, text is the rejection reason
        bool accepted;
        string logReason;

        if(response.Response == null || response.Response.Type == JTokenType.Null)
        {
            accepted = true;
            logReason = "null response";
        }
        else if(response.Response.Type == JTokenType.Boolean)
        {
            accepted = response.Response.Value<bool>();
            logReason = accepted ? "explicit true" : "explicit false";
        }
        else
        {
            var text = response.Response.ToString();
            accepted = string.IsNullOrEmpty(text);
            logReason = accepted ? "empty string" : text;
        }

        if(accepted)
            logger.Info(() => $"[{config.Id}] Merged block accepted by {config.Name} ({logReason})");
        else
            logger.Warn(() => $"[{config.Id}] Merged block rejected by {config.Name}: {logReason}");

        // Invalidate current aux block to force refresh on next share
        auxBlockInvalidated = true;

        return accepted;
    }

    /// <summary>
    /// Turns the daemon's reported target into the uint256 used for share comparison.
    ///
    /// Namecoin-family daemons report <c>_target</c> (or <c>target</c>) as little-endian hex, which is
    /// what this class has always assumed. Newer Bitcoin Core forks (S256, Qbit) instead report
    /// <c>target</c> as big-endian display hex. Read as little-endian that value is astronomically
    /// small, so no share would ever qualify and merged mining would fail silently.
    ///
    /// <c>bits</c> is unambiguous, so it is used to decide which reading is right: whichever
    /// interpretation re-compacts to the reported bits wins. If neither does (or bits is absent) the
    /// historical little-endian reading is kept, so existing chains behave exactly as before.
    /// </summary>
    /// <summary>
    /// Parameters for the aux work request. createauxblock always takes the payout address. getauxblock takes it only
    /// when <see cref="AuxChainConfig.PassAddressToGetAuxBlock"/> is set, because on Namecoin-style daemons a lone
    /// argument to getauxblock is a block hash. getblocktemplate takes the segwit rule. Anything else: no parameters.
    /// </summary>
    internal static object BuildWorkRequestArgs(AuxChainConfig config)
    {
        var hasAddress = !string.IsNullOrEmpty(config.Address);

        if(config.GetAuxBlockMethod == "createauxblock" && hasAddress)
            return new object[] { config.Address };

        if(config.GetAuxBlockMethod == "getauxblock" && config.PassAddressToGetAuxBlock && hasAddress)
            return new object[] { config.Address };

        if(config.GetAuxBlockMethod == "getblocktemplate")
            return new object[] { new { rules = new[] { "segwit" } } };

        return null;
    }

    public static uint256 ResolveTarget(string targetHex, string bitsHex)
    {
        return ResolveTarget(targetHex, bitsHex, out _);
    }

    /// <summary>
    /// How <see cref="ResolveTarget(string, string, out TargetResolution)"/> arrived at its result.
    /// </summary>
    public enum TargetResolution
    {
        /// <summary>Neither target nor usable bits were reported: no target value at all.</summary>
        None,
        /// <summary>No target reported; the value was expanded from bits.</summary>
        FromBits,
        /// <summary>Target read as little-endian hex, confirmed by bits.</summary>
        LittleEndian,
        /// <summary>Target read as big-endian display hex, confirmed by bits.</summary>
        BigEndian,
        /// <summary>Target read as little-endian hex; bits were absent or malformed, so this is unverified.</summary>
        LittleEndianUnchecked,
        /// <summary>Neither byte order of target matches bits; the little-endian reading was kept.</summary>
        Mismatch,
    }

    /// <summary>
    /// Same as <see cref="ResolveTarget(string, string)"/>, additionally reporting how the
    /// result was obtained so the caller can log the cases that deserve attention.
    /// </summary>
    public static uint256 ResolveTarget(string targetHex, string bitsHex, out TargetResolution resolution)
    {
        uint? bitsCompact = null;
        uint256 fromBits = null;

        if(!string.IsNullOrEmpty(bitsHex))
        {
            try
            {
                var t = new NBitcoin.Target(bitsHex.HexToByteArray());
                bitsCompact = t.ToCompact();
                fromBits = t.ToUInt256();
            }
            catch
            {
                // malformed bits: fall through to the target-only path
            }
        }

        if(string.IsNullOrEmpty(targetHex))
        {
            resolution = fromBits != null ? TargetResolution.FromBits : TargetResolution.None;
            return fromBits;
        }

        var bytes = targetHex.HexToByteArray();
        var asLittleEndian = new uint256(bytes);   // historical assumption

        if(bitsCompact == null)
        {
            resolution = TargetResolution.LittleEndianUnchecked;
            return asLittleEndian;
        }

        if(new NBitcoin.Target(asLittleEndian).ToCompact() == bitsCompact)
        {
            resolution = TargetResolution.LittleEndian;
            return asLittleEndian;
        }

        var reversed = (byte[]) bytes.Clone();
        Array.Reverse(reversed);
        var asBigEndian = new uint256(reversed);

        if(new NBitcoin.Target(asBigEndian).ToCompact() == bitsCompact)
        {
            resolution = TargetResolution.BigEndian;
            return asBigEndian;
        }

        resolution = TargetResolution.Mismatch;
        return asLittleEndian;
    }

    /// <summary>
    /// Surfaces the two cases in which merged mining for this chain would otherwise fail silently:
    /// no usable target at all (shares are never checked against the chain, see BitcoinJob), and a
    /// target that matches bits in neither byte order (the chain is probably unreachable by any share).
    /// Each is warned once per distinct daemon value, not once per poll.
    /// </summary>
    private void LogTargetResolution(AuxBlockData block, TargetResolution resolution)
    {
        string warningKey = null;

        switch(resolution)
        {
            case TargetResolution.None:
                warningKey = $"none:{block.Target}:{block.Bits}";
                if(lastTargetWarningKey != warningKey)
                    logger.Warn(() => $"[{config.Id}] {config.GetAuxBlockMethod} returned no usable target (target='{block.Target}', bits='{block.Bits}'): shares will not be checked against this chain until it does");
                break;

            case TargetResolution.Mismatch:
                warningKey = $"mismatch:{block.Target}:{block.Bits}";
                if(lastTargetWarningKey != warningKey)
                    logger.Warn(() => $"[{config.Id}] target '{block.Target}' does not re-compact to bits '{block.Bits}' in either byte order; keeping the little-endian reading. If this chain never finds a block, this is the likely cause");
                break;

            case TargetResolution.BigEndian:
                logger.Debug(() => $"[{config.Id}] target '{block.Target}' read as big-endian display hex (matches bits {block.Bits})");
                break;

            case TargetResolution.LittleEndianUnchecked:
                logger.Debug(() => $"[{config.Id}] target '{block.Target}' read as little-endian hex; bits '{block.Bits}' unavailable to confirm it");
                break;
        }

        lastTargetWarningKey = warningKey;
    }

    // Some daemons serialize integer fields (chainid, height) as JSON strings.
    // This helper tolerates both int and string tokens.
    private static int? ParseInt(JToken? token)
    {
        if(token == null || token.Type == JTokenType.Null)
            return null;
        if(token.Type == JTokenType.Integer)
            return token.Value<int>();
        if(token.Type == JTokenType.String && int.TryParse(token.Value<string>(), out var v))
            return v;
        return null;
    }
}
