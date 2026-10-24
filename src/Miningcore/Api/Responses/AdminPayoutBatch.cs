namespace Miningcore.Api.Responses;

public class AdminPayoutBatch
{
    public long Id { get; set; }
    public string PoolId { get; set; }
    public string Status { get; set; }
    public Persistence.Model.PayoutBatchRecipient[] Recipients { get; set; }
    public decimal Total { get; set; }
    public string TransactionId { get; set; }
    public string Error { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
}
