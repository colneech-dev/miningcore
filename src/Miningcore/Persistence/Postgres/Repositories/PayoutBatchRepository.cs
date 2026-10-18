using System.Data;
using Dapper;
using Miningcore.Mapping;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;

namespace Miningcore.Persistence.Postgres.Repositories;

public class PayoutBatchRepository : IPayoutBatchRepository
{
    public PayoutBatchRepository(MiningcoreMapper mapper)
    {
        this.mapper = mapper;
    }

    private readonly MiningcoreMapper mapper;

    public Task<long> InsertAsync(IDbConnection con, IDbTransaction tx, PayoutBatch batch)
    {
        var mapped = mapper.ToEntity(batch);

        const string query = @"INSERT INTO payoutbatches(poolid, status, recipients, transactionconfirmationdata, error, created, updated)
            VALUES(@poolid, @status, @recipients, @transactionconfirmationdata, @error, @created, @updated)
            RETURNING id";

        return con.ExecuteScalarAsync<long>(query, mapped, tx);
    }

    public async Task UpdateStatusAsync(IDbConnection con, IDbTransaction tx, long id, PayoutBatchStatus status, string transactionConfirmationData, string error, DateTime updated)
    {
        const string query = @"UPDATE payoutbatches SET status = @status, error = @error, updated = @updated,
            transactionconfirmationdata = COALESCE(@transactionConfirmationData, transactionconfirmationdata)
            WHERE id = @id";

        await con.ExecuteAsync(query, new { id, status = status.ToString().ToLowerInvariant(), transactionConfirmationData, error, updated }, tx);
    }

    public async Task<PayoutBatch> GetAsync(IDbConnection con, string poolId, long id, CancellationToken ct)
    {
        const string query = "SELECT * FROM payoutbatches WHERE poolid = @poolId AND id = @id";

        var entity = await con.QuerySingleOrDefaultAsync<Entities.PayoutBatch>(new CommandDefinition(query, new { poolId, id }, cancellationToken: ct));
        return entity == null ? null : mapper.ToModel(entity);
    }

    public async Task<PayoutBatch[]> GetByStatusAsync(IDbConnection con, string poolId, PayoutBatchStatus[] statuses, CancellationToken ct)
    {
        const string query = "SELECT * FROM payoutbatches WHERE poolid = @poolId AND status = ANY(@status) ORDER BY id";

        return (await con.QueryAsync<Entities.PayoutBatch>(new CommandDefinition(query, new
        {
            poolId,
            status = statuses.Select(x => x.ToString().ToLowerInvariant()).ToArray()
        }, cancellationToken: ct)))
            .Select(mapper.ToModel)
            .ToArray();
    }

    public async Task<PayoutBatch[]> PageAsync(IDbConnection con, string poolId, int page, int pageSize, CancellationToken ct)
    {
        const string query = @"SELECT * FROM payoutbatches WHERE poolid = @poolId
            ORDER BY id DESC OFFSET @offset FETCH NEXT @pageSize ROWS ONLY";

        return (await con.QueryAsync<Entities.PayoutBatch>(new CommandDefinition(query, new
        {
            poolId,
            offset = page * pageSize,
            pageSize
        }, cancellationToken: ct)))
            .Select(mapper.ToModel)
            .ToArray();
    }
}
