using System.Linq;
using Autofac;
using Microsoft.IO;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Miningcore.Configuration;
using Miningcore.Stratum;
using Miningcore.Tests.Util;
using NBitcoin;
using Newtonsoft.Json;
using NLog;
using Xunit;

namespace Miningcore.Tests.Blockchain.Bitcoin;

/// <summary>
/// Regression tests confirming that merged-mining aux candidates are generated on every
/// valid share — not only when the share is also a primary chain block candidate.
///
/// The invariant under test: BitcoinJob.ProcessShareInternal checks aux chain targets
/// independently of isBlockCandidate. A share that fails the primary chain difficulty
/// but passes an aux chain's difficulty target must still produce an AuxCandidate, so
/// BitcoinJobManager.SubmitShareAsync can submit it to the aux daemon.
/// </summary>
public class AuxCandidateJobTests : TestBase
{
    // Primary target hardcoded to the smallest non-zero value so isBlockCandidate
    // is always false for any real SHA256d hash — guarantees the aux-independent code path.
    private const string ImpossiblePrimaryTarget =
        "0000000000000000000000000000000000000000000000000000000000000001";

    // Dash block template (same coin fixture as BitcoinJobTests) with the target above.
    // curTime=0x63445774 matches nTime param; clock mock is set to the same epoch second.
    private const string BlockTemplateJson =
        "{\"version\":536870912," +
        "\"previousBlockhash\":\"0000011a86a1ad3609e5359b6b6411a1654108ee7c1afc003dec23b5a0400e4b\"," +
        "\"coinbaseValue\":1801475949," +
        "\"target\":\"" + ImpossiblePrimaryTarget + "\"," +
        "\"nonceRange\":\"00000000ffffffff\",\"curTime\":1665423220,\"bits\":\"1e01d771\"," +
        "\"height\":813750,\"transactions\":[],\"coinbaseAux\":{\"flags\":null}," +
        "\"default_witness_commitment\":null,\"capabilities\":[\"proposal\"]," +
        "\"rules\":[\"csv\",\"dip0001\",\"bip147\",\"dip0003\",\"dip0008\",\"realloc\",\"dip0020\",\"dip0024\"]," +
        "\"vbavailable\":{},\"vbrequired\":0," +
        "\"longpollid\":\"0000011a86a1ad3609e5359b6b6411a1654108ee7c1afc003dec23b5a0400e4b814670\"," +
        "\"mintime\":1665422408,\"mutable\":[\"time\",\"transactions\",\"prevblock\"]," +
        "\"sigoplimit\":40000,\"sizelimit\":2000000,\"previousbits\":\"1e01bee4\"," +
        "\"masternode\":[{\"payee\":\"yVXDAM73Tg6A44Bm3qduXsMCYxzuqBCT48\"," +
        "\"script\":\"76a91464f2b2b84f62d68a2cd7f7f5fb2b5aa75ef716d788ac\",\"amount\":1080885569}]," +
        "\"masternode_payments_started\":true,\"masternode_payments_enforced\":true," +
        "\"superblock\":[],\"superblocks_started\":true,\"superblocks_enabled\":true," +
        "\"coinbase_payload\":\"0200b66a0c00fbab6816312c05803d026cce30fec0332c059f66e421ab0bf65b96ea9efb8a22e12cfc31666208b47a006e5b74f95a4c0797b6bc620ea1cc07cb53616e547302\"}";

    // uint256 max — every hash is <= this, so every share is an aux candidate
    private static readonly byte[] MaxTargetBytes = Enumerable.Repeat((byte)0xFF, 32).ToArray();

    // uint256 zero — no hash is <= this, so no share is an aux candidate
    private static readonly byte[] ZeroTargetBytes = new byte[32];

    private static AuxBlockData MakeAux(int chainId, byte[] targetBytes) => new AuxBlockData
    {
        Hash = new string((char)('a' + (chainId % 26)), 64),
        ChainId = chainId,
        TargetValue = new uint256(targetBytes)
    };

