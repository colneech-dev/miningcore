using System;
using System.Collections.Generic;
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
}
