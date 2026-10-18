using System.Data;
using System.Data.Common;
using Miningcore.Mapping;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using Newtonsoft.Json;
using NLog;
using Polly;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Payments;

public abstract class PayoutHandlerBase
{
    protected PayoutHandlerBase(
        IConnectionFactory cf,
        MiningcoreMapper mapper,
        IShareRepository shareRepo,
        IBlockRepository blockRepo,
        IBalanceRepository balanceRepo,
        IPaymentRepository paymentRepo,
        IPayoutBatchRepository payoutBatchRepo,
        IMasterClock clock,
        IMessageBus messageBus)
    {
        Contract.RequiresNonNull(cf);
        Contract.RequiresNonNull(mapper);
        Contract.RequiresNonNull(shareRepo);
        Contract.RequiresNonNull(blockRepo);
        Contract.RequiresNonNull(balanceRepo);
        Contract.RequiresNonNull(paymentRepo);
        Contract.RequiresNonNull(payoutBatchRepo);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(messageBus);

        this.cf = cf;
        this.mapper = mapper;
        this.clock = clock;
        this.shareRepo = shareRepo;
        this.blockRepo = blockRepo;
        this.balanceRepo = balanceRepo;
        this.paymentRepo = paymentRepo;
        this.payoutBatchRepo = payoutBatchRepo;
        this.messageBus = messageBus;

        BuildFaultHandlingPolicy();
    }

    protected readonly IBalanceRepository balanceRepo;
    protected readonly IBlockRepository blockRepo;
    protected readonly IConnectionFactory cf;
    protected readonly MiningcoreMapper mapper;
    protected readonly IPaymentRepository paymentRepo;
    protected readonly IPayoutBatchRepository payoutBatchRepo;
    private readonly HashSet<long> notifiedStuckBatches = new();
    protected readonly IShareRepository shareRepo;
    protected readonly IMasterClock clock;
    protected readonly IMessageBus messageBus;
    protected ClusterConfig clusterConfig;
    private IAsyncPolicy faultPolicy;

    protected ILogger logger;
    protected PoolConfig poolConfig;
    private const int RetryCount = 8;

    protected abstract string LogCategory { get; }

