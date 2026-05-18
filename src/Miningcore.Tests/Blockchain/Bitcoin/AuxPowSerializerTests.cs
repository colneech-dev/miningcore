using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Miningcore.Extensions;
using NBitcoin;
using NBitcoin.Crypto;
using Xunit;

namespace Miningcore.Tests.Blockchain.Bitcoin;

public class AuxPowSerializerTests
{
    #region BuildCoinbaseCommitment

    private static byte[] MerkleRoot32(byte fill = 0xaa)
    {
        var b = new byte[32];
        Array.Fill(b, fill);
        return b;
    }

    [Fact]
    public void BuildCoinbaseCommitment_Returns44Bytes()
    {
        var result = AuxPowSerializer.BuildCoinbaseCommitment(MerkleRoot32(), treeSize: 1);
        Assert.Equal(44, result.Length);
    }

    [Fact]
    public void BuildCoinbaseCommitment_StartsWithMergeMiningHeader()
    {
        var result = AuxPowSerializer.BuildCoinbaseCommitment(MerkleRoot32(), treeSize: 1);
        Assert.Equal(AuxPowSerializer.MergeMiningHeader, result[..4]);
    }

    [Fact]
    public void BuildCoinbaseCommitment_EmbedsMerkleRootAtOffset4()
    {
        var root = MerkleRoot32(0x55);
        var result = AuxPowSerializer.BuildCoinbaseCommitment(root, treeSize: 1);
        Assert.Equal(root, result[4..36]);
    }

