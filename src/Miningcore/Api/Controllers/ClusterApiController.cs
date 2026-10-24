using Autofac;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Blockchain;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using System.Collections.Concurrent;
using System.Globalization;
using NLog;

namespace Miningcore.Api.Controllers;

[Route("api")]
[ApiController]
public class ClusterApiController : ApiControllerBase
{
    public ClusterApiController(IComponentContext ctx) : base(ctx)
    {
        statsRepo = ctx.Resolve<IStatsRepository>();
        blocksRepo = ctx.Resolve<IBlockRepository>();
        auxBlocksRepo = ctx.Resolve<IAuxBlockRepository>();
        paymentsRepo = ctx.Resolve<IPaymentRepository>();
        clock = ctx.Resolve<IMasterClock>();
        pools = ctx.Resolve<ConcurrentDictionary<string, IMiningPool>>();
        enabledPools = new HashSet<string>(clusterConfig.Pools.Where(x => x.Enabled).Select(x => x.Id));
    }

    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private readonly IStatsRepository statsRepo;
    private readonly IBlockRepository blocksRepo;
    private readonly IAuxBlockRepository auxBlocksRepo;
    private readonly IPaymentRepository paymentsRepo;
    private readonly IMasterClock clock;
    private readonly ConcurrentDictionary<string, IMiningPool> pools;
    private readonly HashSet<string> enabledPools;

    #region Actions

    [HttpGet("blocks")]
    public async Task<Responses.Block[]> PageBlocksPagedAsync(
        [FromQuery] int page, [FromQuery] int pageSize = 15, [FromQuery] BlockStatus[] state = null)
    {
        pageSize = ClampPageSize(pageSize);
        page = ClampPage(page);
        var ct = HttpContext.RequestAborted;
        var blockStates = state is { Length: > 0 } ?
            state :
            new[] { BlockStatus.Confirmed, BlockStatus.Pending, BlockStatus.Orphaned };

        var blocks = (await cf.Run(con => blocksRepo.PageBlocksAsync(con, blockStates, page, pageSize, ct)))
            .Select(mapper.ToResponse)
            .Where(x => enabledPools.Contains(x.PoolId))
            .ToArray();

        // enrich blocks
        var blocksByPool = blocks.GroupBy(x => x.PoolId);

        foreach(var poolBlocks in blocksByPool)
        {
            var pool = GetPoolNoThrow(poolBlocks.Key);

            if(pool == null)
                continue;

            var blockInfobaseDict = pool.Template.ExplorerBlockLinks;

            // compute infoLink
            if(blockInfobaseDict != null)
            {
                foreach(var block in poolBlocks)
                {
                    blockInfobaseDict.TryGetValue(!string.IsNullOrEmpty(block.Type) ? block.Type : "block", out var blockInfobaseUrl);

                    if(!string.IsNullOrEmpty(blockInfobaseUrl))
                    {
                        if(blockInfobaseUrl.Contains(CoinMetaData.BlockHeightPH))
                            block.InfoLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHeightPH, block.BlockHeight.ToString(CultureInfo.InvariantCulture));
                        else if(blockInfobaseUrl.Contains(CoinMetaData.BlockHashPH) && !string.IsNullOrEmpty(block.Hash))
                            block.InfoLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHashPH, block.Hash);
                    }
                }
            }
        }

        return blocks;
    }

    [HttpGet("auxblocks")]
    public async Task<Responses.AuxBlock[]> PageAuxBlocksAsync(
        [FromQuery] int page, [FromQuery] int pageSize = 15, [FromQuery] BlockStatus[] state = null)
    {
        pageSize = ClampPageSize(pageSize);
        page = ClampPage(page);
        var ct = HttpContext.RequestAborted;
        var blockStates = state is { Length: > 0 } ?
            state :
            new[] { BlockStatus.Confirmed, BlockStatus.Pending, BlockStatus.Orphaned };

        return (await cf.Run(con => auxBlocksRepo.PageAuxBlocksAsync(con, blockStates, page, pageSize, ct)))
            .Select(mapper.ToResponse)
            .Where(x => enabledPools.Contains(x.PoolId))
            .ToArray();
    }

    [HttpGet("auxchains")]
    public Responses.AuxChainStat[] GetAuxChains()
    {
        var sources = new List<AuxChainSource>();

        foreach(var poolConfig in clusterConfig.Pools.Where(x => x.Enabled))
        {
            if(!pools.TryGetValue(poolConfig.Id, out var pool)) continue;
            if(pool is not IAuxMiningPool auxPool) continue;

            var algorithm = poolConfig.Template?.GetAlgorithmName() ?? "unknown";
            var poolHashrate = pool.PoolStats?.PoolHashrate ?? 0;

            // Rootstock and Hathor are merge-mined outside the AuxPoW tree, so they are not in AuxManagers; give them a
            // card too. Neither daemon reports a network hashrate, so it is estimated from the difficulty.
            if(auxPool.RskManager is { } rsk)
                sources.Add(new AuxChainSource
                {
                    PoolId = poolConfig.Id, Algorithm = algorithm, PoolHashrate = poolHashrate,
                    Id = rsk.Config.Id, Name = rsk.Config.Name, ChainId = rsk.Config.ChainId,
                    ExplorerBlockLink = rsk.Config.ExplorerBlockLink, Block = rsk.CurrentAuxBlock,
                    EstimateHashrateBlockTimeSeconds = rsk.Config.BlockTimeSeconds,
                });

            if(auxPool.HathorManager is { } hathor)
                sources.Add(new AuxChainSource
                {
                    PoolId = poolConfig.Id, Algorithm = algorithm, PoolHashrate = poolHashrate,
                    Id = hathor.Config.Id, Name = hathor.Config.Name, ChainId = hathor.Config.ChainId,
                    ExplorerBlockLink = hathor.Config.ExplorerBlockLink, Block = hathor.CurrentAuxBlock,
                    EstimateHashrateBlockTimeSeconds = hathor.Config.BlockTimeSeconds,
                });

            foreach(var mgr in auxPool.AuxManagers)
                sources.Add(new AuxChainSource
                {
                    PoolId = poolConfig.Id, Algorithm = algorithm, PoolHashrate = poolHashrate,
                    Id = mgr.Config.Id, Name = mgr.Config.Name, ChainId = mgr.Config.ChainId,
                    ExplorerBlockLink = mgr.Config.ExplorerBlockLink, Block = mgr.CurrentAuxBlock,
                    Diff1 = mgr.Config.Diff1,
                    NetworkHashrate = mgr.NetworkHashrate, NetworkHashrateAt = mgr.NetworkHashrateAt,
                });
        }

        return AuxChainStatsBuilder.Build(sources);
    }

    #endregion // Actions
}
