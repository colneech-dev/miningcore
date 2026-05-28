namespace Miningcore.Persistence.Postgres.Entities;

public class AuxBlock
{
    public long Id { get; set; }
    public string PoolId { get; set; }
    public string ChainId { get; set; }
    public string ChainName { get; set; }
    public long? BlockHeight { get; set; }
    public string AuxBlockHash { get; set; }
    public string ParentBlockHash { get; set; }
    public string Status { get; set; }
    public double ConfirmationProgress { get; set; }
    public decimal? Reward { get; set; }
    public string Miner { get; set; }
    public string Worker { get; set; }
    public string Source { get; set; }
    public string SubmittedVia { get; set; }
    public double? Difficulty { get; set; }
    public DateTime Created { get; set; }
}
