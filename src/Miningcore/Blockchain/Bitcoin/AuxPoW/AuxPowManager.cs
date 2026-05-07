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
    private readonly TimeSpan fetchInterval;

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
        var elapsed = TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks - Interlocked.Read(ref lastFetchTicks));
        if(!auxBlockInvalidated && elapsed < fetchInterval)
            return;

        if(!await fetchLock.WaitAsync(0, ct))
            return;

        try
        {
            var method = config.GetAuxBlockMethod;
            object args = method == "createauxblock" && !string.IsNullOrEmpty(config.Address)
                ? new object[] { config.Address }
                : null;

            var response = await rpc.ExecuteAsync<JToken>(logger, method, ct, args);

            if(response.Error != null)
            {
                if(config.SilentErrors)
                    logger.Debug(() => $"[{config.Id}] {method} failed: {response.Error.Message}");
                else
                    logger.Warn(() => $"[{config.Id}] {method} failed: {response.Error.Message}");
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
                block.TargetValue = new uint256(block.Target.HexToByteArray()); // target is already little-endian (internal byte order)
            else if(!string.IsNullOrEmpty(block.Bits))
            {
                var tmp = new NBitcoin.Target(block.Bits.HexToByteArray());
                block.TargetValue = tmp.ToUInt256();
            }

            currentAuxBlock = block;
            Interlocked.Exchange(ref lastFetchTicks, DateTimeOffset.UtcNow.Ticks);
            auxBlockInvalidated = false;

            logger.Debug(() => $"[{config.Id}] AuxBlock refreshed: height={block.Height} target={block.Target}");
        }
        catch(Exception ex)
        {
            if(config.SilentErrors)
                logger.Debug(() => $"[{config.Id}] {config.GetAuxBlockMethod} exception: {ex.Message}");
            else
                logger.Warn(() => $"[{config.Id}] {config.GetAuxBlockMethod} exception: {ex.Message}");
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

        // Some daemons return null/omit result on success; only an explicit boolean false means rejection
        var accepted = response.Response == null
            || response.Response.Type == JTokenType.Null
            || (response.Response.Type == JTokenType.Boolean && response.Response.Value<bool>());

        if(accepted)
            logger.Info(() => $"[{config.Id}] Merged block accepted by {config.Name}!");
        else
            logger.Warn(() => $"[{config.Id}] Merged block rejected by {config.Name}");

        // Invalidate current aux block to force refresh on next share
        auxBlockInvalidated = true;

        return accepted;
    }
}
