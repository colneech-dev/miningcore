namespace Miningcore.Api.Responses;

public class AuxBlock
{
    public string PoolId { get; set; }
    public string ChainId { get; set; }
    public string ChainName { get; set; }
    public ulong? BlockHeight { get; set; }
    public string AuxBlockHash { get; set; }
    public string ParentBlockHash { get; set; }
    public string Status { get; set; }
    public double ConfirmationProgress { get; set; }
    public decimal? Reward { get; set; }
    public string Miner { get; set; }
    public string Worker { get; set; }
    public string Source { get; set; }
    public double? Difficulty { get; set; }
    public double? NetworkDifficulty { get; set; }
    public string SubmittedVia { get; set; }
    public string InfoLink { get; set; }
    public int RequiredConfirmations { get; set; }
    public DateTime Created { get; set; }
}
