namespace Miningcore.Persistence.Postgres.Entities;

public class PayoutBatch
{
    public long Id { get; set; }
    public string PoolId { get; set; }
    public string Status { get; set; }
    public string Recipients { get; set; }
    public string TransactionConfirmationData { get; set; }
    public string Error { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
}
