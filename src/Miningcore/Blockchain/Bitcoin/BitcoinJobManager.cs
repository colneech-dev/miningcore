using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Blockchain.Bitcoin.Custom;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Crypto;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Rpc;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reactive.Linq;
using static Miningcore.Util.ActionUtils;

using Miningcore.Blockchain.Bitcoin.AuxPoW;

namespace Miningcore.Blockchain.Bitcoin;

public class BitcoinJobManager : BitcoinJobManagerBase<BitcoinJob>
{
    public BitcoinJobManager(
        IComponentContext ctx,
        IMasterClock clock,
        IMessageBus messageBus,
        IExtraNonceProvider extraNonceProvider) :
        base(ctx, clock, messageBus, extraNonceProvider)
    {
    }

    private BitcoinTemplate coin;
    private readonly ConcurrentDictionary<string, byte> submittedAuxHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RpcClient> auxRpcClients = new();
    private IConnectionFactory cf;
    private IAuxBlockRepository auxBlockRepo;

    private void EnsureAuxPersistence()
    {
        if(cf != null) return;
        cf = ctx.Resolve<IConnectionFactory>();
        auxBlockRepo = ctx.Resolve<IAuxBlockRepository>();
    }

    private static double? AuxNetworkDifficulty(AuxBlockData auxBlock)
    {
        if(auxBlock?.TargetValue == null) return null;
        var targetBigInt = new BigInteger(auxBlock.TargetValue.ToBytes(), isUnsigned: true);
        if(targetBigInt.IsZero) return null;
        return (double) new Miningcore.Util.BigRational(BitcoinConstants.Diff1, targetBigInt);
    }

    protected override object[] GetBlockTemplateParams()
    {
        var result = base.GetBlockTemplateParams();

        if(coin.HasMWEB)
        {
            result = new object[]
            {
                new
                {
                    rules = new[] {"segwit", "mweb"},
                }
            };
        }

        if(coin.BlockTemplateRpcExtraParams != null)
        {
            if(coin.BlockTemplateRpcExtraParams.Type == JTokenType.Array)
                result = result.Concat(coin.BlockTemplateRpcExtraParams.ToObject<object[]>() ?? Array.Empty<object>()).ToArray();
            else
                result = result.Concat(new []{ coin.BlockTemplateRpcExtraParams.ToObject<object>()}).ToArray();
        }

        return result;
    }

    protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        var syncPendingNotificationShown = false;

