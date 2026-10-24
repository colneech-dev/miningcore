using Newtonsoft.Json;

namespace Miningcore.Persistence.Model;

public enum PayoutBatchStatus
{
    /// <summary>Recorded before the send. If the process dies here the outcome is unknown.</summary>
    Pending,
    /// <summary>The wallet accepted the send; payments and balances may not be persisted yet.</summary>
    Sent,
    /// <summary>Payments inserted and balances debited.</summary>
    Completed,
    /// <summary>The wallet rejected the send; nothing left the wallet, balances untouched.</summary>
    Failed,
}

/// <summary>
/// A payout "intent" written before coins leave the wallet. It is what makes payouts idempotent:
/// a batch that is Sent but not Completed is finished on the next run instead of being paid again,
/// and a batch still Pending after a crash halts payouts for its pool until an operator says what
/// happened. See docs/decisions.md, 2026-10-18 "Payout idempotency".
/// </summary>
public class PayoutBatch
{
    public long Id { get; set; }
    public string PoolId { get; set; }
    public PayoutBatchStatus Status { get; set; }

    /// <summary>JSON array of { address, amount }, see <see cref="PayoutBatchRecipients"/>.</summary>
    public string Recipients { get; set; }

    public string TransactionConfirmationData { get; set; }
    public string Error { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
}

public record PayoutBatchRecipient(string Address, decimal Amount);

public static class PayoutBatchRecipients
{
    public static string Serialize(IEnumerable<Balance> balances) =>
        JsonConvert.SerializeObject(balances.Select(x => new PayoutBatchRecipient(x.Address, x.Amount)).ToArray());

    public static Balance[] Deserialize(string poolId, string json) =>
        (JsonConvert.DeserializeObject<PayoutBatchRecipient[]>(json ?? "[]") ?? Array.Empty<PayoutBatchRecipient>())
            .Select(x => new Balance { PoolId = poolId, Address = x.Address, Amount = x.Amount })
            .ToArray();
}
