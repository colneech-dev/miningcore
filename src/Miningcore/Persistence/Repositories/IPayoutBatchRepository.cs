using System.Data;
using Miningcore.Persistence.Model;

namespace Miningcore.Persistence.Repositories;

public interface IPayoutBatchRepository
{
    /// <summary>Inserts the batch and returns its id.</summary>
    Task<long> InsertAsync(IDbConnection con, IDbTransaction tx, PayoutBatch batch);

    /// <summary>Sets status, error and updated; a null transactionConfirmationData keeps the stored one.</summary>
    Task UpdateStatusAsync(IDbConnection con, IDbTransaction tx, long id, PayoutBatchStatus status, string transactionConfirmationData, string error, DateTime updated);

    Task<PayoutBatch> GetAsync(IDbConnection con, string poolId, long id, CancellationToken ct);
    Task<PayoutBatch[]> GetByStatusAsync(IDbConnection con, string poolId, PayoutBatchStatus[] statuses, CancellationToken ct);
    Task<PayoutBatch[]> PageAsync(IDbConnection con, string poolId, int page, int pageSize, CancellationToken ct);
}
