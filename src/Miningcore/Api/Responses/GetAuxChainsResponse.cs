namespace Miningcore.Api.Responses;

public class AuxChainStat
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int ChainId { get; set; }
    public string Algorithm { get; set; }
    public double NetworkDifficulty { get; set; }
    public int BlockHeight { get; set; }
    public string[] PoolIds { get; set; }
    public double PoolHashrate { get; set; }
    public string ExplorerBlockLink { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }
}
