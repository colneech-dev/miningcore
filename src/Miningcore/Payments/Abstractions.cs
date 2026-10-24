using System.Data;
using Miningcore.Configuration;
using Miningcore.Mining;
using Miningcore.Persistence.Model;

namespace Miningcore.Payments;

public interface IPayoutHandler
{
    Task ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct);

    Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct);
    Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, IMiningPool pool, Block block, CancellationToken ct);
    Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct);

    /// <summary>
    /// Finishes payout batches whose coins left the wallet but whose payments were not persisted,
    /// and reports whether paying new balances is safe. False means a batch with an unknown
    /// outcome exists and payouts for this pool must not run until an operator resolves it.
    /// </summary>
    Task<bool> RecoverPayoutBatchesAsync(CancellationToken ct);
    double AdjustShareDifficulty(double difficulty);
    double AdjustBlockEffort(double effort);

    string FormatAmount(decimal amount);
}

public interface IPayoutScheme
{
    Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, IMiningPool pool, IPayoutHandler payoutHandler, Block block, decimal blockReward, CancellationToken ct);
}
