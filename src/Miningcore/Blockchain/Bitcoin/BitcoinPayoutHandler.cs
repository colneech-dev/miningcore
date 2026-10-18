using Autofac;
using Miningcore.Mapping;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Rpc;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Block = Miningcore.Persistence.Model.Block;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Bitcoin;

[CoinFamily(CoinFamily.Bitcoin, CoinFamily.Nexa)]
public class BitcoinPayoutHandler : PayoutHandlerBase,
    IPayoutHandler
{
    public BitcoinPayoutHandler(
        IComponentContext ctx,
        IConnectionFactory cf,
        MiningcoreMapper mapper,
        IShareRepository shareRepo,
        IBlockRepository blockRepo,
        IBalanceRepository balanceRepo,
        IPaymentRepository paymentRepo,
        IMasterClock clock,
        IMessageBus messageBus) :
        base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(balanceRepo);
        Contract.RequiresNonNull(paymentRepo);

        this.ctx = ctx;
    }

    protected readonly IComponentContext ctx;
    protected RpcClient rpcClient;
    protected BitcoinPoolConfigExtra extraPoolConfig;
    protected BitcoinDaemonEndpointConfigExtra extraPoolEndpointConfig;
    protected BitcoinPoolPaymentProcessingConfigExtra extraPoolPaymentProcessingConfig;

    private int payoutDecimalPlaces = 4;
    private CoinTemplate coin;
    private int minConfirmations;

    protected override string LogCategory => "Bitcoin Payout Handler";

    #region IPayoutHandler

    public virtual Task ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct)
    {
        Contract.RequiresNonNull(pc);

        poolConfig = pc;
        clusterConfig = cc;

        extraPoolConfig = pc.Extra.SafeExtensionDataAs<BitcoinPoolConfigExtra>();
        extraPoolEndpointConfig = pc.Extra.SafeExtensionDataAs<BitcoinDaemonEndpointConfigExtra>();
        extraPoolPaymentProcessingConfig = pc.PaymentProcessing.Extra.SafeExtensionDataAs<BitcoinPoolPaymentProcessingConfigExtra>();

        coin = poolConfig.Template.As<CoinTemplate>();
        if(coin is BitcoinTemplate bitcoinTemplate)
        {
            minConfirmations = extraPoolEndpointConfig?.MinimumConfirmations ?? bitcoinTemplate.CoinbaseMinConfimations ?? BitcoinConstants.CoinbaseMinConfimations;
            payoutDecimalPlaces = bitcoinTemplate.PayoutDecimalPlaces ?? 4;
        }
        else
            minConfirmations = extraPoolEndpointConfig?.MinimumConfirmations ?? BitcoinConstants.CoinbaseMinConfimations;

        logger = LogUtil.GetPoolScopedLogger(typeof(BitcoinPayoutHandler), pc);

        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();
        rpcClient = new RpcClient(pc.Daemons.First(), jsonSerializerSettings, messageBus, pc.Id);

        return Task.CompletedTask;
    }

    // cleared the first time a daemon rejects gettransaction's include_watchonly argument
    private bool getTransactionAcceptsWatchOnly = true;

    public virtual async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(blocks);

        var pageSize = 100;
        var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
        var result = new List<Block>();

        for(var i = 0; i < pageCount; i++)
        {
            // get a page full of blocks
            var page = blocks
                .Skip(i * pageSize)
                .Take(pageSize)
                .ToArray();

            // build command batch (block.TransactionConfirmationData is the hash of the blocks coinbase transaction)
            // include_watchonly=true so watch-only addresses (e.g. P2PKH form of a P2SH pool address) are included in details
            RpcRequest[] BuildBatch(bool withWatchOnly) => page.Select(block => new RpcRequest(BitcoinCommands.GetTransaction,
                withWatchOnly
                    ? new object[] { block.TransactionConfirmationData, true }
                    : new object[] { block.TransactionConfirmationData })).ToArray();

            // execute batch
            var results = await rpcClient.ExecuteBatchAsync(logger, ct, BuildBatch(getTransactionAcceptsWatchOnly));

            // Legacy daemons (e.g. CorgiCoin, Bitcoin 0.8-era) only take <txid> and answer the 2-argument form with their
            // usage string (code -1). Detect that once, remember it, and retry with the single-argument form.
            if(getTransactionAcceptsWatchOnly &&
               results.Any(r => r.Error is {Code: -1} && r.Error.Message?.StartsWith("gettransaction <txid>", StringComparison.OrdinalIgnoreCase) == true))
            {
                logger.Info(() => $"[{LogCategory}] Daemon does not accept the include_watchonly argument for gettransaction; using the single-argument form");
                getTransactionAcceptsWatchOnly = false;
                results = await rpcClient.ExecuteBatchAsync(logger, ct, BuildBatch(false));
            }

            for(var j = 0; j < results.Length; j++)
            {
                var cmdResult = results[j];

                var transactionInfo = cmdResult.Response?.ToObject<Transaction>();
                var block = page[j];

                // check error
                if(cmdResult.Error != null)
                {
                    // Code -5 interpreted as "orphaned"
                    if(cmdResult.Error.Code == -5)
                    {
                        block.Status = BlockStatus.Orphaned;
                        block.Reward = 0;
                        result.Add(block);

                        logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned due to daemon error {cmdResult.Error.Code}");

                        messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                    }

                    else
                        logger.Warn(() => $"[{LogCategory}] Daemon reports error '{cmdResult.Error.Message}' (Code {cmdResult.Error.Code}) for transaction {page[j].TransactionConfirmationData}");
                }

                // missing transaction details are interpreted as "orphaned"
                else if(transactionInfo?.Details == null || transactionInfo.Details.Length == 0)
                {
                    block.Status = BlockStatus.Orphaned;
                    block.Reward = 0;
                    result.Add(block);

                    logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned due to missing tx details");

                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                }

                // On very low-difficulty chains one job can yield several blocks. They all share the same coinbase txid,
                // but the daemon only holds that transaction in the block that made it onto the chain. A block whose own
                // hash is not the block containing its coinbase is a stale duplicate: never credit it a second time.
                else if(BitcoinUtils.IsCompetingBlock(transactionInfo.BlockHash, block.Hash))
                {
                    block.Status = BlockStatus.Orphaned;
                    block.Reward = 0;
                    result.Add(block);

                    logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} [{block.Hash}] classified as orphaned: its coinbase is in block {transactionInfo.BlockHash}");

                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                }

                else
                {
                    // Top-level Amount is 0 for watch-only coinbase outputs; fall back to summing details
                    var reward = transactionInfo.Amount != 0
                        ? transactionInfo.Amount
                        : transactionInfo.Details.Sum(d => d.Amount);

                    switch(transactionInfo.Details[0].Category)
                    {
                        case "immature":
                            // update progress
                            block.ConfirmationProgress = Math.Min(1.0d, (double) transactionInfo.Confirmations / minConfirmations);
                            block.Reward = reward;
                            result.Add(block);

                            messageBus.NotifyBlockConfirmationProgress(poolConfig.Id, block, coin);
                            break;

                        case "generate":
                            // matured and spendable coinbase transaction
                            block.Status = BlockStatus.Confirmed;
                            block.ConfirmationProgress = 1;
                            block.Reward = reward;
                            result.Add(block);

                            logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");

                            messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                            break;

                        default:
                            logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned. Category: {transactionInfo.Details[0].Category}");

                            block.Status = BlockStatus.Orphaned;
                            block.Reward = 0;
                            result.Add(block);

                            messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                            break;
                    }
                }
            }
        }

        return result.ToArray();
    }

    public virtual async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
    {
        Contract.RequiresNonNull(balances);

        // build args
        var amounts = balances
            .Where(x => x.Amount > 0)
            .ToDictionary(x => x.Address, x => Math.Round(x.Amount, payoutDecimalPlaces));

        if(amounts.Count == 0)
            return;

        logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

        object[] args;

        var identifier = !string.IsNullOrEmpty(clusterConfig.PaymentProcessing?.CoinbaseString) ?
            clusterConfig.PaymentProcessing.CoinbaseString.Trim() : "Miningcore";

        var comment = $"{identifier} Payment";

        if(!(extraPoolConfig?.HasBrokenSendMany == true || poolConfig.Template is BitcoinTemplate { HasBrokenSendMany: true }))
        {
            if(extraPoolPaymentProcessingConfig?.MinersPayTxFees == true)
            {
                var subtractFeesFrom = amounts.Keys.ToArray();

                if(!poolConfig.Template.As<BitcoinTemplate>().HasMasterNodes)
                {
                    args = new object[]
                    {
                        string.Empty, // default account
                        amounts, // addresses and associated amounts
                        1, // only spend funds covered by this many confirmations
                        comment, // tx comment
                        subtractFeesFrom, // distribute transaction fee equally over all recipients
                    };
                }

                else
                {
                    args = new object[]
                    {
                        string.Empty, // default account
                        amounts, // addresses and associated amounts
                        1, // only spend funds covered by this many confirmations
                        false, // Whether to add confirmations to transactions locked via InstantSend
                        comment, // tx comment
                        subtractFeesFrom, // distribute transaction fee equally over all recipients
                        false, // use_is: Send this transaction as InstantSend
                        false, // Use anonymized funds only
                    };
                }
            }

            else
            {
                args = new object[]
                {
                    string.Empty, // default account
                    amounts, // addresses and associated amounts
                };
            }

            var didUnlockWallet = false;

            // send command
            tryTransfer:
            var result = await rpcClient.ExecuteAsync<string>(logger, BitcoinCommands.SendMany, ct, args);

            if(result.Error == null)
            {
                if(didUnlockWallet)
                {
                    // lock wallet
                    logger.Info(() => $"[{LogCategory}] Locking wallet");
                    await rpcClient.ExecuteAsync<JToken>(logger, BitcoinCommands.WalletLock, ct);
                }

                // check result
                var txId = result.Response;

                if(string.IsNullOrEmpty(txId))
                    logger.Error(() => $"[{LogCategory}] {BitcoinCommands.SendMany} did not return a transaction id!");
                else
                    logger.Info(() => $"[{LogCategory}] Payment transaction id: {txId}");

                await PersistPaymentsAsync(balances, txId);

                NotifyPayoutSuccess(poolConfig.Id, balances, new[]
                {
                    txId
                }, null);
            }

            else
            {
                if(result.Error.Code == (int) BitcoinRPCErrorCode.RPC_WALLET_UNLOCK_NEEDED && !didUnlockWallet)
                {
                    if(!string.IsNullOrEmpty(extraPoolPaymentProcessingConfig?.WalletPassword))
                    {
                        logger.Info(() => $"[{LogCategory}] Unlocking wallet");

                        var unlockResult = await rpcClient.ExecuteAsync<JToken>(logger, BitcoinCommands.WalletPassphrase, ct, new[]
                        {
                            extraPoolPaymentProcessingConfig.WalletPassword,
                            (object) 5 // unlock for N seconds
                        });

                        if(unlockResult.Error == null)
                        {
                            didUnlockWallet = true;
                            goto tryTransfer;
                        }

                        else
                            logger.Error(() => $"[{LogCategory}] {BitcoinCommands.WalletPassphrase} returned error: {result.Error.Message} code {result.Error.Code}");
                    }

                    else
                        logger.Error(() => $"[{LogCategory}] Wallet is locked but walletPassword was not configured. Unable to send funds.");
                }

                else
                {
                    logger.Error(() => $"[{LogCategory}] {BitcoinCommands.SendMany} returned error: {result.Error.Message} code {result.Error.Code}");

                    NotifyPayoutFailure(poolConfig.Id, balances, $"{BitcoinCommands.SendMany} returned error: {result.Error.Message} code {result.Error.Code}", null);
                }
            }
        }

        else
        {
            var txFailures = new List<Tuple<KeyValuePair<string, decimal>, Exception>>();
            var resultsLock = new object();   // successBalances and txFailures are written from Parallel.ForEachAsync lambdas
            var successBalances = new Dictionary<Balance, string>();

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 8,
                CancellationToken = ct
            };

            await Parallel.ForEachAsync(amounts, parallelOptions, async (x, _ct) =>
            {
                var (address, amount) = x;

                await Guard(async () =>
                {
                    // use a common id for all log entries related to this transfer
                    var transferId = CorrelationIdGenerator.GetNextId();

                    logger.Info(()=> $"[{LogCategory}] [{transferId}] Sending {FormatAmount(amount)} to {address}");

                    var result = await rpcClient.ExecuteAsync<string>(logger, BitcoinCommands.SendToAddress, ct, new object[]
                    {
                        address,
                        amount,
                    });

                    // check result
                    var txId = result.Response;

                    if(result.Error != null)
                        throw new Exception($"[{transferId}] {BitcoinCommands.SendToAddress} returned error: {result.Error.Message} code {result.Error.Code}");

                    if(string.IsNullOrEmpty(txId))
                        throw new Exception($"[{transferId}] {BitcoinCommands.SendToAddress} did not return a transaction id!");
                    else
                        logger.Info(() => $"[{LogCategory}] [{transferId}] Payment transaction id: {txId}");

                    lock(resultsLock)
                    {
                        successBalances.Add(new Balance
                        {
                            PoolId = poolConfig.Id,
                            Address = address,
                            Amount = amount,
                        }, txId);
                    }
                }, ex =>
                {
                    lock(resultsLock)
                        txFailures.Add(Tuple.Create(x, ex));
                });
            });

            if(successBalances.Any())
            {
                await PersistPaymentsAsync(successBalances);

                NotifyPayoutSuccess(poolConfig.Id, successBalances.Keys.ToArray(), successBalances.Values.ToArray(), null);
            }

            if(txFailures.Any())
            {
                var failureBalances = txFailures.Select(x=> new Balance { Amount = x.Item1.Value }).ToArray();
                var error = string.Join(", ", txFailures.Select(x => $"{x.Item1.Key} {FormatAmount(x.Item1.Value)}: {x.Item2.Message}"));

                logger.Error(()=> $"[{LogCategory}] Failed to transfer the following balances: {error}");

                NotifyPayoutFailure(poolConfig.Id, failureBalances, error, null);
            }
        }
    }

    public double AdjustBlockEffort(double effort)
    {
        return effort;
    }

    #endregion // IPayoutHandler
}