        do
        {
            var response = await rpc.ExecuteAsync<BlockTemplate>(logger,
                BitcoinCommands.GetBlockTemplate, ct, GetBlockTemplateParams());

            var isSynched = response.Error == null;

            if(isSynched)
            {
                logger.Info(() => "All daemons synched with blockchain");
                break;
            }
            else
            {
                logger.Debug(() => $"Daemon reports error: {response.Error?.Message}");
            }

            if(!syncPendingNotificationShown)
            {
                logger.Info(() => "Daemon is still syncing with network. Manager will be started once synced.");
                syncPendingNotificationShown = true;
            }

            await ShowDaemonSyncProgressAsync(ct);
        } while(await timer.WaitForNextTickAsync(ct));
    }

    protected async Task<RpcResponse<BlockTemplate>> GetBlockTemplateAsync(CancellationToken ct)
    {
        var result = await rpc.ExecuteAsync<BlockTemplate>(logger,
            BitcoinCommands.GetBlockTemplate, ct, extraPoolConfig?.GBTArgs ?? (object) GetBlockTemplateParams());

        return result;
    }

    protected RpcResponse<BlockTemplate> GetBlockTemplateFromJson(string json)
    {
        var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);

        return new RpcResponse<BlockTemplate>(result!.ResultAs<BlockTemplate>());
    }

    private BitcoinJob CreateJob()
    {
        switch(coin.Symbol)
        {
            case "ADVC":
                return new AdventurecoinJob();
        }

        return new();
    }

    protected override void PostChainIdentifyConfigure()
    {
        base.PostChainIdentifyConfigure();

        if(poolConfig.EnableInternalStratum == true && coin.HeaderHasherValue is IHashAlgorithmInit hashInit)
        {
            if(!hashInit.DigestInit(poolConfig))
                logger.Error(()=> $"{hashInit.GetType().Name} initialization failed");
        }
    }

    protected override async Task<(bool IsNew, bool Force)> UpdateJob(CancellationToken ct, bool forceUpdate, string via = null, string json = null)
    {
        try
        {
            if(forceUpdate)
                lastJobRebroadcast = clock.Now;

            var response = string.IsNullOrEmpty(json) ?
                await GetBlockTemplateAsync(ct) :
                GetBlockTemplateFromJson(json);

            // may happen if daemon is currently not connected to peers
            if(response.Error != null)
            {
                logger.Warn(() => $"Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
                return (false, forceUpdate);
            }

            var blockTemplate = response.Response;
            var job = currentJob;

            var isNew = job == null ||
                (blockTemplate != null &&
                    (job.BlockTemplate?.PreviousBlockhash != blockTemplate.PreviousBlockhash ||
                        blockTemplate.Height > job.BlockTemplate?.Height));

            if(isNew)
                messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

            // Always refresh aux blocks — detect changes even when the primary chain is unchanged
            AuxBlockData[] currentAuxBlocks = null;
            if(auxPowManagers.Count > 0)
            {
                await Task.WhenAll(auxPowManagers.Select(m => m.RefreshAsync(ct)));
                currentAuxBlocks = auxPowManagers
                    .Select(m => m.CurrentAuxBlock)
                    .Where(b => b != null)
                    .ToArray();

                // If any aux block hash changed, rebuild the job so the coinbase commitment is fresh
                if(!isNew && !forceUpdate && job != null)
                {
                    var prevHashes = job.AuxBlocks?.Select(b => b.Hash)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var newHashes = currentAuxBlocks.Select(b => b.Hash)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if(!prevHashes.SetEquals(newHashes))
                        isNew = true;
                }
            }

            // Refresh RSK work and detect changes
            AuxBlockData currentRskAuxBlock = null;
            if(rskManager != null)
            {
                await rskManager.RefreshAsync(ct);
                currentRskAuxBlock = rskManager.CurrentAuxBlock;

                if(!isNew && !forceUpdate && job?.RskAuxBlock?.Hash != null &&
                   !string.Equals(job.RskAuxBlock.Hash, currentRskAuxBlock?.Hash, StringComparison.OrdinalIgnoreCase))
                    isNew = true;
            }

            if(isNew || forceUpdate)
            {
                job = CreateJob();

                job.Init(blockTemplate, NextJobId(),
                    poolConfig, extraPoolConfig, clusterConfig, clock, poolAddressDestination, network, isPoS,
                    ShareMultiplier, coin.CoinbaseHasherValue, coin.HeaderHasherValue,
                    !isPoS ? coin.BlockHasherValue : coin.PoSBlockHasherValue ?? coin.BlockHasherValue,
                    currentAuxBlocks,
                    currentRskAuxBlock);

                if(isNew || forceUpdate)
                    submittedAuxHashes.Clear();

                if(isNew)
                {
                    if(via != null)
                        logger.Info(() => $"Detected new block {blockTemplate.Height} [{via}]");
                    else
                        logger.Info(() => $"Detected new block {blockTemplate.Height}");

                    // update stats
                    BlockchainStats.LastNetworkBlockTime = clock.Now;
                    BlockchainStats.BlockHeight = blockTemplate.Height;
                    BlockchainStats.NetworkDifficulty = job.Difficulty;
                    BlockchainStats.NextNetworkTarget = blockTemplate.Target;
                    BlockchainStats.NextNetworkBits = blockTemplate.Bits;

                    // Some daemons (e.g. Fractal Bitcoin) report inflated getnetworkhashps values.
                    // If targetBlockTime is set in the coin template, recalculate from difficulty instead.
                    if(coin.TargetBlockTime.HasValue && coin.TargetBlockTime.Value > 0)
                        BlockchainStats.NetworkHashrate = BlockchainStats.NetworkDifficulty * Math.Pow(2, 32) / coin.TargetBlockTime.Value;
                }

                else
                {
                    if(via != null)
                        logger.Debug(() => $"Template update {blockTemplate?.Height} [{via}]");
                    else
                        logger.Debug(() => $"Template update {blockTemplate?.Height}");
                }

                // Always keep BlockReward current — coinbase value includes fees which vary per template
                BlockchainStats.BlockReward = (decimal)blockTemplate.CoinbaseValue / 100_000_000m;

                currentJob = job;
            }

            return (isNew, forceUpdate);
        }

        catch(OperationCanceledException)
        {
            // ignored
        }

        catch(Exception ex)
        {
            logger.Error(ex, () => $"Error during {nameof(UpdateJob)}");
        }

        return (false, forceUpdate);
    }

    protected override object GetJobParamsForStratum(bool isNew)
    {
        var job = currentJob;
        return job?.GetJobParams(isNew);
    }

    public override BitcoinJob GetJobForStratum()
    {
        var job = currentJob;
        return job;
    }

    #region API-Surface

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        coin = pc.Template.As<BitcoinTemplate>();
        extraPoolConfig = pc.Extra.SafeExtensionDataAs<BitcoinPoolConfigExtra>();
        extraPoolPaymentProcessingConfig = pc.PaymentProcessing?.Extra?.SafeExtensionDataAs<BitcoinPoolPaymentProcessingConfigExtra>();

        if(extraPoolConfig?.MaxActiveJobs.HasValue == true)
            maxActiveJobs = extraPoolConfig.MaxActiveJobs.Value;

        hasLegacyDaemon = extraPoolConfig?.HasLegacyDaemon == true;

        base.Configure(pc, cc);
    }

    public virtual object[] GetSubscriberData(StratumConnection worker)
    {
        Contract.RequiresNonNull(worker);

        var context = worker.ContextAs<BitcoinWorkerContext>();

        // assign unique ExtraNonce1 to worker (miner)
        context.ExtraNonce1 = extraNonceProvider.Next();

        // setup response data
        var responseData = new object[]
        {
            context.ExtraNonce1,
            BitcoinConstants.ExtranoncePlaceHolderLength - ExtranonceBytes,
        };

        return responseData;
    }

    public virtual async ValueTask<Share> SubmitShareAsync(StratumConnection worker, object submission,
        CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(submission);

        if(submission is not object[] submitParams)
            throw new StratumException(StratumError.Other, "invalid params");

        var context = worker.ContextAs<BitcoinWorkerContext>();

        // extract params
        var workerValue = (submitParams[0] as string)?.Trim();
        var jobId = submitParams[1] as string;
        var extraNonce2 = submitParams[2] as string;
        var nTime = submitParams[3] as string;
        var nonce = submitParams[4] as string;
        var versionBits = context.VersionRollingMask.HasValue ? submitParams[5] as string : null;

        if(string.IsNullOrEmpty(workerValue))
            throw new StratumException(StratumError.Other, "missing or invalid workername");

        BitcoinJob job;

        lock(context)
        {
            job = context.GetJob(jobId);
        }

        if(job == null)
            throw new StratumException(StratumError.JobNotFound, "job not found");

        // validate & process
        var (share, blockHex) = job.ProcessShare(worker, extraNonce2, nTime, nonce, versionBits);

        // enrich share with common data
        share.PoolId = poolConfig.Id;
        share.IpAddress = worker.RemoteEndpoint.Address.ToString();
        share.Miner = context.Miner;
        share.Worker = context.Worker;
        share.UserAgent = context.UserAgent;
        share.Source = clusterConfig.ClusterName;
        share.Created = clock.Now;

        // if block candidate, submit & check if accepted by network
        if(share.IsBlockCandidate)
        {
            logger.Info(() => $"Submitting block {share.BlockHeight} [{share.BlockHash}]");
            logger.Info(() => $"Block {share.BlockHeight} hex[0..160]: {blockHex[..Math.Min(160, blockHex.Length)]}");

            var acceptResponse = await SubmitBlockAsync(share, blockHex, ct);

            // is it still a block candidate?
            share.IsBlockCandidate = acceptResponse.Accepted;

            if(share.IsBlockCandidate)
            {
                logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash}] submitted by {context.Miner}");

                OnBlockFound();

                // persist the coinbase transaction-hash to allow the payment processor
                // to verify later on that the pool has received the reward for the block
                share.TransactionConfirmationData = acceptResponse.CoinbaseTx;
            }

            else
            {
                // clear fields that no longer apply
                share.TransactionConfirmationData = null;
            }
        }

        // Submit to any aux chains (merged mining) if this share met their targets
        if(share.AuxCandidates?.Count > 0 && (auxPowManagers.Count > 0 || rskManager != null))
        {
            var submittingJob = job;
            foreach(var (auxBlock, headerBytes, coinbase) in share.AuxCandidates)
            {
                var dedupKey = $"{auxBlock.ChainId}:{auxBlock.Hash}";
                if(!submittedAuxHashes.TryAdd(dedupKey, 1))
                {
                    logger.Debug(() => $"Skipping duplicate aux submission for {auxBlock.Hash[..Math.Min(16, auxBlock.Hash.Length)]}");
                    continue;
                }

                // RSK uses mnr_submitBitcoinBlock — route by chainId
                if(rskManager != null && auxBlock.ChainId == rskManager.Config.ChainId)
                {
                    try
                    {
                        logger.Info(() => $"Submitting merged block to {rskManager.Config.Name}");

                        var coinbaseTxHex = coinbase.ToHexString();
                        var merkleBranch = submittingJob?.MerkleBranchSteps ?? new List<byte[]>();
                        var headerHex = headerBytes.ToHexString();

                        logger.Info(() => $"RSK submit: hash={auxBlock.Hash[..Math.Min(16, auxBlock.Hash.Length)]}... coinbaseBranchLen={merkleBranch.Count}");
                        var rskAccepted = await rskManager.SubmitBitcoinBlockAsync(
                            auxBlock.Hash, headerHex, coinbaseTxHex, merkleBranch, ct);

                        if(rskAccepted)
                        {
                            try
                            {
                                EnsureAuxPersistence();

                                var record = new Persistence.Model.AuxBlock
                                {
                                    PoolId = poolConfig.Id,
                                    ChainId = rskManager.Config.Id,
                                    ChainName = rskManager.Config.Name,
                                    BlockHeight = auxBlock.Height > 0 ? (ulong?)auxBlock.Height : null,
                                    AuxBlockHash = auxBlock.Hash,
                                    ParentBlockHash = share.BlockHash,
                                    Status = Persistence.Model.BlockStatus.Pending,
                                    ConfirmationProgress = 0,
                                    Reward = auxBlock.CoinbaseValue > 0 ? (decimal)auxBlock.CoinbaseValue / 100_000_000m : null,
                                    Miner = share.Miner,
                                    Worker = share.Worker,
                                    Source = clusterConfig.ClusterName,
                                    SubmittedVia = "live",
                                    Difficulty = share.HashDifficulty > 0 ? (double?)share.HashDifficulty : null,
                                    NetworkDifficulty = AuxNetworkDifficulty(auxBlock),
                                    Created = clock.Now,
                                };

                                _ = cf.RunTx(async (con, tx) => await auxBlockRepo.InsertAsync(con, tx, record))
                                    .ContinueWith(t => logger.Error(t.Exception, () => $"Failed to persist RSK block {auxBlock.Hash}"),
                                        TaskContinuationOptions.OnlyOnFaulted);
                            }
                            catch(Exception ex)
                            {
                                logger.Error(ex, () => $"Failed to persist RSK block {auxBlock.Hash}");
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        logger.Error(ex, () => $"Error submitting RSK merged block: {ex.Message}");
                    }

                    continue;
                }

                // Standard AuxPoW submission
                var manager = auxPowManagers.FirstOrDefault(m => string.Equals(m.CurrentAuxBlock?.Hash, auxBlock.Hash, StringComparison.OrdinalIgnoreCase));
                if(manager == null)
                {
                    logger.Debug(() => $"No manager found for aux block {auxBlock.Hash[..Math.Min(16, auxBlock.Hash.Length)]} — block likely superseded");
                    continue;
                }

                try
                {
                    logger.Info(() => "Submitting merged block to " + manager.Config.Name);

                    var coinbaseTxHex = coinbase.ToHexString();
                    var merkleBranch = submittingJob?.MerkleBranchSteps ?? new List<byte[]>();
                    var (auxBranch, auxIndex) = AuxPowSerializer.GetAuxMerkleBranch(
                        submittingJob?.AuxMerkleNodes,
                        submittingJob?.AuxMerkleTreeSize ?? 1,
                        submittingJob?.AuxMerkleTreeNonce ?? 0,
                        auxBlock.ChainId);
                    var auxPoWHex = AuxPowSerializer.BuildAuxPoWHex(coinbaseTxHex, headerBytes, merkleBranch, auxBranch, auxIndex);

                    logger.Info(() => $"AuxPoW [{manager.Config.Id}] hash={auxBlock.Hash} treeSize={submittingJob?.AuxMerkleTreeSize} auxIndex={auxIndex} auxBranchLen={auxBranch.Count} coinbaseBranchLen={merkleBranch.Count} auxPoW={auxPoWHex}");
                    var auxAccepted = await manager.SubmitAuxBlockAsync(auxBlock.Hash, auxPoWHex, ct);

                    if(auxAccepted)
                    {
                        try
                        {
                            EnsureAuxPersistence();

                            var record = new Persistence.Model.AuxBlock
                            {
                                PoolId = poolConfig.Id,
                                ChainId = manager.Config.Id,
                                ChainName = manager.Config.Name,
                                BlockHeight = auxBlock.Height > 0 ? (ulong?)auxBlock.Height : null,
                                AuxBlockHash = auxBlock.Hash,
                                ParentBlockHash = share.BlockHash,
                                Status = Persistence.Model.BlockStatus.Pending,
                                ConfirmationProgress = 0,
                                Reward = auxBlock.CoinbaseValue > 0 ? (decimal)auxBlock.CoinbaseValue / 100_000_000m : null,
                                Miner = share.Miner,
                                Worker = share.Worker,
                                Source = clusterConfig.ClusterName,
                                SubmittedVia = "live",
                                Difficulty = share.HashDifficulty > 0 ? (double?)share.HashDifficulty : null,
                                NetworkDifficulty = AuxNetworkDifficulty(auxBlock),
                                Created = clock.Now,
                            };

                            _ = cf.RunTx(async (con, tx) => await auxBlockRepo.InsertAsync(con, tx, record))
                                .ContinueWith(t => logger.Error(t.Exception, () => $"Failed to persist aux block {auxBlock.Hash} for {manager.Config.Name}"),
                                    TaskContinuationOptions.OnlyOnFaulted);
                        }
                        catch(Exception ex)
                        {
                            logger.Error(ex, () => $"Failed to persist aux block {auxBlock.Hash} for {manager.Config.Name}");
                        }
                    }
                }
                catch(Exception ex)
                {
                    // Don't let aux submission failure affect the accepted primary share
                    logger.Error(ex, () => $"Error submitting merged block to {manager.Config.Name}");
                }
            }
        }

        // Refresh aux blocks on every share (rate-limited internally)
        // Use CancellationToken.None so a miner disconnect doesn't abort the refresh
        if(auxPowManagers.Count > 0)
        {
            foreach(var manager in auxPowManagers)
                _ = manager.RefreshAsync(CancellationToken.None);
        }

        if(rskManager != null)
            _ = rskManager.RefreshAsync(CancellationToken.None);

        return share;
    }

    protected override async Task PostStartInitAsync(CancellationToken ct)
    {
        await base.PostStartInitAsync(ct);

        if(auxPowManagers.Count > 0)
        {
            EnsureAuxPersistence();

            Observable.Interval(TimeSpan.FromMinutes(1))
                .Select(_ => Observable.FromAsync(() =>
                    Guard(() => ConfirmAuxBlocksAsync(ct), ex => logger.Error(ex))))
                .Concat()
                .Subscribe();
        }
    }

    private RpcClient GetAuxRpcClient(AuxChainConfig config)
    {
        if(!auxRpcClients.TryGetValue(config.Id, out var client))
        {
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();
            client = new RpcClient(config.Daemons.First(), jsonSerializerSettings, messageBus, config.Id);
            auxRpcClients[config.Id] = client;
        }
        return client;
    }

    private async Task ConfirmAuxBlocksAsync(CancellationToken ct)
    {
        foreach(var manager in auxPowManagers)
        {
            var config = manager.Config;
            if(config.RequiredConfirmations <= 0 || config.Daemons == null || config.Daemons.Length == 0)
                continue;

            try
            {
                var auxRpc = GetAuxRpcClient(config);

                var chainInfoResult = await auxRpc.ExecuteAsync<BlockchainInfo>(logger, BitcoinCommands.GetBlockchainInfo, ct);
                long currentHeight;
                if(chainInfoResult.Error != null || chainInfoResult.Response == null)
                {
                    // Fallback for daemons that don't support getblockchaininfo (e.g. Elastos)
                    var countResult = await auxRpc.ExecuteAsync<JToken>(logger, BitcoinCommands.GetBlockCount, ct);
                    if(countResult.Error != null || countResult.Response == null)
                    {
                        logger.Warn(() => $"[aux-confirm] [{config.Id}] Could not get chain height: {countResult.Error?.Message}");
                        continue;
                    }
                    currentHeight = countResult.Response.Value<long>();
                }
                else
                {
                    currentHeight = chainInfoResult.Response.Blocks;
                }
                var pending = await cf.Run(con => auxBlockRepo.GetPendingAsync(con, poolConfig.Id, config.Id, 200, ct));

                foreach(var block in pending)
                {
                    if(block.BlockHeight == null)
                        continue;

                    var blockHeight = (long)block.BlockHeight.Value;
                    var confirmations = currentHeight - blockHeight + 1;

                    if(confirmations < 0)
                        continue;

                    // Orphan check: hash at this height should match our stored hash
                    var hashResult = await auxRpc.ExecuteAsync<string>(logger, "getblockhash", ct, new object[] { blockHeight });
                    if(hashResult.Response != null &&
                       !string.Equals(hashResult.Response, block.AuxBlockHash, StringComparison.OrdinalIgnoreCase))
                    {
                        block.Status = BlockStatus.Orphaned;
                        block.ConfirmationProgress = 0;
                        await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                        logger.Info(() => $"[aux-confirm] [{config.Id}] Block {block.AuxBlockHash[..Math.Min(8, block.AuxBlockHash.Length)]}… height {blockHeight} orphaned");
                        continue;
                    }

                    var progress = Math.Min(1.0, (double) confirmations / config.RequiredConfirmations);

                    if(progress >= 1.0)
                    {
                        block.Status = BlockStatus.Confirmed;
                        block.ConfirmationProgress = 1;
                        logger.Info(() => $"[aux-confirm] [{config.Id}] Block {block.AuxBlockHash[..Math.Min(8, block.AuxBlockHash.Length)]}… confirmed at height {blockHeight}");
                    }
                    else
                    {
                        block.ConfirmationProgress = progress;
                    }

                    await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                }

                // Heal backfill blocks that are missing blockHeight
                var noHeight = await cf.Run(con => auxBlockRepo.GetBlocksWithoutHeightAsync(con, poolConfig.Id, config.Id, 50, ct));
                foreach(var block in noHeight)
                {
                    if(string.IsNullOrEmpty(block.AuxBlockHash)) continue;
                    var blockResult = await auxRpc.ExecuteAsync<DaemonResponses.Block>(logger, BitcoinCommands.GetBlock, ct, new object[] { block.AuxBlockHash, 1 });
                    if(blockResult.Error != null || blockResult.Response == null) continue;
                    block.BlockHeight = blockResult.Response.Height;
                    await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                    logger.Info(() => $"[aux-confirm] [{config.Id}] Healed missing height for {block.AuxBlockHash[..Math.Min(8, block.AuxBlockHash.Length)]}… → {block.BlockHeight}");
                }

                // Deep reorg check: re-verify recently confirmed blocks in case of chain reorg
                var recentConfirmed = await cf.Run(con => auxBlockRepo.GetRecentlyConfirmedAsync(con, poolConfig.Id, config.Id, TimeSpan.FromHours(24), 50, ct));
                foreach(var block in recentConfirmed)
                {
                    var blockHeight = (long)block.BlockHeight!.Value;
                    var hashResult = await auxRpc.ExecuteAsync<string>(logger, "getblockhash", ct, new object[] { blockHeight });
                    if(hashResult.Response == null) continue;
                    if(!string.Equals(hashResult.Response, block.AuxBlockHash, StringComparison.OrdinalIgnoreCase))
                    {
                        block.Status = BlockStatus.Orphaned;
                        block.ConfirmationProgress = 0;
                        await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                        logger.Warn(() => $"[aux-confirm] [{config.Id}] Deep reorg: block {block.AuxBlockHash[..Math.Min(8, block.AuxBlockHash.Length)]}… at height {blockHeight} is now orphaned");
                    }
                }
            }
            catch(Exception ex)
            {
                logger.Warn(() => $"[aux-confirm] [{config.Id}] Check failed: {ex.Message}");
            }
        }

        if(rskManager != null)
            await ConfirmRskBlocksAsync(ct);
    }

    private async Task ConfirmRskBlocksAsync(CancellationToken ct)
    {
        var rskConfig = rskManager.Config;
        if(rskConfig.RequiredConfirmations <= 0)
            return;

        try
        {
            var blockNumResult = await rskManager.Rpc.ExecuteAsync<string>(logger, "eth_blockNumber", ct);
            if(blockNumResult.Error != null || blockNumResult.Response == null)
            {
                logger.Warn(() => $"[rsk-confirm] Could not get RSK block height: {blockNumResult.Error?.Message}");
                return;
            }
            var currentHeight = Convert.ToInt64(blockNumResult.Response.TrimStart('0', 'x').TrimStart('0', 'X'), 16);

            // Heal blocks missing height
            var noHeight = await cf.Run(con => auxBlockRepo.GetBlocksWithoutHeightAsync(con, poolConfig.Id, rskConfig.Id, 50, ct));
            foreach(var block in noHeight)
            {
                if(string.IsNullOrEmpty(block.AuxBlockHash))
                {
                    logger.Warn(() => $"[rsk-confirm] Block id={block.Id} has no AuxBlockHash — skipping height heal");
                    continue;
                }
                var hexHash = block.AuxBlockHash.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? block.AuxBlockHash : "0x" + block.AuxBlockHash;
                var br = await rskManager.Rpc.ExecuteAsync<JToken>(logger, "eth_getBlockByHash", ct, new object[] { hexHash, false });
                if(br.Error != null || br.Response == null || br.Response.Type == JTokenType.Null) continue;
                var numHex = br.Response["number"]?.Value<string>();
                if(string.IsNullOrEmpty(numHex)) continue;
                block.BlockHeight = (ulong)Convert.ToInt64(numHex.TrimStart('0', 'x').TrimStart('0', 'X'), 16);
                await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                logger.Info(() => $"[rsk-confirm] Healed height for {block.AuxBlockHash[..Math.Min(8, block.AuxBlockHash.Length)]}… → {block.BlockHeight}");
            }

            // Confirm/orphan pending blocks
            var pending = await cf.Run(con => auxBlockRepo.GetPendingAsync(con, poolConfig.Id, rskConfig.Id, 200, ct));
            foreach(var block in pending)
            {
                if(block.BlockHeight == null)
                {
                    logger.Warn(() => $"[rsk-confirm] Block id={block.Id} ({block.AuxBlockHash?[..Math.Min(8, block.AuxBlockHash?.Length ?? 0)]}) has no BlockHeight — skipping confirmation");
                    continue;
                }
                var blockHeight = (long)block.BlockHeight.Value;
                var confirmations = currentHeight - blockHeight + 1;
                if(confirmations < 0) continue;

                var heightHex = "0x" + blockHeight.ToString("x");
                var chainBlock = await rskManager.Rpc.ExecuteAsync<JToken>(logger, "eth_getBlockByNumber", ct, new object[] { heightHex, false });
                if(chainBlock.Response != null && chainBlock.Response.Type != JTokenType.Null)
                {
                    var chainHash = chainBlock.Response["hash"]?.Value<string>() ?? "";
                    var storedHash = block.AuxBlockHash.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? block.AuxBlockHash : "0x" + block.AuxBlockHash;
                    if(!string.IsNullOrEmpty(chainHash) && !string.Equals(chainHash, storedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        block.Status = BlockStatus.Orphaned;
                        block.ConfirmationProgress = 0;
                        await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                        logger.Info(() => $"[rsk-confirm] Block {block.AuxBlockHash[..Math.Min(8, block.AuxBlockHash.Length)]}… height {blockHeight} orphaned");
                        continue;
                    }
                }

                var progress = Math.Min(1.0, (double) confirmations / rskConfig.RequiredConfirmations);
                if(progress >= 1.0)
                {
                    block.Status = BlockStatus.Confirmed;
                    block.ConfirmationProgress = 1;
                    logger.Info(() => $"[rsk-confirm] Block {block.AuxBlockHash[..Math.Min(8, block.AuxBlockHash.Length)]}… confirmed at height {blockHeight}");
                }
                else
                    block.ConfirmationProgress = progress;

                await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
            }

            // Deep reorg check on recently confirmed blocks
            var recentConfirmed = await cf.Run(con => auxBlockRepo.GetRecentlyConfirmedAsync(con, poolConfig.Id, rskConfig.Id, TimeSpan.FromHours(24), 50, ct));
            foreach(var block in recentConfirmed)
            {
                if(block.BlockHeight == null) continue;
                var blockHeight = (long)block.BlockHeight.Value;
                var heightHex = "0x" + blockHeight.ToString("x");
                var chainBlock = await rskManager.Rpc.ExecuteAsync<JToken>(logger, "eth_getBlockByNumber", ct, new object[] { heightHex, false });
                if(chainBlock.Response == null || chainBlock.Response.Type == JTokenType.Null) continue;
                var chainHash = chainBlock.Response["hash"]?.Value<string>() ?? "";
                var storedHash = block.AuxBlockHash.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? block.AuxBlockHash : "0x" + block.AuxBlockHash;
                if(!string.IsNullOrEmpty(chainHash) && !string.Equals(chainHash, storedHash, StringComparison.OrdinalIgnoreCase))
                {
                    block.Status = BlockStatus.Orphaned;
                    block.ConfirmationProgress = 0;
                    await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                    logger.Warn(() => $"[rsk-confirm] Deep reorg: block {block.AuxBlockHash[..Math.Min(8, block.AuxBlockHash.Length)]}… at height {blockHeight} orphaned");
                }
            }
        }
        catch(Exception ex)
        {
            logger.Warn(() => $"[rsk-confirm] Check failed: {ex.Message}");
        }
    }

    public double ShareMultiplier => coin.ShareMultiplier;

    #endregion // API-Surface
}