    [Fact]
    public void BuildCoinbaseCommitment_DaemonCanFindRoot_AfterReversal()
    {
        // The aux daemon (auxpow.cpp::CAuxPow::check) reconstructs the merkle root in
        // internal/LE byte order, then reverses it before searching the coinbase.
        // This means BuildCoinbaseCommitment must receive the root in display/BE order
        // (i.e. the caller must reverse nodes[1] before passing it).
        //
        // Test: build tree → get root in LE (nodes[1]) → reverse to BE → build commitment
        //       → confirm the reversed root (= what the daemon will search for) appears in the commitment.
        var hashHex = "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
        var (nodes, _, _) = AuxPowSerializer.BuildAuxTree(new[] { MakeAux(1, hashHex) });

        var rootLe = nodes[1]; // internal/LE byte order (direct SHA256d output)
        var rootBe = rootLe.Reverse().ToArray(); // display/BE — what the daemon searches for

        var commitment = AuxPowSerializer.BuildCoinbaseCommitment(rootBe, treeSize: 1);

        // Daemon-side: search for rootBe in the script — must be found
        var script = commitment.ToHexString();
        Assert.Contains(rootBe.ToHexString(), script, StringComparison.OrdinalIgnoreCase);

        // Regression guard: embedding rootLe (the wrong order) would NOT be found by the daemon
        Assert.DoesNotContain(rootLe.ToHexString(), script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCoinbaseCommitment_TreeSizeEncodedLE()
    {
        var result = AuxPowSerializer.BuildCoinbaseCommitment(MerkleRoot32(), treeSize: 3);
        var treeSize = BitConverter.ToInt32(result, 36);
        Assert.Equal(3, treeSize);
    }

    [Fact]
    public void BuildCoinbaseCommitment_DefaultNonceIsZero()
    {
        var result = AuxPowSerializer.BuildCoinbaseCommitment(MerkleRoot32(), treeSize: 1);
        var nonce = BitConverter.ToUInt32(result, 40);
        Assert.Equal(0u, nonce);
    }

    [Fact]
    public void BuildCoinbaseCommitment_CustomNonceEncodedLE()
    {
        var result = AuxPowSerializer.BuildCoinbaseCommitment(MerkleRoot32(), treeSize: 1, nonce: 0xDEADBEEF);
        var nonce = BitConverter.ToUInt32(result, 40);
        Assert.Equal(0xDEADBEEFu, nonce);
    }

    #endregion

    #region BuildAuxPoWHex

    [Fact]
    public void BuildAuxPoWHex_StartsWithCoinbaseTxBytes_FIX_A()
    {
        // FIX-A: no VarInt length prefix — coinbase TX bytes must appear at offset 0
        var coinbaseTxHex = "01020304050607080910";
        var header = new byte[80];
        var branch = new List<byte[]>();

        var result = AuxPowSerializer.BuildAuxPoWHex(coinbaseTxHex, header, branch);

        Assert.StartsWith(coinbaseTxHex, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAuxPoWHex_ParentHashIsInternalByteOrder()
    {
        // NBitcoin ToBytes() returns internal (little-endian) byte order — no reversal needed.
        // Commit 5a29ded2: removed Array.Reverse; aux daemon computes SHA256d in the same order.
        var coinbaseTxBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var coinbaseTxHex = coinbaseTxBytes.ToHexString();
        var header = new byte[80];
        header[0] = 0x01;
        var branch = new List<byte[]>();

        var result = AuxPowSerializer.BuildAuxPoWHex(coinbaseTxHex, header, branch);
        var resultBytes = result.HexToByteArray();

        var parentHashOffset = coinbaseTxBytes.Length;
        var parentHashInResult = resultBytes[parentHashOffset..(parentHashOffset + 32)];

        var expectedHash = Hashes.DoubleSHA256(header).ToBytes();

        Assert.Equal(expectedHash, parentHashInResult);
    }

    [Fact]
    public void BuildAuxPoWHex_IncludesMerkleBranchEntries_FIX_C()
    {
        // FIX-C: merkle branch must not be hardcoded as empty
        var coinbaseTxHex = "deadbeef";
        var header = new byte[80];
        var branchHash = new byte[32];
        branchHash[0] = 0xab;
        branchHash[31] = 0xcd;
        var branch = new List<byte[]> { branchHash };

        var result = AuxPowSerializer.BuildAuxPoWHex(coinbaseTxHex, header, branch);

        Assert.Contains(branchHash.ToHexString(), result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAuxPoWHex_EndsWithParentHeader()
    {
        var coinbaseTxHex = "01020304";
        var header = new byte[80];
        for(var i = 0; i < header.Length; i++)
            header[i] = (byte) i;
        var branch = new List<byte[]>();

        var result = AuxPowSerializer.BuildAuxPoWHex(coinbaseTxHex, header, branch);

        Assert.EndsWith(header.ToHexString(), result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAuxPoWHex_EmptyBranch_HasCorrectLength()
    {
        var coinbaseTxBytes = new byte[4];
        var header = new byte[80];
        var branch = new List<byte[]>();

        var result = AuxPowSerializer.BuildAuxPoWHex(
            coinbaseTxBytes.ToHexString(), header, branch);

        // coinbase(4) + parentHash(32) + branchCount(1) + sideMask(4) + auxBranchCount(1) + auxSideMask(4) + header(80)
        var expectedLength = (4 + 32 + 1 + 4 + 1 + 4 + 80) * 2; // hex chars
        Assert.Equal(expectedLength, result.Length);
    }

    #endregion

    #region AuxBlockData record equality (IMP-6)

    [Fact]
    public void AuxBlockData_RecordEquality_SameValues()
    {
        var a = new AuxBlockData { Hash = "abc123", Height = 100, ChainId = 1 };
        var b = new AuxBlockData { Hash = "abc123", Height = 100, ChainId = 1 };
        Assert.Equal(a, b);
    }

    [Fact]
    public void AuxBlockData_RecordInequality_DifferentHash()
    {
        var a = new AuxBlockData { Hash = "abc123", Height = 100 };
        var b = new AuxBlockData { Hash = "def456", Height = 100 };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AuxBlockData_RecordInequality_DifferentHeight()
    {
        var a = new AuxBlockData { Hash = "abc123", Height = 100 };
        var b = new AuxBlockData { Hash = "abc123", Height = 101 };
        Assert.NotEqual(a, b);
    }

    #endregion

    #region AuxChainConfig (IMP-5)

    [Fact]
    public void AuxChainConfig_DefaultPollIntervalIs10()
    {
        var config = new AuxChainConfig();
        Assert.Equal(10, config.PollIntervalSeconds);
    }

    [Fact]
    public void AuxChainConfig_PollIntervalCanBeOverridden()
    {
        var config = new AuxChainConfig { PollIntervalSeconds = 30 };
        Assert.Equal(30, config.PollIntervalSeconds);
    }

    #endregion

    #region BuildAuxTree

    private static AuxBlockData MakeAux(int chainId, string hashHex = null)
    {
        hashHex ??= new string('a', 64);
        return new AuxBlockData { Hash = hashHex, ChainId = chainId };
    }

    // Compute log2 of a power-of-2 integer (used in tests to derive h from treeSize).
    private static int TreeHeight(int treeSize)
    {
        int h = 0;
        while((1 << h) < treeSize) h++;
        return h;
    }

    [Fact]
    public void BuildAuxTree_EmptyList_ReturnsTreeSizeOne()
    {
        var (nodes, treeSize, nonce) = AuxPowSerializer.BuildAuxTree(new List<AuxBlockData>());
        Assert.Equal(1, treeSize);
        Assert.Equal(0u, nonce);
        Assert.Equal(3, nodes.Length); // nodes[0..2]
    }

    [Fact]
    public void BuildAuxTree_SingleChain_TreeSizeOne()
    {
        var (_, treeSize, _) = AuxPowSerializer.BuildAuxTree(new[] { MakeAux(1) });
        Assert.Equal(1, treeSize);
    }

    [Fact]
    public void BuildAuxTree_SingleChain_RootIsLeafHashReversed()
    {
        var hashHex = new string('0', 62) + "ab"; // 32 bytes, last byte = 0xab
        var (nodes, _, _) = AuxPowSerializer.BuildAuxTree(new[] { MakeAux(0, hashHex) });

        // Hash reversed to LE: first byte of reversed = 0xab
        Assert.Equal(0xab, nodes[1][0]);
    }

    [Fact]
    public void BuildAuxTree_TwoChains_NoCollision_TreeSizeTwo()
    {
        // chainId=1 and chainId=2 must fit in a treeSize=2 tree (a valid nonce exists).
        var (_, treeSize, _) = AuxPowSerializer.BuildAuxTree(new[] { MakeAux(1), MakeAux(2) });
        Assert.Equal(2, treeSize);
    }

    [Fact]
    public void BuildAuxTree_TwoChains_LcgCollision_ForcesLargerTree()
    {
        // chainId=0 and chainId=2 always collide at treeSize=2 under the LCG formula
        // because the LCG result mod 2 is identical for chainIds differing by 2.
        // The tree must grow to treeSize=4 where they can be separated.
        var (_, treeSize, _) = AuxPowSerializer.BuildAuxTree(new[] { MakeAux(0), MakeAux(2) });
        Assert.Equal(4, treeSize);
    }

    [Fact]
    public void BuildAuxTree_SpaceXpanseChainId_PlacedAtCorrectSlot()
    {
        // SpaceXpanse chainId=1899. At treeSize=1 (single chain), slot=0.
        var (nodes, treeSize, _) = AuxPowSerializer.BuildAuxTree(new[] { MakeAux(1899) });

        Assert.Equal(1, treeSize);
        Assert.NotNull(nodes[1]); // root/leaf at nodes[1]
    }

    [Fact]
    public void BuildAuxTree_TwoChains_RootIsSHA256dOfChildren()
    {
        // Verify root = SHA256d(slotZeroLeaf || slotOneLeaf) using LCG-derived slot positions.
        var hashA = new string('0', 62) + "cc"; // will be placed at its LCG-derived slot
        var hashB = new string('0', 62) + "dd";

        var auxA = MakeAux(2, hashA);
        var auxB = MakeAux(1, hashB);

        var (nodes, treeSize, nonce) = AuxPowSerializer.BuildAuxTree(new[] { auxA, auxB });

        Assert.Equal(2, treeSize);

        // Determine which chain landed at slot 0 and slot 1 using the LCG.
        int h = TreeHeight(treeSize); // h=1 for treeSize=2
        var slotA = (int) AuxPowSerializer.GetExpectedIndex(nonce, 2, h);
        var slotB = (int) AuxPowSerializer.GetExpectedIndex(nonce, 1, h);
        Assert.NotEqual(slotA, slotB); // slots must be unique

        // Build expected root from the actual slot assignments.
        var leafA = hashA.HexToByteArray().Reverse().ToArray();
        var leafB = hashB.HexToByteArray().Reverse().ToArray();
        byte[] left  = slotA == 0 ? leafA : leafB;
        byte[] right = slotA == 0 ? leafB : leafA;
        var combined = left.Concat(right).ToArray();
        using var sha = System.Security.Cryptography.SHA256.Create();
        var expectedRoot = sha.ComputeHash(sha.ComputeHash(combined));

        Assert.Equal(expectedRoot, nodes[1]);
    }

    [Fact]
    public void BuildAuxTree_NodesArrayLength_Is2TimeTreeSize()
    {
        var (nodes, treeSize, _) = AuxPowSerializer.BuildAuxTree(new[] { MakeAux(1), MakeAux(3) });
        Assert.Equal(2 * treeSize, nodes.Length);
    }

    [Fact]
    public void BuildAuxTree_AllSlotsUnique_UnderLcg()
    {
        // Verify that no two chains occupy the same slot in the built tree.
        var chains = new[] { MakeAux(1), MakeAux(2), MakeAux(3), MakeAux(4) };
        var (_, treeSize, nonce) = AuxPowSerializer.BuildAuxTree(chains);

        int h = TreeHeight(treeSize);
        var slots = chains.Select(c => AuxPowSerializer.GetExpectedIndex(nonce, c.ChainId, h)).ToList();
        Assert.Equal(slots.Count, slots.Distinct().Count());
    }

    [Fact]
    public void BuildAuxTree_LcgNonce_MatchesCoinbaseCommitment()
    {
        // The nonce returned by BuildAuxTree must match what BuildCoinbaseCommitment embeds.
        var chains = new[] { MakeAux(1), MakeAux(2) };
        var (nodes, treeSize, nonce) = AuxPowSerializer.BuildAuxTree(chains);

        var rootBe = nodes[1].Reverse().ToArray();
        var commitment = AuxPowSerializer.BuildCoinbaseCommitment(rootBe, treeSize, nonce);

        // Last 4 bytes of commitment are the nonce (little-endian).
        var embeddedNonce = BitConverter.ToUInt32(commitment, 40);
        Assert.Equal(nonce, embeddedNonce);
    }

    #endregion

    #region GetAuxMerkleBranch

    [Fact]
    public void GetAuxMerkleBranch_NullNodes_ReturnsEmptyBranch()
    {
        var (branch, leafIndex) = AuxPowSerializer.GetAuxMerkleBranch(null, 1, 0u, 1);
        Assert.Empty(branch);
        Assert.Equal(0u, leafIndex);
    }

    [Fact]
    public void GetAuxMerkleBranch_TreeSizeOne_EmptyBranch()
    {
        var (nodes, treeSize, nonce) = AuxPowSerializer.BuildAuxTree(new[] { MakeAux(1899) });
        var (branch, leafIndex) = AuxPowSerializer.GetAuxMerkleBranch(nodes, treeSize, nonce, 1899);

        Assert.Empty(branch); // single-chain tree: no siblings needed
        Assert.Equal(0u, leafIndex);
    }

    [Fact]
    public void GetAuxMerkleBranch_TreeSizeTwo_BranchLengthOne()
    {
        var (nodes, treeSize, nonce) = AuxPowSerializer.BuildAuxTree(new[] { MakeAux(1), MakeAux(2) });
        var (branch, _) = AuxPowSerializer.GetAuxMerkleBranch(nodes, treeSize, nonce, 1);

        Assert.Single(branch); // one sibling at each level for treeSize=2
    }

    [Fact]
    public void GetAuxMerkleBranch_TreeSizeFour_BranchLengthTwo()
    {
        // 4-leaf tree → 2 levels → branch length 2
        var (nodes, treeSize, nonce) = AuxPowSerializer.BuildAuxTree(new[] { MakeAux(0), MakeAux(2) });
        Assert.Equal(4, treeSize);

        var (branch, _) = AuxPowSerializer.GetAuxMerkleBranch(nodes, treeSize, nonce, 0);
        Assert.Equal(2, branch.Count);
    }

    [Fact]
    public void GetAuxMerkleBranch_LeafIndex_IsLcgDerivedSlot()
    {
        // leafIndex must equal GetExpectedIndex(nonce, chainId, h) — the Namecoin LCG formula.
        var (nodes, treeSize, nonce) = AuxPowSerializer.BuildAuxTree(new[] { MakeAux(1), MakeAux(2) });
        var (_, leafIndex) = AuxPowSerializer.GetAuxMerkleBranch(nodes, treeSize, nonce, 1);

        int h = TreeHeight(treeSize);
        Assert.Equal(AuxPowSerializer.GetExpectedIndex(nonce, 1, h), leafIndex);
    }

    [Fact]
    public void GetAuxMerkleBranch_BranchAllowsRootRecovery()
    {
        // Verify that applying the branch hashes to the leaf reproduces the root.
        var hashHex    = new string('0', 62) + "ee";
        var siblingHex = new string('0', 62) + "ff";

        var (nodes, treeSize, nonce) = AuxPowSerializer.BuildAuxTree(new[]
        {
            MakeAux(1, hashHex),
            MakeAux(2, siblingHex)
        });

        var (branch, leafIndex) = AuxPowSerializer.GetAuxMerkleBranch(nodes, treeSize, nonce, 1);

        var leaf = hashHex.HexToByteArray().Reverse().ToArray();
        var current = leaf;
        var idx = (int) leafIndex;

        using var sha = System.Security.Cryptography.SHA256.Create();

        foreach(var sibling in branch)
        {
            byte[] combined;
            if((idx & 1) == 0) // even index: current is left child
                combined = current.Concat(sibling).ToArray();
            else               // odd index: current is right child
                combined = sibling.Concat(current).ToArray();

            current = sha.ComputeHash(sha.ComputeHash(combined));
            idx >>= 1;
        }

        Assert.Equal(nodes[1], current); // reconstructed root must match tree root
    }

    #endregion
}
