using Autofac;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Persistence.Repositories;
using Miningcore.Util;
using System.Collections.Concurrent;
using System.Net;
using NLog;

namespace Miningcore.Api.Controllers;

[Route("api/admin")]
[ApiController]
public class AdminApiController : ApiControllerBase
{
    public AdminApiController(IComponentContext ctx) : base(ctx)
    {
        gcStats = ctx.Resolve<Responses.AdminGcStats>();
        minerRepo = ctx.Resolve<IMinerRepository>();
        pools = ctx.Resolve<ConcurrentDictionary<string, IMiningPool>>();
        paymentsRepo = ctx.Resolve<IPaymentRepository>();
        balanceRepo = ctx.Resolve<IBalanceRepository>();
        payoutBatchRepo = ctx.Resolve<IPayoutBatchRepository>();
    }

    private readonly IPayoutBatchRepository payoutBatchRepo;

    private readonly IPaymentRepository paymentsRepo;
    private readonly IBalanceRepository balanceRepo;
    private readonly IMinerRepository minerRepo;
    private readonly ConcurrentDictionary<string, IMiningPool> pools;

    private readonly Responses.AdminGcStats gcStats;

    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    #region Actions

    // POST: this changes server state. GET mutations are cacheable and trivially cross-site requestable.
    [HttpPost("logging/level/{level}")]
    public ActionResult<string> SetLoggingLevel(string level)
    {
        if (string.IsNullOrEmpty(level))
            throw new ApiException("Invalid logging level", HttpStatusCode.BadRequest);

        var logLevel = LogLevel.FromString(level);

        if (logLevel == null)
            throw new ApiException("Invalid logging level", HttpStatusCode.BadRequest);

        foreach (var rule in LogManager.Configuration.LoggingRules)
        {
            rule.EnableLoggingForLevel(logLevel);
            rule.SetLoggingLevels(logLevel, LogLevel.Fatal); // set minimum logging level
        }

        // The existing rules were re-levelled above. This used to also append a brand-new rule on
        // every call, so repeated calls accumulated duplicate rules and duplicate log lines.
        LogManager.ReconfigExistingLoggers();

        logger.Info($"Logging level set to {level}");
        return "Ok";
    }

    [HttpPost("payment/processing/enable")]
    public ActionResult<string> EnablePoolsPaymentProcessing()
    {
        var poolIdsUpdated = new List<string>();
        foreach(var pool in pools.Values)
        {
            if(!pool.Config.Enabled)
                continue;

            poolIdsUpdated.Add(pool.Config.Id);
            pool.Config.PaymentProcessing.Enabled = true;
        }

        // Join the poolIdsUpdated into CSV string
        var poolIdsCsv = String.Join(",", poolIdsUpdated);
        logger.Info(()=> $"Enabled payment processing for pool {poolIdsCsv}");

        return poolIdsCsv;
    }

    [HttpPost("payment/processing/disable")]
    public ActionResult<string> DisablePoolsPaymentProcessing()
    {
        var poolIdsUpdated = new List<string>();
        foreach(var pool in pools.Values)
        {
            if(!pool.Config.Enabled)
                continue;

            poolIdsUpdated.Add(pool.Config.Id);
            pool.Config.PaymentProcessing.Enabled = false;
        }

        // Join the poolIdsUpdated into CSV string
        var poolIdsCsv = String.Join(",", poolIdsUpdated);
        logger.Info(()=> $"Disabled payment processing for pool {poolIdsCsv}");

        return poolIdsCsv;
    }

    [HttpPost("payment/processing/{poolId}/enable")]
    public ActionResult<string> EnablePoolPaymentProcessing(string poolId)
    {
        if (string.IsNullOrEmpty(poolId))
            throw new ApiException("Missing pool ID", HttpStatusCode.BadRequest);

        pools.TryGetValue(poolId, out var poolInstance);
        if(poolInstance == null)
            return "-1";

        poolInstance.Config.PaymentProcessing.Enabled = true;
        logger.Info(()=> $"Enabled payment processing for pool {poolId}");
        return "Ok";
    }

