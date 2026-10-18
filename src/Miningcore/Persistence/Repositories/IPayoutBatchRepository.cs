using System.Data;
using Miningcore.Persistence.Model;

namespace Miningcore.Persistence.Repositories;

public interface IPayoutBatchRepository
{
    /// <summary>Inserts the batch and returns its id.</summary>
    Task<long> InsertAsync(IDbConnection con, IDbTransaction tx, PayoutBatch batch);

    /// <summary>
    /// Compare-and-set: moves the batch to <paramref name="status"/> only if it is currently in one of
    /// <paramref name="fromStatuses"/>, and returns the number of rows changed (0 = it was not). A null
    /// transactionConfirmationData or error keeps the stored value. The guard stops an operator resolving a
    /// batch from overwriting one a handler has just completed, and makes retried transactions harmless.
    /// </summary>
    Task<int> UpdateStatusAsync(IDbConnection con, IDbTransaction tx, long id, PayoutBatchStatus status, string transactionConfirmationData, string error, DateTime updated, params PayoutBatchStatus[] fromStatuses);

    Task<PayoutBatch> GetAsync(IDbConnection con, string poolId, long id, CancellationToken ct);
    Task<PayoutBatch[]> GetByStatusAsync(IDbConnection con, string poolId, PayoutBatchStatus[] statuses, CancellationToken ct);
    Task<PayoutBatch[]> PageAsync(IDbConnection con, string poolId, int page, int pageSize, CancellationToken ct);
}