    private (BitcoinJob job, StratumConnection worker) CreateJob(AuxBlockData[] auxBlocks = null)
    {
        var job = new BitcoinJob();
        var coin = (BitcoinTemplate)ModuleInitializer.CoinTemplates["dash"];
        var pc = new PoolConfig { Template = coin };

        var blockTemplate = JsonConvert.DeserializeObject<
            Miningcore.Blockchain.Bitcoin.DaemonResponses.BlockTemplate>(
            BlockTemplateJson, jsonSerializerSettings);

        var clock = MockMasterClock.FromTicks(638010200200475015);
        var poolAddressDestination = BitcoinUtils.AddressToDestination(
            "yNkA6gVSPqKzW6WmJtTazRLKbSkQA5ND2h", Network.TestNet);
        var network = Network.GetNetwork("testnet");

        var context = new BitcoinWorkerContext
        {
            Miner = "yXHmbak4AdgK5vWamwqFtEijn2NpgLvmi4",
            ExtraNonce1 = "60000001",
            Difficulty = 1e-20, // any SHA256d hash passes stratum at this difficulty
            UserAgent = "cpuminer-multi/1.3.1"
        };

        var worker = new StratumConnection(
            new NullLogger(LogManager.LogFactory),
            container.Resolve<RecyclableMemoryStreamManager>(),
            clock, "1", false);
        worker.SetContext(context);

        job.Init(blockTemplate, "1", pc, null, new ClusterConfig(), clock,
            poolAddressDestination, network, false,
            coin.ShareMultiplier, coin.CoinbaseHasherValue, coin.HeaderHasherValue, coin.BlockHasherValue,
            auxBlocks);

        return (job, worker);
    }

    [Fact]
    public void AuxCandidates_GeneratedEvenWhenShareIsNotBlockCandidate()
    {
        // Primary target is impossible → isBlockCandidate=false.
        // Aux target is max → aux candidate must still be generated.
        // This is the core regression guard: aux submission must not be gated on
        // the share also being a primary block candidate.
        var (job, worker) = CreateJob(new[] { MakeAux(1, MaxTargetBytes) });

        var (share, blockHex) = job.ProcessShare(worker, "01000000", "63445774", "00000000");

        Assert.False(share.IsBlockCandidate, "impossible primary target should yield no block candidate");
        Assert.Null(blockHex);
        Assert.NotNull(share.AuxCandidates);
        Assert.Single(share.AuxCandidates);
        Assert.Equal(1, share.AuxCandidates[0].AuxBlock.ChainId);
    }

    [Fact]
    public void AuxCandidates_AbsentWhenHashAboveAuxTarget()
    {
        // Zero target is below every hash — no aux candidate should be produced.
        var (job, worker) = CreateJob(new[] { MakeAux(1, ZeroTargetBytes) });

        var (share, _) = job.ProcessShare(worker, "01000000", "63445774", "00000000");

        Assert.True(share.AuxCandidates == null || share.AuxCandidates.Count == 0,
            "zero target must never yield an aux candidate");
    }

    [Fact]
    public void AuxCandidates_NullWhenNoAuxBlocksConfigured()
    {
        // No aux blocks passed to Init → AuxCandidates must be null (not an empty list).
        var (job, worker) = CreateJob();

        var (share, _) = job.ProcessShare(worker, "01000000", "63445774", "00000000");

        Assert.Null(share.AuxCandidates);
    }

    [Fact]
    public void AuxCandidates_PartialMatch_OnlyPassingChainsIncluded()
    {
        // Two aux chains: max target (passes) and zero target (fails).
        // Only the passing chain must appear in AuxCandidates.
        var (job, worker) = CreateJob(new[]
        {
            MakeAux(chainId: 1, MaxTargetBytes),
            MakeAux(chainId: 3, ZeroTargetBytes)
        });

        var (share, _) = job.ProcessShare(worker, "01000000", "63445774", "00000000");

        Assert.NotNull(share.AuxCandidates);
        Assert.Single(share.AuxCandidates);
        Assert.Equal(1, share.AuxCandidates[0].AuxBlock.ChainId);
    }

    [Fact]
    public void AuxCandidates_AllPresent_WhenAllChainsPassTarget()
    {
        // Three chains all with max target — all three must be in AuxCandidates.
        var (job, worker) = CreateJob(new[]
        {
            MakeAux(chainId: 1, MaxTargetBytes),
            MakeAux(chainId: 2, MaxTargetBytes),
            MakeAux(chainId: 3, MaxTargetBytes)
        });

        var (share, _) = job.ProcessShare(worker, "01000000", "63445774", "00000000");

        Assert.NotNull(share.AuxCandidates);
        Assert.Equal(3, share.AuxCandidates.Count);
        var foundChains = share.AuxCandidates.Select(c => c.AuxBlock.ChainId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, foundChains);
    }

    [Fact]
    public void AuxCandidates_HeaderBytesAndCoinbasePopulated()
    {
        // Aux candidates must carry the header bytes and coinbase needed for AuxPoW serialization.
        var (job, worker) = CreateJob(new[] { MakeAux(1, MaxTargetBytes) });

        var (share, _) = job.ProcessShare(worker, "01000000", "63445774", "00000000");

        Assert.NotNull(share.AuxCandidates);
        var (auxBlock, headerBytes, coinbase) = share.AuxCandidates[0];
        Assert.Equal(80, headerBytes.Length);
        Assert.NotEmpty(coinbase);
        Assert.Equal(1, auxBlock.ChainId);
    }
}
