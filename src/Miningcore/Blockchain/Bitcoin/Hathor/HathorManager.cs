using System.Collections.Concurrent;
using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Miningcore.Extensions;
using Newtonsoft.Json.Linq;
using NLog;

namespace Miningcore.Blockchain.Bitcoin.Hathor;

/// <summary>
/// Manages Hathor (HTR) merge mining alongside a Bitcoin-family parent pool.
///
/// Hathor's protocol (RFC 0006) differs from standard AuxPoW:
///   - Templates via HTTP GET /v1a/get_block_template?address=...&capabilities=mergedmining
///   - The template's mining base hash (sha256d of the block-without-nonce) is committed in
///     the parent coinbase as an OP_RETURN push of "Hath" + hash — the magic must immediately
///     precede the hash and must not occur earlier in the coinbase.
///   - A winning share is submitted as hex(funds || graph || BitcoinAuxPow) via
///     POST /v1a/submit_block; the node validates the parent header hash against the
///     block weight (target = 2^(256-weight) - 1).
/// </summary>
public class HathorManager : IDisposable
{
    private readonly ILogger logger;
    private readonly HathorChainConfig config;
    private readonly HttpClient http;
    private readonly string baseUrl;
    private volatile AuxBlockData currentAuxBlock;
    private readonly SemaphoreSlim fetchLock = new(1, 1);
    private volatile bool invalidated = true;
    private long lastFetchTicks;
    private readonly TimeSpan fetchInterval;
    private long tipHeight;

    // Recent templates by base-hash so submits can find the funds/graph bytes even if
    // a newer template has since replaced currentWork.
    private readonly ConcurrentDictionary<string, HathorWork> recentWorks = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxRecentWorks = 16;

    public record HathorWork(string BaseHashHex, byte[] Funds, byte[] Graph, double Weight, long Height, DateTimeOffset FetchedAt);

    public HathorManager(HathorChainConfig config)
    {
        this.config = config;
        logger = LogManager.GetLogger(config.Id);

        if(config.Daemons == null || config.Daemons.Length == 0)
            throw new ArgumentException($"HathorChain '{config.Id}' has no daemons configured");

        if(string.IsNullOrEmpty(config.Address))
            throw new ArgumentException($"HathorChain '{config.Id}' has no payout address configured");

        var daemon = config.Daemons.First();
        var scheme = daemon.Ssl ? "https" : "http";
        baseUrl = $"{scheme}://{daemon.Host}:{daemon.Port}/v1a/";
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        fetchInterval = TimeSpan.FromSeconds(config.PollIntervalSeconds > 0 ? config.PollIntervalSeconds : 5);
    }

    public void Dispose()
    {
        fetchLock.Dispose();
        http.Dispose();
    }

    public HathorChainConfig Config => config;
    public AuxBlockData CurrentAuxBlock => currentAuxBlock;
    public long TipHeight => Interlocked.Read(ref tipHeight);

