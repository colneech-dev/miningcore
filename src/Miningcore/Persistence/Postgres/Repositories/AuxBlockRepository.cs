using System.Data;
using AutoMapper;
using Dapper;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;

namespace Miningcore.Persistence.Postgres.Repositories;

public class AuxBlockRepository : IAuxBlockRepository
{
    public AuxBlockRepository(IMapper mapper)
    {
        this.mapper = mapper;
    }

    private readonly IMapper mapper;

    public async Task<bool> InsertAsync(IDbConnection con, IDbTransaction tx, AuxBlock block)
    {
        var mapped = mapper.Map<Entities.AuxBlock>(block);

        const string query = @"
            INSERT INTO auxblocks(poolid, chainid, chainname, blockheight, auxblockhash, parentblockhash,
                status, confirmationprogress, reward, miner, worker, source, submittedvia, created)
            VALUES(@poolid, @chainid, @chainname, @blockheight, @auxblockhash, @parentblockhash,
                @status, @confirmationprogress, @reward, @miner, @worker, @source, @submittedvia, @created)
            ON CONFLICT (poolid, chainid, auxblockhash) DO NOTHING";

        var rows = await con.ExecuteAsync(query, mapped, tx);
        return rows > 0;
    }

    public async Task<AuxBlock[]> PageAuxBlocksAsync(IDbConnection con, BlockStatus[] status, int page, int pageSize, CancellationToken ct)
    {
        const string query = @"SELECT * FROM auxblocks WHERE status = ANY(@status)
            ORDER BY created DESC OFFSET @offset FETCH NEXT @pageSize ROWS ONLY";

        return (await con.QueryAsync<Entities.AuxBlock>(new CommandDefinition(query, new
        {
            status = status.Select(x => x.ToString().ToLower()).ToArray(),
            offset = page * pageSize,
            pageSize
        }, cancellationToken: ct)))
            .Select(mapper.Map<AuxBlock>)
            .ToArray();
    }

    public async Task<AuxBlock[]> PagePoolAuxBlocksAsync(IDbConnection con, string poolId, BlockStatus[] status, int page, int pageSize, CancellationToken ct)
    {
        const string query = @"SELECT * FROM auxblocks WHERE poolid = @poolId AND status = ANY(@status)
            ORDER BY created DESC OFFSET @offset FETCH NEXT @pageSize ROWS ONLY";

        return (await con.QueryAsync<Entities.AuxBlock>(new CommandDefinition(query, new
        {
            poolId,
            status = status.Select(x => x.ToString().ToLower()).ToArray(),
            offset = page * pageSize,
            pageSize
        }, cancellationToken: ct)))
            .Select(mapper.Map<AuxBlock>)
            .ToArray();
    }

    public async Task<AuxBlock[]> PageMinerAuxBlocksAsync(IDbConnection con, string poolId, string address, BlockStatus[] status, int page, int pageSize, CancellationToken ct)
    {
        const string query = @"SELECT * FROM auxblocks WHERE poolid = @poolId AND miner = @address AND status = ANY(@status)
            ORDER BY created DESC OFFSET @offset FETCH NEXT @pageSize ROWS ONLY";

        return (await con.QueryAsync<Entities.AuxBlock>(new CommandDefinition(query, new
        {
            poolId,
            address,
            status = status.Select(x => x.ToString().ToLower()).ToArray(),
            offset = page * pageSize,
            pageSize
        }, cancellationToken: ct)))
            .Select(mapper.Map<AuxBlock>)
            .ToArray();
    }

    public async Task<AuxBlock[]> GetPendingAsync(IDbConnection con, string poolId, string chainId, int limit, CancellationToken ct)
    {
        const string query = @"SELECT * FROM auxblocks WHERE poolid = @poolId AND chainid = @chainId
            AND status = 'pending' ORDER BY created ASC LIMIT @limit";

        return (await con.QueryAsync<Entities.AuxBlock>(new CommandDefinition(query, new
        {
            poolId, chainId, limit
        }, cancellationToken: ct)))
            .Select(mapper.Map<AuxBlock>)
            .ToArray();
    }

    public async Task<AuxBlock[]> GetRecentlyConfirmedAsync(IDbConnection con, string poolId, string chainId, TimeSpan window, int limit, CancellationToken ct)
    {
        const string query = @"SELECT * FROM auxblocks WHERE poolid = @poolId AND chainid = @chainId
            AND status = 'confirmed' AND blockheight IS NOT NULL AND created >= @since
            ORDER BY created DESC LIMIT @limit";

        return (await con.QueryAsync<Entities.AuxBlock>(new CommandDefinition(query, new
        {
            poolId, chainId,
            since = DateTime.UtcNow - window,
            limit
        }, cancellationToken: ct)))
            .Select(mapper.Map<AuxBlock>)
            .ToArray();
    }

    public async Task<AuxBlock[]> GetBlocksWithoutHeightAsync(IDbConnection con, string poolId, string chainId, int limit, CancellationToken ct)
    {
        const string query = @"SELECT * FROM auxblocks WHERE poolid = @poolId AND chainid = @chainId
            AND blockheight IS NULL ORDER BY created ASC LIMIT @limit";

        return (await con.QueryAsync<Entities.AuxBlock>(new CommandDefinition(query, new
        {
            poolId, chainId, limit
        }, cancellationToken: ct)))
            .Select(mapper.Map<AuxBlock>)
            .ToArray();
    }

    public async Task UpdateAsync(IDbConnection con, IDbTransaction tx, AuxBlock block)
    {
        var mapped = mapper.Map<Entities.AuxBlock>(block);

        const string query = @"UPDATE auxblocks SET status = @status, confirmationprogress = @confirmationprogress,
            blockheight = @blockheight WHERE id = @id";

        await con.ExecuteAsync(query, mapped, tx);
    }

    public Task<int> GetPoolAuxBlockCountAsync(IDbConnection con, string poolId, CancellationToken ct)
    {
        const string query = @"SELECT COUNT(*) FROM auxblocks WHERE poolid = @poolId";
        return con.ExecuteScalarAsync<int>(new CommandDefinition(query, new { poolId }, cancellationToken: ct));
    }

    public Task<int> GetPoolAuxBlockCountSinceAsync(IDbConnection con, string poolId, DateTime since, CancellationToken ct)
    {
        const string query = @"SELECT COUNT(*) FROM auxblocks WHERE poolid = @poolId AND created >= @since";
        return con.ExecuteScalarAsync<int>(new CommandDefinition(query, new { poolId, since }, cancellationToken: ct));
    }
}
