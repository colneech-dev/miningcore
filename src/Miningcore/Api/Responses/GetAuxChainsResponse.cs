namespace Miningcore.Api.Responses;

public class AuxChainStat
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int ChainId { get; set; }
    public string Algorithm { get; set; }
    /// <summary>Network difficulty in the chain's own units (its configured diff1, Bitcoin's when none is set).</summary>
    public double NetworkDifficulty { get; set; }

    /// <summary>Network hashrate in H/s as reported by the aux daemon's getnetworkhashps; null when unknown.</summary>
    public double? NetworkHashrate { get; set; }
    public int BlockHeight { get; set; }
    public string[] PoolIds { get; set; }
    public double PoolHashrate { get; set; }
    public string ExplorerBlockLink { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }
}
