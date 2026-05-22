namespace Miningcore.Api.Responses;

public class SummaryPoolCoin
{
    public string Symbol { get; set; }
    public string Algorithm { get; set; }
}

public class SummaryMergeMineEntry
{
    public string Id { get; set; }
    public string Name { get; set; }
}

public class SummaryPoolEntry
{
    public string Id { get; set; }
    public string Scheme { get; set; }
    public int ConnectedMiners { get; set; }
    public int ConnectedWorkers { get; set; }
    public double PoolHashrate { get; set; }
    public double NetworkHashrate { get; set; }
    public double NetworkDifficulty { get; set; }
    public long BlockHeight { get; set; }
    public double? PoolEffort { get; set; }
    public DateTime? LastPoolBlockTime { get; set; }
    public decimal BlockReward { get; set; }
    public SummaryPoolCoin Coin { get; set; }
    public int BlocksToday { get; set; }
    public uint TotalBlocks { get; set; }
    public int AuxBlocksToday { get; set; }
    public int TotalAuxBlocks { get; set; }
    public float Fee { get; set; }
    public decimal MinimumPayment { get; set; }
    public SummaryMergeMineEntry[] MergeMinedCoins { get; set; }
}

public class SummaryTotals
{
    public double TotalHashrate { get; set; }
    public int TotalMiners { get; set; }
    public int TotalWorkers { get; set; }
    public int ActivePools { get; set; }
    public int PoolCount { get; set; }
    public long TotalBlocksAllTime { get; set; }
    public int TotalBlocksToday { get; set; }
    public int TotalAuxBlocksAllTime { get; set; }
    public int TotalAuxBlocksToday { get; set; }
}

public class GetSummaryResponse
{
    public DateTime GeneratedAt { get; set; }
    public bool Stale { get; set; }
    public SummaryPoolEntry[] Pools { get; set; }
    public SummaryTotals Totals { get; set; }
}
