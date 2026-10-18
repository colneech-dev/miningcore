using System.Net;
using Autofac;
using Miningcore.Mapping;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Configuration;
using Miningcore.Persistence;

namespace Miningcore.Api.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected ApiControllerBase(IComponentContext ctx)
    {
        mapper = ctx.Resolve<MiningcoreMapper>();
        clusterConfig = ctx.Resolve<ClusterConfig>();
        cf = ctx.Resolve<IConnectionFactory>();
    }

    protected readonly ClusterConfig clusterConfig;
    protected readonly IConnectionFactory cf;
    protected readonly MiningcoreMapper mapper;

    /// <summary>
    /// Upper bound for the pageSize query parameter. It used to flow straight into FETCH NEXT, so
    /// any client could ask for millions of rows and hold a connection and a worker.
    /// </summary>
    public const int MaxPageSize = 500;

    protected static int ClampPageSize(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);

    /// <summary>Page index bound: a negative page is a negative OFFSET (database error) and page * pageSize is an unchecked int multiply.</summary>
    protected static int ClampPage(int page) => Math.Clamp(page, 0, 1_000_000);

    protected PoolConfig GetPoolNoThrow(string poolId)
    {
        if(string.IsNullOrEmpty(poolId))
            return null;

        var pool = clusterConfig.Pools.FirstOrDefault(x => x.Id == poolId && x.Enabled);
        return pool;
    }

    protected PoolConfig GetPool(string poolId)
    {
        if(string.IsNullOrEmpty(poolId))
            throw new ApiException("Invalid pool id", HttpStatusCode.NotFound);

        var pool = clusterConfig.Pools.FirstOrDefault(x => x.Id == poolId && x.Enabled);

        if(pool == null)
            throw new ApiException($"Unknown pool {poolId}", HttpStatusCode.NotFound);

        return pool;
    }
}