    /// <summary>
    /// Polls get_block_template if the cache has expired or been invalidated.
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
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));

            var url = $"{baseUrl}get_block_template?address={Uri.EscapeDataString(config.Address)}&capabilities=mergedmining";
            using var response = await http.GetAsync(url, cts.Token);

            if(!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cts.Token);
                LogError(() => $"[{config.Id}] get_block_template HTTP {(int) response.StatusCode}: {Truncate(body, 200)}");
                return;
            }

            var json = JObject.Parse(await response.Content.ReadAsStringAsync(cts.Token));

            // The node signals problems (e.g. still syncing) with {"error": ...} and 200 OK on some versions
            if(json["error"] != null && json["error"].Type != JTokenType.Null)
            {
                LogError(() => $"[{config.Id}] get_block_template error: {json["error"]}");
                return;
            }

            var (funds, graph, weight, height) = HathorSerializer.ParseTemplate(json);

            var version = json.Value<int?>("version") ?? 0;
            if(version != 3)
            {
                // MERGE_MINED_BLOCK = 3; anything else means the node ignored our capability flag
                LogError(() => $"[{config.Id}] get_block_template returned version={version}, expected 3 (merge-mined block)");
                return;
            }

            var outputs = json["outputs"] as JArray;
            var firstScript = outputs?.FirstOrDefault()?.Value<string>("script");
            if(string.IsNullOrEmpty(firstScript))
            {
                LogError(() => $"[{config.Id}] template output script empty — address {config.Address} not accepted by node?");
                return;
            }

            var baseHash = HathorSerializer.ComputeMiningBaseHash(funds, graph);
            var baseHashHex = baseHash.ToHexString();

            var targetBe = HathorSerializer.WeightToTargetBytes(weight);

            var work = new HathorWork(baseHashHex, funds, graph, weight, height, DateTimeOffset.UtcNow);
            recentWorks[baseHashHex] = work;
            TrimRecentWorks();

            currentAuxBlock = new AuxBlockData
            {
                // Raw sha256d bytes as hex; committed into the coinbase verbatim (no reversal)
                Hash = baseHashHex,
                ChainId = config.ChainId,
                Target = targetBe.ToHexString(),
                // uint256 stores little-endian internally; target bytes are big-endian
                TargetValue = new NBitcoin.uint256(targetBe.Reverse().ToArray()),
                Height = (int) Math.Min(height, int.MaxValue),
                FetchedAt = DateTimeOffset.UtcNow,
            };

            Interlocked.Exchange(ref tipHeight, height);
            Interlocked.Exchange(ref lastFetchTicks, DateTimeOffset.UtcNow.Ticks);
            invalidated = false;

            logger.Debug(() => $"[{config.Id}] Hathor work: baseHash={baseHashHex[..16]}... weight={weight:F2} height={height}");
        }
        catch(Exception ex)
        {
            LogError(() => $"[{config.Id}] get_block_template exception: {ex.Message}");
        }
        finally
        {
            fetchLock.Release();
        }
    }

    /// <summary>
    /// Submits a solved parent block as a Hathor merge-mined block.
    /// </summary>
    /// <param name="baseHashHex">The mining base hash the job committed (from AuxBlockData.Hash)</param>
    /// <param name="headerBytes">80-byte parent block header</param>
    /// <param name="coinbaseBytes">Full serialized parent coinbase transaction</param>
    /// <param name="merkleBranch">Coinbase-to-root merkle branch (internal byte order)</param>
    /// <returns>(accepted, hathorBlockHashHex, height)</returns>
    public async Task<(bool Accepted, string BlockHash, long Height)> SubmitBlockAsync(
        string baseHashHex, byte[] headerBytes, byte[] coinbaseBytes, IList<byte[]> merkleBranch, CancellationToken ct)
    {
        if(!recentWorks.TryGetValue(baseHashHex, out var work))
        {
            logger.Warn(() => $"[{config.Id}] No cached template for baseHash={baseHashHex[..16]}... — cannot submit");
            return (false, null, 0);
        }

        var baseHash = baseHashHex.HexToByteArray();

        // Split the coinbase at the base hash; the bytes before it must end with "Hath"
        var idx = IndexOf(coinbaseBytes, baseHash);
        if(idx < 4)
        {
            logger.Warn(() => $"[{config.Id}] base hash not found in coinbase — commitment missing?");
            return (false, null, 0);
        }

        var head = coinbaseBytes[..idx];
        var tail = coinbaseBytes[(idx + 32)..];

        if(!head[^4..].SequenceEqual(HathorSerializer.MagicNumber))
        {
            logger.Warn(() => $"[{config.Id}] coinbase head does not end with Hath magic — refusing submit");
            return (false, null, 0);
        }

        if(IndexOf(head[..^4], HathorSerializer.MagicNumber) >= 0)
        {
            logger.Warn(() => $"[{config.Id}] stray Hath magic earlier in coinbase — node would reject, refusing submit");
            return (false, null, 0);
        }

        // Hathor's BitcoinAuxPow expects merkle path links in DISPLAY (reversed) byte order —
        // its fold (_merkle_concat) reverses them back to internal before hashing. Our
        // MerkleBranchSteps are internal-order (stratum convention), so reverse each.
        var displayBranch = merkleBranch.Select(h => h.Reverse().ToArray()).ToList();

        var auxPow = HathorSerializer.BuildAuxPowBytes(headerBytes, head, tail, displayBranch);

        var blockBytes = new byte[work.Funds.Length + work.Graph.Length + auxPow.Length];
        work.Funds.CopyTo(blockBytes, 0);
        work.Graph.CopyTo(blockBytes, work.Funds.Length);
        auxPow.CopyTo(blockBytes, work.Funds.Length + work.Graph.Length);

        var hathorBlockHash = HathorSerializer.ComputeHathorBlockHash(headerBytes, head, baseHash, tail, displayBranch);
        var hathorBlockHashHex = hathorBlockHash.ToHexString();

        // Self-check: when coinbase + branch are consistent with the header, the reconstructed
        // hash equals the parent block hash in display order. A mismatch means the node will
        // reject with {"result":false} — log it loudly so the cause is visible.
        using(var sha = System.Security.Cryptography.SHA256.Create())
        {
            var parentDisplay = sha.ComputeHash(sha.ComputeHash(headerBytes)).Reverse().ToArray().ToHexString();
            if(!string.Equals(parentDisplay, hathorBlockHashHex, StringComparison.OrdinalIgnoreCase))
                logger.Warn(() => $"[{config.Id}] AuxPow reconstruction mismatch! parentHeaderHash={parentDisplay} reconstructed={hathorBlockHashHex} branchLen={merkleBranch.Count} — merkle branch does not match this coinbase/header");
            else
                logger.Info(() => $"[{config.Id}] AuxPow reconstruction verified: {parentDisplay[..16]}... (branchLen={merkleBranch.Count})");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var payload = new StringContent(
                new JObject { ["hexdata"] = blockBytes.ToHexString() }.ToString(Newtonsoft.Json.Formatting.None),
                System.Text.Encoding.UTF8, "application/json");

            using var response = await http.PostAsync($"{baseUrl}submit_block", payload, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);

            if(!response.IsSuccessStatusCode)
            {
                logger.Warn(() => $"[{config.Id}] submit_block HTTP {(int) response.StatusCode}: {Truncate(body, 300)}");
                return (false, hathorBlockHashHex, work.Height);
            }

            var json = JObject.Parse(body);
            var accepted = json.Value<bool?>("result") ?? false;

            if(accepted)
                logger.Info(() => $"[{config.Id}] Hathor merged block ACCEPTED! hash={hathorBlockHashHex[..16]}... height={work.Height}");
            else
            {
                logger.Warn(() => $"[{config.Id}] Hathor block rejected: {Truncate(body, 300)} (templateAge={(DateTimeOffset.UtcNow - work.FetchedAt).TotalSeconds:F1}s weight={work.Weight:F2})");
                // Full submitted payload for offline post-mortem
                logger.Info(() => $"[{config.Id}] rejected hexdata: {blockBytes.ToHexString()}");
            }

            invalidated = true;
            return (accepted, hathorBlockHashHex, work.Height);
        }
        catch(Exception ex)
        {
            logger.Warn(() => $"[{config.Id}] submit_block exception: {ex.Message}");
            return (false, hathorBlockHashHex, work.Height);
        }
    }

    /// <summary>
    /// Checks a submitted block's status for confirmation tracking.
    /// Returns (exists, voided, height).
    /// </summary>
    public async Task<(bool Exists, bool Voided, long Height)> CheckBlockAsync(string blockHashHex, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));

            using var response = await http.GetAsync($"{baseUrl}transaction?id={blockHashHex}", cts.Token);
            if(!response.IsSuccessStatusCode)
                return (false, false, 0);

            var json = JObject.Parse(await response.Content.ReadAsStringAsync(cts.Token));
            if(!(json.Value<bool?>("success") ?? false))
                return (false, false, 0);

            var meta = json["meta"] ?? json["tx"]?["metadata"];
            var voidedBy = meta?["voided_by"] as JArray;
            var voided = voidedBy is { Count: > 0 };
            var height = meta?.Value<long?>("height") ?? json["tx"]?["height"]?.Value<long?>() ?? 0;

            return (true, voided, height);
        }
        catch(Exception ex)
        {
            logger.Debug(() => $"[{config.Id}] CheckBlockAsync exception: {ex.Message}");
            return (false, false, 0);
        }
    }

    private void TrimRecentWorks()
    {
        if(recentWorks.Count <= MaxRecentWorks)
            return;

        foreach(var stale in recentWorks.Values
                    .OrderByDescending(w => w.FetchedAt)
                    .Skip(MaxRecentWorks)
                    .ToList())
            recentWorks.TryRemove(stale.BaseHashHex, out _);
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        return haystack.IndexOf(needle);
    }

    private void LogError(Func<string> message)
    {
        if(config.SilentErrors)
            logger.Debug(message);
        else
            logger.Warn(message);
    }

    private static string Truncate(string s, int len) => string.IsNullOrEmpty(s) || s.Length <= len ? s : s[..len];
}
