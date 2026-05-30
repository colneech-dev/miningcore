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
    void Configure(PoolConfig pc, ClusterConfig cc);
    double HashrateFromShares(double shares, double interval);
    Task RunAsync(CancellationToken ct);
}

public interface IAuxMiningPool
{
    IReadOnlyList<AuxPowManager> AuxManagers { get; }
}