    protected void BuildFaultHandlingPolicy()
    {
        var retry = Policy
            .Handle<DbException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), OnRetry);

        faultPolicy = retry;
    }

    protected virtual void OnRetry(Exception ex, TimeSpan timeSpan, int retry, object context)
    {
        logger.Warn(() => $"[{LogCategory}] Retry {retry} in {timeSpan} due to: {ex}");
    }

    public virtual async Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, IMiningPool pool, Block block, CancellationToken ct)
    {
        var blockRewardRemaining = block.Reward;

        // Distribute funds to configured reward recipients
        foreach(var recipient in poolConfig.RewardRecipients.Where(x => x.Percentage > 0))
        {
            var amount = block.Reward * (recipient.Percentage / 100.0m);
            var address = recipient.Address;

            blockRewardRemaining -= amount;

            // skip transfers from pool wallet to pool wallet
            if(address != poolConfig.Address)
            {
                logger.Info(() => $"Crediting {address} with {FormatAmount(amount)}");
                await balanceRepo.AddAmountAsync(con, tx, poolConfig.Id, address, amount, $"Reward for block {block.BlockHeight}");
            }
        }

        return blockRewardRemaining;
    }

    /// <summary>Inserts the payment rows and debits the balances, inside the caller's transaction.</summary>
    private async Task PersistPaymentsCoreAsync(IDbConnection con, IDbTransaction tx, Balance[] balances, string transactionConfirmation)
    {
        var coin = poolConfig.Template.As<CoinTemplate>();

        foreach(var balance in balances)
        {
            if(!string.IsNullOrEmpty(transactionConfirmation) && poolConfig.RewardRecipients.All(x => x.Address != balance.Address))
            {
                // record payment
                var payment = new Payment
                {
                    PoolId = poolConfig.Id,
                    Coin = coin.Symbol,
                    Address = balance.Address,
                    Amount = balance.Amount,
                    Created = clock.Now,
                    TransactionConfirmationData = transactionConfirmation
                };

                await paymentRepo.InsertAsync(con, tx, payment);
            }

            // reset balance
            logger.Info(() => $"[{LogCategory}] Resetting balance of {balance.Address}");
            await balanceRepo.AddAmountAsync(con, tx, poolConfig.Id, balance.Address, -balance.Amount, "Balance reset after payment");
        }
    }

    #region Payout batches (idempotent payouts)

    // RpcClient reports transport failures (timeouts, connection errors, cancellation) as code -500
    // with no daemon response. For a send that means the outcome is unknown: the daemon may well
    // have broadcast the transaction. Daemon-reported errors (any other code) mean nothing was sent.
    protected static bool IsOutcomeUnknown(JsonRpcError error) => error is { Code: -500 };

    /// <summary>
    /// Records the intent to pay <paramref name="balances"/> before anything is sent. Throws if the
    /// record cannot be written: no send happens without a durable record of it.
    /// </summary>
    protected async Task<PayoutBatch> BeginPayoutBatchAsync(Balance[] balances)
    {
        Contract.RequiresNonNull(balances);

        var now = clock.Now;
        var batch = new PayoutBatch
        {
            PoolId = poolConfig.Id,
            Status = PayoutBatchStatus.Pending,
            Recipients = PayoutBatchRecipients.Serialize(balances),
            Created = now,
            Updated = now,
        };

        batch.Id = await faultPolicy.ExecuteAsync(() => cf.RunTx((con, tx) => payoutBatchRepo.InsertAsync(con, tx, batch)));

        logger.Info(() => $"[{LogCategory}] Payout batch {batch.Id}: {balances.Length} recipient(s), {FormatAmount(balances.Sum(x => x.Amount))}");
        return batch;
    }

    /// <summary>The wallet rejected the send; nothing left it and balances stay as they are.</summary>
    protected async Task MarkPayoutBatchFailedAsync(PayoutBatch batch, string error)
    {
        batch.Status = PayoutBatchStatus.Failed;
        batch.Error = error;

        await faultPolicy.ExecuteAsync(() => cf.RunTx((con, tx) =>
            payoutBatchRepo.UpdateStatusAsync(con, tx, batch.Id, PayoutBatchStatus.Failed, null, error, clock.Now)));
    }

    /// <summary>
    /// The wallet accepted the send. First the batch is marked Sent with the transaction id (so a
    /// crash from here on is recoverable), then payments are inserted, balances debited and the
    /// batch completed in one transaction. If that last step fails the batch stays Sent and
    /// <see cref="RecoverPayoutBatchesAsync"/> finishes it before the next payout.
    /// </summary>
    protected async Task CompletePayoutBatchAsync(PayoutBatch batch, string transactionConfirmation)
    {
        Contract.RequiresNonNull(batch);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(transactionConfirmation));

        try
        {
            await faultPolicy.ExecuteAsync(() => cf.RunTx((con, tx) =>
                payoutBatchRepo.UpdateStatusAsync(con, tx, batch.Id, PayoutBatchStatus.Sent, transactionConfirmation, null, clock.Now)));
        }

        catch(Exception ex)
        {
            logger.Fatal(ex, () => $"[{LogCategory}] Payout batch {batch.Id} WAS SENT (tx {transactionConfirmation}) but could not be marked as sent. " +
                $"Payouts for this pool halt until it is resolved: POST api/admin/pools/{poolConfig.Id}/payoutbatches/{batch.Id}/resolve?outcome=sent&txid={transactionConfirmation}");
            throw;
        }

        batch.Status = PayoutBatchStatus.Sent;
        batch.TransactionConfirmationData = transactionConfirmation;

        await FinishSentBatchAsync(batch);
    }

    private async Task FinishSentBatchAsync(PayoutBatch batch)
    {
        var balances = PayoutBatchRecipients.Deserialize(batch.PoolId, batch.Recipients);

        try
        {
            await faultPolicy.ExecuteAsync(() => cf.RunTx(async (con, tx) =>
            {
                await PersistPaymentsCoreAsync(con, tx, balances, batch.TransactionConfirmationData);
                await payoutBatchRepo.UpdateStatusAsync(con, tx, batch.Id, PayoutBatchStatus.Completed, null, null, clock.Now);
            }));

            batch.Status = PayoutBatchStatus.Completed;
        }

        catch(Exception ex)
        {
            logger.Error(ex, () => $"[{LogCategory}] Payout batch {batch.Id} is sent (tx {batch.TransactionConfirmationData}) but its payments could not be persisted; " +
                $"it will be retried before the next payout. Recipients: {batch.Recipients}");
            throw;
        }
    }

    public async Task<bool> RecoverPayoutBatchesAsync(CancellationToken ct)
    {
        var open = await cf.Run(con => payoutBatchRepo.GetByStatusAsync(con, poolConfig.Id,
            new[] { PayoutBatchStatus.Pending, PayoutBatchStatus.Sent }, ct));

        // sent but not persisted: finish now, before any new payout could pay these balances again.
        // A failure here throws, which skips this pool's payout run; that is the point.
        foreach(var batch in open.Where(x => x.Status == PayoutBatchStatus.Sent))
        {
            logger.Warn(() => $"[{LogCategory}] Finishing payout batch {batch.Id} (tx {batch.TransactionConfirmationData}) whose payments were not persisted");
            await FinishSentBatchAsync(batch);
        }

        var stuck = open.Where(x => x.Status == PayoutBatchStatus.Pending).ToArray();
        if(stuck.Length == 0)
            return true;

        foreach(var batch in stuck)
        {
            logger.Error(() => $"[{LogCategory}] Payout batch {batch.Id} from {batch.Created:u} has unknown outcome (the process stopped between send and record). " +
                $"Check the wallet for a transaction paying {batch.Recipients}, then POST api/admin/pools/{poolConfig.Id}/payoutbatches/{batch.Id}/resolve?outcome=sent&txid=<txid> or ?outcome=notsent");

            if(notifiedStuckBatches.Add(batch.Id))
                messageBus.SendMessage(new AdminNotification($"Payouts halted for {poolConfig.Id}",
                    $"Payout batch {batch.Id} ({FormatAmount(PayoutBatchRecipients.Deserialize(batch.PoolId, batch.Recipients).Sum(x => x.Amount))}) has an unknown outcome. " +
                    $"Verify in the wallet and resolve it via the admin API; no payouts run for this pool until then."));
        }

        return false;
    }

    #endregion // Payout batches

    protected async Task PersistPaymentsAsync(Balance[] balances, string transactionConfirmation)
    {
        Contract.RequiresNonNull(balances);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(transactionConfirmation));

        try
        {
            await faultPolicy.ExecuteAsync(() => cf.RunTx((con, tx) => PersistPaymentsCoreAsync(con, tx, balances, transactionConfirmation)));
        }

        catch(Exception ex)
        {
            logger.Error(ex, () => $"[{LogCategory}] Failed to persist the following payments: " +
                $"{JsonConvert.SerializeObject(balances.Where(x => x.Amount > 0).ToDictionary(x => x.Address, x => x.Amount))}");
            throw;
        }
    }

    protected async Task PersistPaymentsAsync(Dictionary<Balance, string> balances)
    {
        Contract.RequiresNonNull(balances);
        Contract.Requires<ArgumentException>(balances.Count > 0);

        var coin = poolConfig.Template.As<CoinTemplate>();

        try
        {
            await faultPolicy.ExecuteAsync(async () =>
            {
                await cf.RunTx(async (con, tx) =>
                {
                    foreach(var kvp in balances)
                    {
                        var (balance, transactionConfirmation) = kvp;

                        if(!string.IsNullOrEmpty(transactionConfirmation) && poolConfig.RewardRecipients.All(x => x.Address != balance.Address))
                        {
                            // record payment
                            var payment = new Payment
                            {
                                PoolId = poolConfig.Id,
                                Coin = coin.Symbol,
                                Address = balance.Address,
                                Amount = balance.Amount,
                                Created = clock.Now,
                                TransactionConfirmationData = transactionConfirmation
                            };

                            await paymentRepo.InsertAsync(con, tx, payment);
                        }

                        // reset balance
                        logger.Info(() => $"[{LogCategory}] Resetting balance of {balance.Address}");
                        await balanceRepo.AddAmountAsync(con, tx, poolConfig.Id, balance.Address, -balance.Amount, "Balance reset after payment");
                    }
                });
            });
        }

        catch(Exception ex)
        {
            logger.Error(ex, () => $"[{LogCategory}] Failed to persist the following payments: " +
                $"{JsonConvert.SerializeObject(balances.Where(x => x.Key.Amount > 0).ToDictionary(x => x.Key.Address, x => x.Key.Amount))}");
            throw;
        }
    }

    public virtual double AdjustShareDifficulty(double difficulty)
    {
        return difficulty;
    }

    public string FormatAmount(decimal amount)
    {
        var coin = poolConfig.Template.As<CoinTemplate>();
        return $"{amount:0.#####} {coin.Symbol}";
    }

    protected virtual void NotifyPayoutSuccess(string poolId, Balance[] balances, string[] txHashes, decimal? txFee)
    {
        var coin = poolConfig.Template.As<CoinTemplate>();

        // admin notifications
        var explorerLinks = !string.IsNullOrEmpty(coin.ExplorerTxLink) ?
            txHashes.Select(x => string.Format(coin.ExplorerTxLink, x)).ToArray() :
            Array.Empty<string>();

        messageBus.SendMessage(new PaymentNotification(poolId, null, balances.Sum(x => x.Amount), coin.Symbol, balances.Length, txHashes, explorerLinks, txFee));
    }

    protected virtual void NotifyPayoutFailure(string poolId, Balance[] balances, string error, Exception ex)
    {
        var coin = poolConfig.Template.As<CoinTemplate>();

        messageBus.SendMessage(new PaymentNotification(poolId, error ?? ex?.Message, balances.Sum(x => x.Amount), coin.Symbol));
    }
}
