using System;
using System.Linq;
using Miningcore.Api;
using Miningcore.Blockchain.Bitcoin.AuxPoW;
using NBitcoin;
using Xunit;

namespace Miningcore.Tests.Api;

/// <summary>
/// /api/auxchains: one entry per chain per pool algorithm, plus Rootstock and Hathor which are not AuxPoW chains.
/// </summary>
public class AuxChainStatsBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);

    // compact bits 0x1c00ffff = 256 x harder than Bitcoin's difficulty 1, 0x1b00ffff = 65536 x
    private static AuxBlockData Block(uint bits, int height = 100, DateTimeOffset? at = null) => new()
    {
        Hash = new string('a', 64),
        Height = height,
        TargetValue = new Target(bits).ToUInt256(),
        FetchedAt = at ?? T0,
    };

    private static AuxChainSource Source(string pool, string algorithm, string id, AuxBlockData block, double poolHashrate = 10) => new()
    {
        PoolId = pool, Algorithm = algorithm, PoolHashrate = poolHashrate,
        Id = id, Name = id.ToUpperInvariant(), ChainId = 1, Block = block,
    };

    [Fact]
    public void SameChainOnTwoAlgorithms_GivesTwoEntriesEachWithItsOwnDifficulty()
    {
        // Myriadcoin: the SHA-256 node is 256x harder than the Scrypt node here
        var stats = AuxChainStatsBuilder.Build(new[]
        {
            Source("sha-pool", "Sha256D", "myriad", Block(0x1b00ffff)),
            Source("scrypt-pool", "Scrypt", "myriad", Block(0x1c00ffff, at: T0.AddSeconds(5))),   // polled later: must not overwrite the other lane
        });

        Assert.Equal(2, stats.Length);
        var sha = stats.Single(x => x.Algorithm == "Sha256D");
        var scrypt = stats.Single(x => x.Algorithm == "Scrypt");
        Assert.Equal(65536, sha.NetworkDifficulty, 3);
        Assert.Equal(256, scrypt.NetworkDifficulty, 3);
        Assert.Equal(new[] { "sha-pool" }, sha.PoolIds);
        Assert.Equal(new[] { "scrypt-pool" }, scrypt.PoolIds);
    }

    [Fact]
    public void SameChainAndAlgorithmAcrossPools_IsOneEntryListingEveryPool()
    {
        var stats = AuxChainStatsBuilder.Build(new[]
        {
            Source("p1", "Sha256D", "namecoin", Block(0x1c00ffff), 5),
            Source("p2", "Sha256D", "namecoin", Block(0x1c00ffff, at: T0.AddSeconds(3)), 7),
        });

        var nmc = Assert.Single(stats);
        Assert.Equal(new[] { "p1", "p2" }, nmc.PoolIds);
        Assert.Equal(12, nmc.PoolHashrate);
    }

    [Fact]
    public void OlderTemplateFromAnotherPool_DoesNotOverwriteTheFresherOne()
    {
        var stats = AuxChainStatsBuilder.Build(new[]
        {
            Source("p1", "Sha256D", "namecoin", Block(0x1b00ffff, at: T0.AddSeconds(10))),
            Source("p2", "Sha256D", "namecoin", Block(0x1c00ffff, at: T0)),
        });

        Assert.Equal(65536, Assert.Single(stats).NetworkDifficulty, 3);
    }

    [Fact]
    public void RootstockAndHathor_GetEstimatedHashrateFromDifficultyAndBlockTime()
    {
        // difficulty 65536 -> 65536 x 2^32 hashes per block; over a 30 s block time
        var rsk = Source("p1", "Sha256D", "rsk", Block(0x1b00ffff, height: 9351956)) with { EstimateHashrateBlockTimeSeconds = 30 };
        var stats = AuxChainStatsBuilder.Build(new[] { rsk });

        var s = Assert.Single(stats);
        Assert.Equal(9351956, s.BlockHeight);
        Assert.Equal(65536.0 * 4294967296.0 / 30, s.NetworkHashrate!.Value, 0);
    }

    [Fact]
    public void ChainWithNoWork_HasNoDifficultyAndNoHashrate_ButStillAppears()
    {
        var rsk = Source("p1", "Sha256D", "rsk", null) with { EstimateHashrateBlockTimeSeconds = 30 };
        var s = Assert.Single(AuxChainStatsBuilder.Build(new[] { rsk }));

        Assert.Equal(0, s.NetworkDifficulty);
        Assert.Null(s.NetworkHashrate);
        Assert.Equal(0, s.BlockHeight);
    }

    [Fact]
    public void DaemonReportedHashrate_IsKeptPerLane_AndTheFreshestReadingWins()
    {
        var stats = AuxChainStatsBuilder.Build(new[]
        {
            Source("sha-pool", "Sha256D", "myriad", Block(0x1b00ffff)) with { NetworkHashrate = 13e12, NetworkHashrateAt = T0 },
            Source("scrypt-pool", "Scrypt", "myriad", Block(0x1c00ffff)) with { NetworkHashrate = 640e12, NetworkHashrateAt = T0.AddSeconds(30) },
            Source("sha-pool-2", "Sha256D", "myriad", Block(0x1b00ffff)) with { NetworkHashrate = 12e12, NetworkHashrateAt = T0.AddSeconds(60) },
        });

        Assert.Equal(12e12, stats.Single(x => x.Algorithm == "Sha256D").NetworkHashrate);
        Assert.Equal(640e12, stats.Single(x => x.Algorithm == "Scrypt").NetworkHashrate);
    }

    [Fact]
    public void FallsBackToCompactBits_WhenTheDaemonGaveNoTarget()
    {
        var block = new AuxBlockData { Hash = new string('a', 64), Bits = "1c00ffff", Height = 5, FetchedAt = T0 };
        var s = Assert.Single(AuxChainStatsBuilder.Build(new[] { Source("p1", "Sha256D", "coin", block) }));

        Assert.Equal(256, s.NetworkDifficulty, 3);
    }

    [Fact]
    public void EntriesAreOrderedByNameThenAlgorithm()
    {
        var stats = AuxChainStatsBuilder.Build(new[]
        {
            Source("a", "Scrypt", "zeta", Block(0x1c00ffff)),
            Source("b", "Sha256D", "alpha", Block(0x1c00ffff)),
            Source("c", "Sha256D", "myriad", Block(0x1c00ffff)),
            Source("d", "Scrypt", "myriad", Block(0x1c00ffff)),
        });

        Assert.Equal(new[] { "alpha|Sha256D", "myriad|Scrypt", "myriad|Sha256D", "zeta|Scrypt" }, stats.Select(x => x.Id + "|" + x.Algorithm));
    }
}
