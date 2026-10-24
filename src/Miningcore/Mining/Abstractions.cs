using Miningcore.Blockchain;
using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Miningcore.Configuration;

namespace Miningcore.Mining;

public interface IMiningPool
{
    PoolConfig Config { get; }
    PoolStats PoolStats { get; }
    BlockchainStats NetworkStats { get; }
    double ShareMultiplier { get; }

    /// <summary>
    /// Factor applied to sum(share difficulty / network difficulty) to get pool effort. 1 where shares are stored in
    /// the same units as block effort (Bitcoin family); the share multiplier elsewhere, as before, because
    /// those families scale block effort in their payout handler's AdjustBlockEffort.
    /// </summary>
    double EffortShareConstant { get; }
    void Configure(PoolConfig pc, ClusterConfig cc);
    double HashrateFromShares(double shares, double interval);
    Task RunAsync(CancellationToken ct);
}

public interface IAuxMiningPool
{
    IReadOnlyList<AuxPowManager> AuxManagers { get; }

    /// <summary>Rootstock merge-mining manager; null when the pool does not merge-mine RSK. It is not an AuxPoW chain.</summary>
    Blockchain.Bitcoin.RSK.RskManager RskManager { get; }

    /// <summary>Hathor merge-mining manager; null when the pool does not merge-mine Hathor. It is not an AuxPoW chain.</summary>
    Blockchain.Bitcoin.Hathor.HathorManager HathorManager { get; }
}