    [HttpPost("payment/processing/{poolId}/disable")]
    public ActionResult<string> DisablePoolPaymentProcessing(string poolId)
    {
        if (string.IsNullOrEmpty(poolId))
            throw new ApiException("Missing pool ID", HttpStatusCode.BadRequest);

        pools.TryGetValue(poolId, out var poolInstance);
        if(poolInstance == null)
            return "-1";

        poolInstance.Config.PaymentProcessing.Enabled = false;
        logger.Info(()=> $"Disabled payment processing for pool {poolId}");
        return "Ok";
    }

    [HttpGet("stats/gc")]
    public ActionResult<Responses.AdminGcStats> GetGcStats()
    {
        gcStats.GcGen0 = GC.CollectionCount(0);
        gcStats.GcGen1 = GC.CollectionCount(1);
        gcStats.GcGen2 = GC.CollectionCount(2);
        gcStats.MemAllocated = FormatUtil.FormatCapacity(GC.GetTotalMemory(false));

        return gcStats;
    }

    [HttpPost("forcegc")]
    public ActionResult<string> ForceGc()
    {
        GC.Collect(2, GCCollectionMode.Forced);
        return "Ok";
    }

    [HttpGet("pools/{poolId}/miners/{address}/getbalance")]
    public async Task<decimal> GetMinerBalanceAsync(string poolId, string address)
    {
        return await cf.Run(con => balanceRepo.GetBalanceAsync(con, poolId, address));
    }

    [HttpGet("pools/{poolId}/miners/{address}/settings")]
    public async Task<Responses.MinerSettings> GetMinerSettingsAsync(string poolId, string address)
    {
        var pool = GetPool(poolId);

        if(string.IsNullOrEmpty(address))
            throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

        var result = await cf.Run(con=> minerRepo.GetSettingsAsync(con, null, pool.Id, address));

        if(result == null)
            throw new ApiException("No settings found", HttpStatusCode.NotFound);

        return mapper.ToResponse(result);
    }

    [HttpPost("pools/{poolId}/miners/{address}/settings")]
    public async Task<Responses.MinerSettings> SetMinerSettingsAsync(string poolId, string address,
        [FromBody] Responses.MinerSettings settings)
    {
        var pool = GetPool(poolId);

        if(string.IsNullOrEmpty(address))
            throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

        if(settings == null)
            throw new ApiException("Invalid or missing settings", HttpStatusCode.BadRequest);

        // map settings
        var mapped = mapper.ToModel(settings);

        // clamp limit
        if(pool.PaymentProcessing != null)
            mapped.PaymentThreshold = Math.Max(mapped.PaymentThreshold, pool.PaymentProcessing.MinimumPayment);

        mapped.PoolId = pool.Id;
        mapped.Address = address;

        var result = await cf.RunTx(async (con, tx) =>
        {
            await minerRepo.UpdateSettingsAsync(con, tx, mapped);

            return await minerRepo.GetSettingsAsync(con, tx, mapped.PoolId, mapped.Address);
        });

        logger.Info(()=> $"Updated settings for pool {pool.Id}, miner {address}");

        return mapper.ToResponse(result);
    }

    #region Payout batches

    private static Responses.AdminPayoutBatch ToResponse(Persistence.Model.PayoutBatch batch)
    {
        var recipients = Persistence.Model.PayoutBatchRecipients.Deserialize(batch.PoolId, batch.Recipients);

        return new Responses.AdminPayoutBatch
        {
            Id = batch.Id,
            PoolId = batch.PoolId,
            Status = batch.Status.ToString().ToLowerInvariant(),
            Recipients = recipients.Select(x => new Persistence.Model.PayoutBatchRecipient(x.Address, x.Amount)).ToArray(),
            Total = recipients.Sum(x => x.Amount),
            TransactionId = batch.TransactionConfirmationData,
            Error = batch.Error,
            Created = batch.Created,
            Updated = batch.Updated,
        };
    }

