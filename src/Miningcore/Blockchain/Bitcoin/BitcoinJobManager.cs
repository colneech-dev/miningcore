using Autofac;
using Miningcore.Mapping;
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
                BitcoinCommands.GetBlockTemplate, ct, extraPoolConfig?.GBTArgs ?? (object) GetBlockTemplateParams());

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

            // Refresh Hathor work and detect changes
            AuxBlockData currentHathorAuxBlock = null;
            if(hathorManager != null)
            {
                await hathorManager.RefreshAsync(ct);
                currentHathorAuxBlock = hathorManager.CurrentAuxBlock;

                if(!isNew && !forceUpdate && job?.HathorAuxBlock?.Hash != null &&
                   !string.Equals(job.HathorAuxBlock.Hash, currentHathorAuxBlock?.Hash, StringComparison.OrdinalIgnoreCase))
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
                    currentRskAuxBlock,
                    currentHathorAuxBlock);

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
        // Read version bits if present (6th param). Some firmware (e.g. BM1370/AxeOS) applies version
        // rolling to the ASIC chip even when the pool denied it, causing a header mismatch if ignored.
        var versionBits = submitParams.Length > 5 ? submitParams[5] as string : null;

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
        if(share.AuxCandidates?.Count > 0 && (auxPowManagers.Count > 0 || rskManager != null || hathorManager != null))
        {
            var submittingJob = job;
            foreach(var (auxBlock, headerBytes, coinbase, candidateBranch) in share.AuxCandidates)
            {
                // Claim the aux block before submitting so two shares that meet the same aux target in
                // quick succession do not both submit it. The claim is released again below whenever the
                // submit does not succeed (RPC error, rejection, exception). Before that release existed a
                // single transient daemon failure made every later share for the same aux block skip as a
                // "duplicate", and the block was lost for good.
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
                        var merkleBranch = candidateBranch ?? submittingJob?.MerkleBranchSteps ?? new List<byte[]>();
                        var headerHex = headerBytes.ToHexString();

                        logger.Info(() => $"RSK submit: hash={auxBlock.Hash[..Math.Min(16, auxBlock.Hash.Length)]}... coinbaseBranchLen={merkleBranch.Count}");
                        var rskAccepted = await rskManager.SubmitBitcoinBlockAsync(
                            auxBlock.Hash, headerHex, coinbaseTxHex, merkleBranch, ct);

                        if(!rskAccepted)
                            submittedAuxHashes.TryRemove(dedupKey, out _);

                        if(rskAccepted)
                        {
                            try
                            {
                                EnsureAuxPersistence();

                                // mnr_getWork returns blockHashForMergedMining, which is NOT the RSK block hash
                                // (keccak256 of the header) stored on-chain. Right after acceptance the node has
                                // usually not imported the block yet, so the real hash and height cannot be
                                // resolved here. Record the merged-mining hash with BlockHeight = null and let
                                // ConfirmRskBlocksAsync resolve both by matching hashForMergedMining on-chain.
                                // Previously this stored eth_blockNumber (the current tip, not our block) as the
                                // height; the confirmer then compared the tip's hash with our merged-mining hash,
                                // found them different by construction, and marked every RSK block orphaned.
                                string rskBlockHash = auxBlock.Hash;
                                ulong? rskBlockHeight = null;

                                var latestRes = await rskManager.Rpc.ExecuteAsync<JToken>(logger, "eth_getBlockByNumber", ct, new object[] { "latest", false });
                                if(latestRes.Error == null && latestRes.Response?.Type == JTokenType.Object)
                                {
                                    var resolved = MatchRskBlock(latestRes.Response, auxBlock.Hash);
                                    if(resolved != null)
                                    {
                                        rskBlockHash = resolved.Value.Hash;
                                        rskBlockHeight = (ulong) resolved.Value.Height;
                                    }
                                    else
                                        logger.Debug(() => $"[rsk] Block {auxBlock.Hash[..Math.Min(16, auxBlock.Hash.Length)]}… not yet imported by the node; the confirmer will resolve its hash and height");
                                }

                                var record = new Persistence.Model.AuxBlock
                                {
                                    PoolId = poolConfig.Id,
                                    ChainId = rskManager.Config.Id,
                                    ChainName = rskManager.Config.Name,
                                    BlockHeight = rskBlockHeight,
                                    AuxBlockHash = rskBlockHash,
                                    ParentBlockHash = share.BlockHash,
                                    Status = Persistence.Model.BlockStatus.Pending,
                                    ConfirmationProgress = 0,
                                    Miner = share.Miner,
                                    Worker = share.Worker,
                                    Source = clusterConfig.ClusterName,
                                    SubmittedVia = "live",
                                    Difficulty = share.HashDifficulty > 0 ? (double?)share.HashDifficulty : null,
                                    NetworkDifficulty = AuxNetworkDifficulty(auxBlock),
                                    Created = clock.Now,
                                };

                                _ = cf.RunTx(async (con, tx) => await auxBlockRepo.InsertAsync(con, tx, record))
                                    .ContinueWith(t => logger.Error(t.Exception, () => $"Failed to persist RSK block {rskBlockHash}"),
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
                        submittedAuxHashes.TryRemove(dedupKey, out _);
                    }

                    continue;
                }

                // Hathor uses its own submit protocol — route by chainId
                if(hathorManager != null && auxBlock.ChainId == hathorManager.Config.ChainId)
                {
                    try
                    {
                        logger.Info(() => $"Submitting merged block to {hathorManager.Config.Name}");

                        var merkleBranch = candidateBranch ?? submittingJob?.MerkleBranchSteps ?? new List<byte[]>();
                        var (hathorAccepted, hathorBlockHash, hathorHeight) = await hathorManager.SubmitBlockAsync(
                            auxBlock.Hash, headerBytes, coinbase, merkleBranch, ct);

                        if(!hathorAccepted)
                            submittedAuxHashes.TryRemove(dedupKey, out _);

                        if(hathorAccepted)
                        {
                            try
                            {
                                EnsureAuxPersistence();

                                var record = new Persistence.Model.AuxBlock
                                {
                                    PoolId = poolConfig.Id,
                                    ChainId = hathorManager.Config.Id,
                                    ChainName = hathorManager.Config.Name,
                                    BlockHeight = hathorHeight > 0 ? (ulong?) hathorHeight : null,
                                    AuxBlockHash = hathorBlockHash,
                                    ParentBlockHash = share.BlockHash,
                                    Status = Persistence.Model.BlockStatus.Pending,
                                    ConfirmationProgress = 0,
                                    // Hathor uses 2 decimals: CoinbaseValue carries HTR cents
                                    Reward = auxBlock.CoinbaseValue > 0 ? (decimal) auxBlock.CoinbaseValue / 100m : null,
                                    Miner = share.Miner,
                                    Worker = share.Worker,
                                    Source = clusterConfig.ClusterName,
                                    SubmittedVia = "live",
                                    Difficulty = share.HashDifficulty > 0 ? (double?) share.HashDifficulty : null,
                                    NetworkDifficulty = AuxNetworkDifficulty(auxBlock),
                                    Created = clock.Now,
                                };

                                _ = cf.RunTx(async (con, tx) => await auxBlockRepo.InsertAsync(con, tx, record))
                                    .ContinueWith(t => logger.Error(t.Exception, () => $"Failed to persist Hathor block {hathorBlockHash}"),
                                        TaskContinuationOptions.OnlyOnFaulted);
                            }
                            catch(Exception ex)
                            {
                                logger.Error(ex, () => $"Failed to persist Hathor block {auxBlock.Hash}");
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        logger.Error(ex, () => $"Error submitting Hathor merged block: {ex.Message}");
                        submittedAuxHashes.TryRemove(dedupKey, out _);
                    }

                    continue;
                }

                // Standard AuxPoW submission
                var manager = auxPowManagers.FirstOrDefault(m => string.Equals(m.CurrentAuxBlock?.Hash, auxBlock.Hash, StringComparison.OrdinalIgnoreCase));
                if(manager == null)
                {
                    logger.Debug(() => $"No manager found for aux block {auxBlock.Hash[..Math.Min(16, auxBlock.Hash.Length)]} — block likely superseded");
                    submittedAuxHashes.TryRemove(dedupKey, out _);
                    continue;
                }

                try
                {
                    logger.Info(() => "Submitting merged block to " + manager.Config.Name);

                    var coinbaseTxHex = coinbase.ToHexString();
                    var merkleBranch = candidateBranch ?? submittingJob?.MerkleBranchSteps ?? new List<byte[]>();
                    var (auxBranch, auxIndex) = AuxPowSerializer.GetAuxMerkleBranch(
                        submittingJob?.AuxMerkleNodes,
                        submittingJob?.AuxMerkleTreeSize ?? 1,
                        submittingJob?.AuxMerkleTreeNonce ?? 0,
                        auxBlock.ChainId);
                    var auxPoWHex = AuxPowSerializer.BuildAuxPoWHex(coinbaseTxHex, headerBytes, merkleBranch, auxBranch, auxIndex);

                    logger.Info(() => $"AuxPoW [{manager.Config.Id}] hash={auxBlock.Hash} treeSize={submittingJob?.AuxMerkleTreeSize} auxIndex={auxIndex} auxBranchLen={auxBranch.Count} coinbaseBranchLen={merkleBranch.Count} auxPoW={auxPoWHex}");
                    var auxAccepted = await manager.SubmitAuxBlockAsync(auxBlock.Hash, auxPoWHex, ct);

                    if(!auxAccepted)
                        submittedAuxHashes.TryRemove(dedupKey, out _);

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
                    submittedAuxHashes.TryRemove(dedupKey, out _);
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

        if(hathorManager != null)
            _ = hathorManager.RefreshAsync(CancellationToken.None);

        return share;
    }

    protected override async Task PostStartInitAsync(CancellationToken ct)
    {
        await base.PostStartInitAsync(ct);

        if(auxPowManagers.Count > 0 || rskManager != null || hathorManager != null)
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

        if(hathorManager != null)
            await ConfirmHathorBlocksAsync(ct);
    }

    private async Task ConfirmHathorBlocksAsync(CancellationToken ct)
    {
        var config = hathorManager.Config;
        if(config.RequiredConfirmations <= 0)
            return;

        try
        {
            var tipHeight = hathorManager.TipHeight;
            if(tipHeight <= 0)
                return;

            var pending = await cf.Run(con => auxBlockRepo.GetPendingAsync(con, poolConfig.Id, config.Id, 50, ct));

            foreach(var block in pending)
            {
                if(string.IsNullOrEmpty(block.AuxBlockHash))
                    continue;

                var (exists, voided, height) = await hathorManager.CheckBlockAsync(block.AuxBlockHash, ct);

                if(!exists)
                {
                    // Give the node a grace period; if the block never appears it was not adopted
                    if(clock.Now - block.Created > TimeSpan.FromHours(2))
                    {
                        block.Status = Persistence.Model.BlockStatus.Orphaned;
                        block.ConfirmationProgress = 0;
                        await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                        logger.Info(() => $"[aux-confirm] [{config.Id}] Block {block.AuxBlockHash[..8]}… never appeared on-chain — orphaned");
                    }
                    continue;
                }

                if(voided)
                {
                    block.Status = Persistence.Model.BlockStatus.Orphaned;
                    block.ConfirmationProgress = 0;
                    await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                    logger.Info(() => $"[aux-confirm] [{config.Id}] Block {block.AuxBlockHash[..8]}… voided — orphaned");
                    continue;
                }

                if(height > 0 && block.BlockHeight == null)
                    block.BlockHeight = (ulong) height;

                var blockHeight = (long) (block.BlockHeight ?? (ulong) height);
                var confirmations = tipHeight - blockHeight;
                var progress = Math.Min(1.0, (double) confirmations / config.RequiredConfirmations);

                if(progress >= 1.0)
                {
                    block.Status = Persistence.Model.BlockStatus.Confirmed;
                    block.ConfirmationProgress = 1;
                    logger.Info(() => $"[aux-confirm] [{config.Id}] Block {block.AuxBlockHash[..8]}… confirmed at height {blockHeight}");
                }
                else
                {
                    block.ConfirmationProgress = progress;
                }

                await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
            }
        }
        catch(Exception ex)
        {
            logger.Warn(() => $"[aux-confirm] [{config.Id}] Check failed: {ex.Message}");
        }
    }

    #region RSK confirmation

    // RSK records are inserted with the merged-mining hash and no height until the node has imported
    // the block (see the RSK submit path). The confirmer resolves them by scanning recent chain blocks
    // for a matching hashForMergedMining. The scan is incremental and cached so that the same blocks
    // are not re-read every minute; only the confirmer touches this state and it runs serialised.
    private const int RskResolveLookbackBlocks = 240;        // ~2 h at RSK's ~30 s block time
    private const int RskResolveMaxLookbackBlocks = 2000;    // cap when a block is older than the window
    private const int RskResolveMaxScanPerTick = 300;
    private static readonly TimeSpan RskUnresolvedGrace = TimeSpan.FromHours(2);
    private static readonly TimeSpan RskUnresolvedHardLimit = TimeSpan.FromHours(24);   // beyond the scan cap: give up
    private readonly Dictionary<string, (string Hash, long Height)> rskBlocksByMergedMiningHash = new(StringComparer.OrdinalIgnoreCase);
    private long rskLastScannedHeight = -1;
    private long rskScanWindowStart = long.MaxValue;

    private static string StripHexPrefix(string s) =>
        s != null && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;

    private static long? ParseHexLong(string s)
    {
        s = StripHexPrefix(s);
        if(string.IsNullOrEmpty(s))
            return null;

        try
        {
            return Convert.ToInt64(s, 16);
        }
        catch(Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// If <paramref name="chainBlock"/> (an eth_getBlockBy* result) was built from
    /// <paramref name="mergedMiningHash"/>, returns its real hash and height.
    /// </summary>
    private static (string Hash, long Height)? MatchRskBlock(JToken chainBlock, string mergedMiningHash)
    {
        if(chainBlock == null || chainBlock.Type != JTokenType.Object)
            return null;

        var hmm = StripHexPrefix(chainBlock["hashForMergedMining"]?.Value<string>());
        if(string.IsNullOrEmpty(hmm) || !string.Equals(hmm, StripHexPrefix(mergedMiningHash), StringComparison.OrdinalIgnoreCase))
            return null;

        var hash = StripHexPrefix(chainBlock["hash"]?.Value<string>());
        var height = ParseHexLong(chainBlock["number"]?.Value<string>());
        if(string.IsNullOrEmpty(hash) || height == null)
            return null;

        return (hash, height.Value);
    }

    /// <summary>
    /// A record holds either the real block hash (resolved) or the merged-mining hash (unresolved or
    /// a legacy row), so a chain block is "ours" if either of its hashes matches.
    /// </summary>
    private static bool RskBlockMatchesRecord(JToken chainBlock, Persistence.Model.AuxBlock record)
    {
        var stored = StripHexPrefix(record.AuxBlockHash);
        var hash = StripHexPrefix(chainBlock["hash"]?.Value<string>());
        var hmm = StripHexPrefix(chainBlock["hashForMergedMining"]?.Value<string>());

        return string.Equals(hash, stored, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(hmm, stored, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ScanRskBlocksAsync(long tipHeight, int lookback, CancellationToken ct)
    {
        var windowStart = Math.Max(0, tipHeight - lookback + 1);
        var from = Math.Max(rskLastScannedHeight + 1, windowStart);

        // the window moved past what was scanned so far (first run, or a long gap): start a new window
        if(from > rskLastScannedHeight + 1 || rskScanWindowStart == long.MaxValue)
            rskScanWindowStart = from;
        else
            rskScanWindowStart = Math.Min(rskScanWindowStart, from);

        var to = Math.Min(tipHeight, from + RskResolveMaxScanPerTick - 1);

        for(var h = from; h <= to; h++)
        {
            var res = await rskManager.Rpc.ExecuteAsync<JToken>(logger, "eth_getBlockByNumber", ct, new object[] { "0x" + h.ToString("x"), false });
            if(res.Error != null || res.Response == null || res.Response.Type != JTokenType.Object)
                break;  // resume from here on the next tick

            var hmm = StripHexPrefix(res.Response["hashForMergedMining"]?.Value<string>());
            var hash = StripHexPrefix(res.Response["hash"]?.Value<string>());
            if(!string.IsNullOrEmpty(hmm) && !string.IsNullOrEmpty(hash))
                rskBlocksByMergedMiningHash[hmm] = (hash, h);

            rskLastScannedHeight = h;
        }

        var minKeep = tipHeight - RskResolveMaxLookbackBlocks;
        foreach(var key in rskBlocksByMergedMiningHash.Where(kv => kv.Value.Height < minKeep).Select(kv => kv.Key).ToArray())
            rskBlocksByMergedMiningHash.Remove(key);
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

            var currentHeight = ParseHexLong(blockNumResult.Response);
            if(currentHeight == null)
            {
                logger.Warn(() => $"[rsk-confirm] RSK node returned unusable block height '{blockNumResult.Response}' — skipping");
                return;
            }

            if(currentHeight == 0)
            {
                logger.Debug(() => "[rsk-confirm] RSK node at block 0 — not yet synced, skipping");
                return;
            }

            var tip = currentHeight.Value;

            // 1. Resolve records that still carry the merged-mining hash and no height
            var unresolved = await cf.Run(con => auxBlockRepo.GetBlocksWithoutHeightAsync(con, poolConfig.Id, rskConfig.Id, 50, ct));

            if(unresolved.Length > 0)
            {
                // look back far enough to cover the oldest unresolved record (RSK ~30 s blocks, plus margin)
                var oldestAge = unresolved.Max(b => clock.Now - b.Created);
                var lookback = Math.Clamp((int) (oldestAge.TotalSeconds / 30) + 40, RskResolveLookbackBlocks, RskResolveMaxLookbackBlocks);

                await ScanRskBlocksAsync(tip, lookback, ct);

                foreach(var block in unresolved)
                {
                    if(string.IsNullOrEmpty(block.AuxBlockHash))
                    {
                        logger.Warn(() => $"[rsk-confirm] Block id={block.Id} has no AuxBlockHash — cannot resolve");
                        continue;
                    }

                    (string Hash, long Height)? found = null;

                    if(rskBlocksByMergedMiningHash.TryGetValue(StripHexPrefix(block.AuxBlockHash), out var hit))
                        found = hit;
                    else
                    {
                        // a row that already holds the real hash (resolved at submit time) and only lacks the height
                        var br = await rskManager.Rpc.ExecuteAsync<JToken>(logger, "eth_getBlockByHash", ct, new object[] { "0x" + StripHexPrefix(block.AuxBlockHash), false });
                        if(br.Error == null && br.Response?.Type == JTokenType.Object)
                        {
                            var height = ParseHexLong(br.Response["number"]?.Value<string>());
                            if(height != null)
                                found = (StripHexPrefix(block.AuxBlockHash), height.Value);
                        }
                    }

                    if(found != null)
                    {
                        var mergedMiningHash = block.AuxBlockHash;
                        block.AuxBlockHash = found.Value.Hash;
                        block.BlockHeight = (ulong) found.Value.Height;
                        await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                        logger.Info(() => $"[rsk-confirm] Resolved {mergedMiningHash[..Math.Min(8, mergedMiningHash.Length)]}… → block {found.Value.Hash[..Math.Min(8, found.Value.Hash.Length)]}… at height {found.Value.Height}");
                        continue;
                    }

                    // Not on-chain. Only give up once the scan has covered the whole window the block
                    // could be in and the block is old enough that the node would have imported it.
                    var ageInBlocks = (long) ((clock.Now - block.Created).TotalSeconds / 30) + 40;
                    var scanCoversBlock = rskLastScannedHeight >= tip - 1 && rskScanWindowStart <= tip - ageInBlocks;

                    var age = clock.Now - block.Created;
                    if((age > RskUnresolvedGrace && scanCoversBlock) || age > RskUnresolvedHardLimit)
                    {
                        block.Status = BlockStatus.Orphaned;
                        block.ConfirmationProgress = 0;
                        await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                        logger.Info(() => $"[rsk-confirm] Block {block.AuxBlockHash[..Math.Min(8, block.AuxBlockHash.Length)]}… never appeared on-chain — orphaned");
                    }
                }
            }

            // 2. Confirm or orphan pending blocks whose height is known
            var pending = await cf.Run(con => auxBlockRepo.GetPendingAsync(con, poolConfig.Id, rskConfig.Id, 200, ct));

            foreach(var block in pending)
            {
                if(block.BlockHeight == null)
                    continue;   // still unresolved; handled above on a later tick

                var blockHeight = (long) block.BlockHeight.Value;
                var confirmations = tip - blockHeight + 1;
                if(confirmations < 0)
                    continue;

                var chainBlock = await rskManager.Rpc.ExecuteAsync<JToken>(logger, "eth_getBlockByNumber", ct, new object[] { "0x" + blockHeight.ToString("x"), false });
                if(chainBlock.Response != null && chainBlock.Response.Type == JTokenType.Object)
                {
                    if(!RskBlockMatchesRecord(chainBlock.Response, block))
                    {
                        block.Status = BlockStatus.Orphaned;
                        block.ConfirmationProgress = 0;
                        await cf.RunTx(async (con, tx) => await auxBlockRepo.UpdateAsync(con, tx, block));
                        logger.Info(() => $"[rsk-confirm] Block {block.AuxBlockHash[..Math.Min(8, block.AuxBlockHash.Length)]}… height {blockHeight} orphaned");
                        continue;
                    }

                    // a legacy row that stored the merged-mining hash with a height: upgrade it to the real hash
                    var realHash = StripHexPrefix(chainBlock.Response["hash"]?.Value<string>());
                    if(!string.IsNullOrEmpty(realHash) && !string.Equals(realHash, StripHexPrefix(block.AuxBlockHash), StringComparison.OrdinalIgnoreCase))
                        block.AuxBlockHash = realHash;
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

            // 3. Deep reorg check on recently confirmed blocks
            var recentConfirmed = await cf.Run(con => auxBlockRepo.GetRecentlyConfirmedAsync(con, poolConfig.Id, rskConfig.Id, TimeSpan.FromHours(24), 50, ct));

            foreach(var block in recentConfirmed)
            {
                if(block.BlockHeight == null)
                    continue;

                var blockHeight = (long) block.BlockHeight.Value;
                var chainBlock = await rskManager.Rpc.ExecuteAsync<JToken>(logger, "eth_getBlockByNumber", ct, new object[] { "0x" + blockHeight.ToString("x"), false });
                if(chainBlock.Response == null || chainBlock.Response.Type != JTokenType.Object)
                    continue;

                if(!RskBlockMatchesRecord(chainBlock.Response, block))
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

    #endregion // RSK confirmation

    public double ShareMultiplier => coin.ShareMultiplier;

    #endregion // API-Surface
}
