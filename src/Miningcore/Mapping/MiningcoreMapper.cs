using Miningcore.Configuration;
using Riok.Mapperly.Abstractions;
using Entities = Miningcore.Persistence.Postgres.Entities;
using Model = Miningcore.Persistence.Model;
using Projections = Miningcore.Persistence.Model.Projections;
using Responses = Miningcore.Api.Responses;

namespace Miningcore.Mapping;

/// <summary>
/// All object-to-object mappings in Miningcore, generated at compile time by Mapperly.
///
/// Replaces the AutoMapper profile. Every mapping is a typed method, so a renamed or
/// removed property is a build warning (RMG0xx) instead of a silent null at runtime, and
/// no reflection or expression compilation happens at startup. Overloads are grouped by
/// direction: <c>ToEntity</c> (model to PostgreSQL row), <c>ToModel</c> (row or request to
/// model), <c>ToResponse</c> (model to API response). Mappings that need logic beyond
/// member copying are written by hand at the bottom of the class.
///
/// Notes on the generator configuration:
///  - EnumMappingIgnoreCase: block status is stored lowercase ("pending"); parsing back
///    into <see cref="Model.BlockStatus"/> must accept that.
///  - RequiredMappingStrategy.Target: a target member with no source is a warning. Source
///    members with no target are expected (entities carry ids, stats carry extra columns).
/// </summary>
[Mapper(EnumMappingIgnoreCase = true, RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class MiningcoreMapper
{
    // ------------------------------------------------------------------
    // Stratum share -> persistence
    // ------------------------------------------------------------------

    public partial Model.Share ToModel(Blockchain.Share share);

    [MapProperty(nameof(Blockchain.Share.BlockReward), nameof(Model.Block.Reward))]
    [MapProperty(nameof(Blockchain.Share.BlockHash), nameof(Model.Block.Hash))]
    [MapProperty(nameof(Blockchain.Share.BlockType), nameof(Model.Block.Type))]
    [MapperIgnoreTarget(nameof(Model.Block.Id))]
    [MapperIgnoreTarget(nameof(Model.Block.Status))]
    [MapperIgnoreTarget(nameof(Model.Block.Difficulty))]
    [MapperIgnoreTarget(nameof(Model.Block.ConfirmationProgress))]
    [MapperIgnoreTarget(nameof(Model.Block.Effort))]
    [MapperIgnoreTarget(nameof(Model.Block.MinerEffort))]
    private partial Model.Block ToBlockCore(Blockchain.Share share);

    /// <summary>Block record for a block-candidate share. Status is left for the caller to set.</summary>
    public Model.Block ToBlock(Blockchain.Share share)
    {
        if(share == null)
            return null;

        var block = ToBlockCore(share);
        // Share.Difficulty is the share's own difficulty; the block's difficulty is the hash difficulty,
        // when known. Both are in daemon units (see Blockchain/Bitcoin/DifficultyUnits.cs).
        block.Difficulty = share.HashDifficulty > 0 ? share.HashDifficulty : null;
        return block;
    }

    // ------------------------------------------------------------------
    // Model <-> PostgreSQL entities
    // ------------------------------------------------------------------

    public partial Entities.Share ToEntity(Model.Share share);
    public partial Entities.Block ToEntity(Model.Block block);
    public partial Entities.AuxBlock ToEntity(Model.AuxBlock block);
    public partial Entities.Balance ToEntity(Model.Balance balance);
    public partial Entities.Payment ToEntity(Model.Payment payment);
    public partial Entities.PayoutBatch ToEntity(Model.PayoutBatch batch);
    public partial Entities.MinerSettings ToEntity(Model.MinerSettings settings);
    public partial Entities.PoolStats ToEntity(Model.PoolStats stats);

    [MapperIgnoreTarget(nameof(Entities.MinerWorkerPerformanceStats.Id))]
    [MapperIgnoreTarget(nameof(Entities.MinerWorkerPerformanceStats.Partition))]
    public partial Entities.MinerWorkerPerformanceStats ToEntity(Model.MinerWorkerPerformanceStats stats);

    public partial Model.Share ToModel(Entities.Share share);
    public partial Model.Block ToModel(Entities.Block block);
    public partial Model.AuxBlock ToModel(Entities.AuxBlock block);
    public partial Model.Balance ToModel(Entities.Balance balance);
    public partial Model.Payment ToModel(Entities.Payment payment);
    public partial Model.PayoutBatch ToModel(Entities.PayoutBatch batch);
    public partial Model.BalanceChange ToModel(Entities.BalanceChange change);
    public partial Model.PoolStats ToModel(Entities.PoolStats stats);
    public partial Model.MinerSettings ToModel(Entities.MinerSettings settings);
    public partial Model.MinerWorkerPerformanceStats ToModel(Entities.MinerWorkerPerformanceStats stats);

    // ------------------------------------------------------------------
    // Model -> API responses
    // ------------------------------------------------------------------

    [MapperIgnoreTarget(nameof(Responses.Block.InfoLink))]
    public partial Responses.Block ToResponse(Model.Block block);

    [MapperIgnoreTarget(nameof(Responses.AuxBlock.InfoLink))]
    [MapperIgnoreTarget(nameof(Responses.AuxBlock.RequiredConfirmations))]
    public partial Responses.AuxBlock ToResponse(Model.AuxBlock block);

    public partial Responses.MinerSettings ToResponse(Model.MinerSettings settings);

    [MapperIgnoreTarget(nameof(Responses.Payment.AddressInfoLink))]
    [MapperIgnoreTarget(nameof(Responses.Payment.TransactionInfoLink))]
    public partial Responses.Payment ToResponse(Model.Payment payment);

    public partial Responses.BalanceChange ToResponse(Model.BalanceChange change);

    [MapperIgnoreTarget(nameof(Responses.AggregatedPoolStats.ValidSharesPerSecond))]
    public partial Responses.AggregatedPoolStats ToResponse(Model.PoolStats stats);

    [MapperIgnoreTarget(nameof(Responses.MinerStats.LastPayment))]
    [MapperIgnoreTarget(nameof(Responses.MinerStats.LastPaymentLink))]
    [MapperIgnoreTarget(nameof(Responses.MinerStats.PerformanceSamples))]
    public partial Responses.MinerStats ToResponse(Projections.MinerStats stats);

    public partial Responses.WorkerPerformanceStats ToResponse(Projections.WorkerPerformanceStats stats);
    public partial Responses.WorkerPerformanceStatsContainer ToResponse(Projections.WorkerPerformanceStatsContainer stats);
    public partial Responses.WorkerPerformanceStatsContainer[] ToResponse(Projections.WorkerPerformanceStatsContainer[] stats);
    public partial Responses.MinerPerformanceStats ToResponse(Model.MinerWorkerPerformanceStats stats);
    public partial Responses.MinerPerformanceStats ToResponse(Entities.MinerWorkerPerformanceStats stats);

    // ------------------------------------------------------------------
    // API requests -> model
    // ------------------------------------------------------------------

    [MapperIgnoreTarget(nameof(Model.MinerSettings.PoolId))]
    [MapperIgnoreTarget(nameof(Model.MinerSettings.Address))]
    [MapperIgnoreTarget(nameof(Model.MinerSettings.Created))]
    [MapperIgnoreTarget(nameof(Model.MinerSettings.Updated))]
    public partial Model.MinerSettings ToModel(Responses.MinerSettings settings);

    // ------------------------------------------------------------------
    // Persisted pool stats <-> live stats
    // ------------------------------------------------------------------

    [MapperIgnoreTarget(nameof(Mining.PoolStats.LastPoolBlockTime))]
    public partial Mining.PoolStats ToMiningStats(Model.PoolStats stats);

    [MapperIgnoreTarget(nameof(Blockchain.BlockchainStats.NetworkType))]
    [MapperIgnoreTarget(nameof(Blockchain.BlockchainStats.NextNetworkTarget))]
    [MapperIgnoreTarget(nameof(Blockchain.BlockchainStats.NextNetworkBits))]
    [MapperIgnoreTarget(nameof(Blockchain.BlockchainStats.NodeVersion))]
    [MapperIgnoreTarget(nameof(Blockchain.BlockchainStats.RewardType))]
    [MapperIgnoreTarget(nameof(Blockchain.BlockchainStats.BlockReward))]
    public partial Blockchain.BlockchainStats ToBlockchainStats(Model.PoolStats stats);

    /// <summary>
    /// The stats row StatsRecorder persists each interval: pool-side figures from the live
    /// <see cref="Mining.PoolStats"/>, network-side figures from <see cref="Blockchain.BlockchainStats"/>.
    /// Written by hand because <see cref="Model.PoolStats"/> is init-only and is built from two sources.
    /// </summary>
    public Model.PoolStats ToModel(Mining.PoolStats poolStats, Blockchain.BlockchainStats networkStats, string poolId, DateTime created)
    {
        return new Model.PoolStats
        {
            PoolId = poolId,
            Created = created,

            ConnectedMiners = poolStats?.ConnectedMiners ?? 0,
            ConnectedWorkers = poolStats?.ConnectedWorkers ?? 0,
            PoolHashrate = poolStats?.PoolHashrate ?? 0,
            SharesPerSecond = poolStats?.SharesPerSecond ?? 0,

            NetworkHashrate = networkStats?.NetworkHashrate ?? 0,
            NetworkDifficulty = networkStats?.NetworkDifficulty ?? 0,
            LastNetworkBlockTime = networkStats?.LastNetworkBlockTime,
            BlockHeight = (long) (networkStats?.BlockHeight ?? 0),
            ConnectedPeers = networkStats?.ConnectedPeers ?? 0,
        };
    }

    // ------------------------------------------------------------------
    // Enum <-> string: block status is persisted and served lowercase
    // ------------------------------------------------------------------

    private static string ToStatusString(Model.BlockStatus status) => status.ToString().ToLowerInvariant();
    private static string ToStatusString(Model.PayoutBatchStatus status) => status.ToString().ToLowerInvariant();

    // ------------------------------------------------------------------
    // Pool configuration -> API (hand-written: renames, computed members, and copies
    // that must not alias the live configuration, because the API layer strips secrets
    // from the result afterwards)
    // ------------------------------------------------------------------

    public Responses.PoolInfo ToPoolInfo(PoolConfig config)
    {
        if(config == null)
            return null;

        return new Responses.PoolInfo
        {
            Id = config.Id,
            Coin = ToApiCoinConfig(config.Template),
            Ports = config.Ports?.ToDictionary(x => x.Key, x => Clone(x.Value)),
            PaymentProcessing = ToApiPaymentProcessing(config.PaymentProcessing),
            ClientConnectionTimeout = config.ClientConnectionTimeout,
            JobRebroadcastTimeout = config.JobRebroadcastTimeout,
            BlockRefreshInterval = config.BlockRefreshInterval,
            Address = config.Address,
            // PoolFeePercent, AddressInfoLink and the stats members are filled in by the caller
        };
    }

    public Responses.ApiCoinConfig ToApiCoinConfig(CoinTemplate template)
    {
        if(template == null)
            return null;

        return new Responses.ApiCoinConfig
        {
            Type = template.Symbol,
            Name = template.Name,
            Symbol = template.Symbol,
            Website = template.Website,
            Market = template.Market,
            Family = template.Family.ToString().ToLowerInvariant(),
            Algorithm = SafeAlgorithmName(template),
            Twitter = template.Twitter,
            Discord = template.Discord,
            Telegram = template.Telegram,
            CanonicalName = template.CanonicalName,
        };
    }

    // AutoMapper's MapFrom swallowed a NullReferenceException here and served Algorithm = null; a template without a
    // hasher (malformed) must keep doing that rather than turn /api/pools into a 500
    private static string SafeAlgorithmName(CoinTemplate template)
    {
        try
        {
            return template.GetAlgorithmName();
        }
        catch(NullReferenceException)
        {
            return null;
        }
    }

    public Responses.ApiPoolPaymentProcessingConfig ToApiPaymentProcessing(PoolPaymentProcessingConfig config)
    {
        if(config == null)
            return null;

        return new Responses.ApiPoolPaymentProcessingConfig
        {
            Enabled = config.Enabled,
            MinimumPayment = config.MinimumPayment,
            PayoutScheme = config.PayoutScheme.ToString(),
            // the configured scheme options (factor, block finder percentage); defaults apply for anything absent
            PayoutSchemeConfig = config.PayoutSchemeConfig?.ToObject<Responses.ApiPoolPayoutSchemeConfig>() ?? new Responses.ApiPoolPayoutSchemeConfig(),
            // a copy: MiningPoolExtensions removes wallet secrets from this dictionary before serving it
            Extra = config.Extra != null ? new Dictionary<string, object>(config.Extra) : null,
        };
    }

    private static PoolEndpoint Clone(PoolEndpoint endpoint)
    {
        if(endpoint == null)
            return null;

        return new PoolEndpoint
        {
            ListenAddress = endpoint.ListenAddress,
            Name = endpoint.Name,
            Difficulty = endpoint.Difficulty,
            TcpProxyProtocol = endpoint.TcpProxyProtocol,
            VarDiff = endpoint.VarDiff,
            Tls = endpoint.Tls,
            TlsAuto = endpoint.TlsAuto,
            TlsPfxFile = endpoint.TlsPfxFile,
            TlsPfxPassword = endpoint.TlsPfxPassword,
        };
    }
}
