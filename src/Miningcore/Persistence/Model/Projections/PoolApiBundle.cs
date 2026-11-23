using Miningcore.Persistence.Model;

namespace Miningcore.Persistence.Model.Projections;

/// <summary>Everything /api/pools and /api/pools/{id} need per pool, fetched over one
/// connection and one network round trip (QueryMultipleAsync) instead of 5 separate ones.</summary>
public class PoolApiBundle
{
    public PoolStats Stats { get; set; }
    public decimal TotalPaid { get; set; }
    public PoolBlockSummary BlockSummary { get; set; }
    public PoolAuxBlockSummary AuxSummary { get; set; }
    public MinerWorkerPerformanceStats[] TopMiners { get; set; }
}