    /// <summary>Most recent payout batches for a pool. A "pending" batch older than a minute means payouts are halted.</summary>
    [HttpGet("pools/{poolId}/payoutbatches")]
    public async Task<Responses.AdminPayoutBatch[]> GetPayoutBatchesAsync(string poolId, [FromQuery] int page = 0, [FromQuery] int pageSize = 50)
    {
        var pool = GetPool(poolId);
        pageSize = ClampPageSize(pageSize);
        page = Math.Max(0, page);

        var batches = await cf.Run(con => payoutBatchRepo.PageAsync(con, pool.Id, page, pageSize, HttpContext.RequestAborted));
        return batches.Select(ToResponse).ToArray();
    }

    /// <summary>
    /// Resolves a payout batch whose outcome is unknown (status "pending" after a crash between send and
    /// record). outcome=sent&amp;txid=... marks it sent; the next payout run then inserts the payments and
    /// debits the balances. outcome=notsent marks it failed; the balances are paid on the next run.
    /// Check the wallet before choosing: "sent" on a batch that never left the wallet keeps the coins and
    /// deletes the miners' balances, "notsent" on a batch that was sent pays everyone twice.
    /// </summary>
    [HttpPost("pools/{poolId}/payoutbatches/{id}/resolve")]
    public async Task<Responses.AdminPayoutBatch> ResolvePayoutBatchAsync(string poolId, long id, [FromQuery] string outcome, [FromQuery] string txid = null)
    {
        var pool = GetPool(poolId);
        var ct = HttpContext.RequestAborted;

        var batch = await cf.Run(con => payoutBatchRepo.GetAsync(con, pool.Id, id, ct));
        if(batch == null)
            throw new ApiException("Unknown payout batch", HttpStatusCode.NotFound);

        if(batch.Status != Persistence.Model.PayoutBatchStatus.Pending)
            throw new ApiException($"Payout batch {id} is {batch.Status.ToString().ToLowerInvariant()}, only pending batches can be resolved", HttpStatusCode.BadRequest);

        switch(outcome?.ToLowerInvariant())
        {
            case "sent":
                if(string.IsNullOrWhiteSpace(txid))
                    throw new ApiException("outcome=sent requires txid", HttpStatusCode.BadRequest);

                if(await cf.RunTx((con, tx) => payoutBatchRepo.UpdateStatusAsync(con, tx, id, Persistence.Model.PayoutBatchStatus.Sent, txid.Trim(), "resolved by operator: sent", DateTime.UtcNow, Persistence.Model.PayoutBatchStatus.Pending)) == 0)
                    throw new ApiException($"Payout batch {id} changed state while you were resolving it; reload and check its status", HttpStatusCode.Conflict);

                logger.Warn(() => $"Payout batch {id} of pool {pool.Id} resolved by operator as SENT (tx {txid}); payments are persisted on the next payout run");
                break;

            case "notsent":
                if(await cf.RunTx((con, tx) => payoutBatchRepo.UpdateStatusAsync(con, tx, id, Persistence.Model.PayoutBatchStatus.Failed, null, "resolved by operator: not sent", DateTime.UtcNow, Persistence.Model.PayoutBatchStatus.Pending)) == 0)
                    throw new ApiException($"Payout batch {id} changed state while you were resolving it; reload and check its status", HttpStatusCode.Conflict);

                logger.Warn(() => $"Payout batch {id} of pool {pool.Id} resolved by operator as NOT SENT; its balances are paid on the next payout run");
                break;

            default:
                throw new ApiException("outcome must be 'sent' or 'notsent'", HttpStatusCode.BadRequest);
        }

        return ToResponse(await cf.Run(con => payoutBatchRepo.GetAsync(con, pool.Id, id, ct)));
    }

    #endregion // Payout batches

    #endregion // Actions
}
