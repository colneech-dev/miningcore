namespace Miningcore.Persistence.Model.Projections;

/// <summary>Per aux-chain aggregate for /api/summary.</summary>
public class AuxChainSummary
{
    public string ChainId { get; set; }
    public long Total { get; set; }
    public long Today { get; set; }
    public DateTime? LastCreated { get; set; }
    public decimal? LastReward { get; set; }
}
