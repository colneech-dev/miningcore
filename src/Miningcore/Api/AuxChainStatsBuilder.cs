using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Miningcore.Extensions;
using NLog;

namespace Miningcore.Api;

/// <summary>One merge-mined chain as one pool sees it.</summary>
public record AuxChainSource
{
    public string PoolId { get; init; }
    public string Algorithm { get; init; }
    public double PoolHashrate { get; init; }

    public string Id { get; init; }
    public string Name { get; init; }
    public int ChainId { get; init; }
    public string ExplorerBlockLink { get; init; }

    public AuxBlockData Block { get; init; }

    /// <summary>Difficulty-1 target as hex for chains not measured against Bitcoin's; null for the rest.</summary>
    public string Diff1 { get; init; }

    /// <summary>Hashrate the daemon reported, and when. Null for chains whose daemon reports none.</summary>
    public double? NetworkHashrate { get; init; }
    public DateTimeOffset? NetworkHashrateAt { get; init; }

    /// <summary>Set for chains whose daemon reports no hashrate (Rootstock, Hathor): it is estimated from the
    /// difficulty and this target block time instead.</summary>
    public int? EstimateHashrateBlockTimeSeconds { get; init; }
}

/// <summary>
/// Folds every pool's view of every merge-mined chain into the entries served by /api/auxchains.
/// Entries are keyed by chain AND pool algorithm: a chain id can sit behind different daemons in different lanes
/// (Myriadcoin runs a SHA-256 and a Scrypt node under one id), and merging them showed whichever lane polled last.
/// </summary>
public static class AuxChainStatsBuilder
{
    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    public static Responses.AuxChainStat[] Build(IEnumerable<AuxChainSource> sources)
    {
        var result = new Dictionary<string, Responses.AuxChainStat>();
        var hashrateAt = new Dictionary<string, DateTimeOffset>();

        foreach(var src in sources)
        {
            var key = src.Id + "|" + src.Algorithm;

            if(!result.TryGetValue(key, out var stat))
            {
                stat = new Responses.AuxChainStat
                {
                    Id = src.Id,
                    Name = src.Name,
                    ChainId = src.ChainId,
                    Algorithm = src.Algorithm,
                    ExplorerBlockLink = src.ExplorerBlockLink,
                    PoolIds = Array.Empty<string>(),
                };
                result[key] = stat;
            }

            stat.PoolIds = stat.PoolIds.Append(src.PoolId).ToArray();
            stat.PoolHashrate += src.PoolHashrate;

            ApplyBlock(stat, src.Block, src.Diff1);

            if(src.EstimateHashrateBlockTimeSeconds is > 0 && stat.NetworkDifficulty > 0)
                stat.NetworkHashrate = stat.NetworkDifficulty * 4294967296.0 / src.EstimateHashrateBlockTimeSeconds.Value;

            // Every pool that merge-mines the chain has its own manager; keep the freshest hashrate reading.
            if(src.NetworkHashrate != null && src.NetworkHashrateAt != null &&
               (!hashrateAt.TryGetValue(key, out var seen) || src.NetworkHashrateAt > seen))
            {
                hashrateAt[key] = src.NetworkHashrateAt.Value;
                stat.NetworkHashrate = src.NetworkHashrate;
            }
        }

        return result.Values.OrderBy(x => x.Name).ThenBy(x => x.Algorithm).ToArray();
    }

    // Difficulty in the chain's own units: the target the pool actually mines against, measured against the chain's
    // diff1 (Bitcoin's when none is set). Falls back to the compact bits when the daemon gave no usable target.
    private static void ApplyBlock(Responses.AuxChainStat stat, AuxBlockData block, string diff1Hex)
    {
        if(block == null || (stat.FetchedAt != null && block.FetchedAt <= stat.FetchedAt))
            return;

        stat.BlockHeight = block.Height;
        stat.FetchedAt = block.FetchedAt;

        var difficulty = DifficultyUnits.TargetDifficulty(block.TargetValue,
            DifficultyUnits.TryParseDiff1(diff1Hex, out var diff1) ? diff1 : BitcoinConstants.Diff1);

        if(difficulty == null && !string.IsNullOrEmpty(block.Bits))
        {
            try
            {
                difficulty = new global::NBitcoin.Target(block.Bits.HexToByteArray()).Difficulty;
            }
            catch(Exception ex)
            {
                logger.Debug(ex);   // best-effort enrichment; the response is still served without it
            }
        }

        if(difficulty != null)
            stat.NetworkDifficulty = difficulty.Value;
    }
}
