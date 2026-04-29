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

    [Fact]
    public void BuildCoinbaseCommitment_ReturnsNull_ForNull()
    {
        Assert.Null(AuxPowSerializer.BuildCoinbaseCommitment(null));
    }

    [Fact]
    public void BuildCoinbaseCommitment_ReturnsNull_ForEmpty()
    {
        Assert.Null(AuxPowSerializer.BuildCoinbaseCommitment(string.Empty));
    }

    [Fact]
    public void BuildCoinbaseCommitment_ReturnsNull_ForShortHash()
    {
        // 62 hex chars = 31 bytes — too short
        Assert.Null(AuxPowSerializer.BuildCoinbaseCommitment(new string('a', 62)));
    }

    [Fact]
    public void BuildCoinbaseCommitment_ReturnsNull_ForLongHash()
    {
        // 66 hex chars = 33 bytes — too long
        Assert.Null(AuxPowSerializer.BuildCoinbaseCommitment(new string('a', 66)));
    }

    [Fact]
    public void BuildCoinbaseCommitment_StartsWithMergeMiningHeader()
    {
        var hash = new string('a', 64);
        var result = AuxPowSerializer.BuildCoinbaseCommitment(hash);

        Assert.NotNull(result);
        Assert.Equal(AuxPowSerializer.MergeMiningHeader, result[..4]);
    }

    [Fact]
    public void BuildCoinbaseCommitment_HasCorrectTotalLength()
    {
        var hash = new string('a', 64);
        var result = AuxPowSerializer.BuildCoinbaseCommitment(hash);

        // 4 (header) + 32 (hash) + 4 (numChains) + 4 (nonce) = 44
        Assert.NotNull(result);
        Assert.Equal(44, result.Length);
    }

    [Fact]
    public void BuildCoinbaseCommitment_EmbedHashWithoutReversal_FIX_D()
    {
        // The hash returned by getauxblock is already in the correct byte order for embedding.
        // We must NOT reverse it.
        var hashHex = "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
        var expectedHashBytes = hashHex.HexToByteArray();

        var result = AuxPowSerializer.BuildCoinbaseCommitment(hashHex);

        Assert.NotNull(result);
        // Hash bytes start immediately after the 4-byte merge mining header
        var embeddedHash = result[4..36];
        Assert.Equal(expectedHashBytes, embeddedHash);
    }

    [Fact]
    public void BuildCoinbaseCommitment_EmbedHashWithoutReversal_0xPrefix()
    {
        var hashHex = "0x0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";
        var expectedHashBytes = hashHex[2..].HexToByteArray();

        var result = AuxPowSerializer.BuildCoinbaseCommitment(hashHex);

        Assert.NotNull(result);
        Assert.Equal(expectedHashBytes, result[4..36]);
    }

    [Fact]
    public void BuildCoinbaseCommitment_NumChainsEncodedLE()
    {
        var hash = new string('0', 64);
        var result = AuxPowSerializer.BuildCoinbaseCommitment(hash, numChains: 3);

        Assert.NotNull(result);
        // numChains at offset 36, little-endian uint32
        var numChains = BitConverter.ToUInt32(result, 36);
        Assert.Equal(3u, numChains);
    }

    [Fact]
    public void BuildCoinbaseCommitment_NonceEncodedLE()
    {
        var hash = new string('0', 64);
        var result = AuxPowSerializer.BuildCoinbaseCommitment(hash, nonce: 0xDEADBEEF);

        Assert.NotNull(result);
        // nonce at offset 40, little-endian uint32
        var nonce = BitConverter.ToUInt32(result, 40);
        Assert.Equal(0xDEADBEEF, nonce);
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
    public void BuildAuxPoWHex_ParentHashIsLittleEndian_FIX_B()
    {
        // FIX-B: NBitcoin ToBytes() returns big-endian display order;
        // AuxPoW requires little-endian (reversed).
        var coinbaseTxBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var coinbaseTxHex = coinbaseTxBytes.ToHexString();
        var header = new byte[80];
        // Use a non-trivial header so the hash isn't all zeros
        header[0] = 0x01;
        var branch = new List<byte[]>();

        var result = AuxPowSerializer.BuildAuxPoWHex(coinbaseTxHex, header, branch);
        var resultBytes = result.HexToByteArray();

        // Parent hash follows the coinbase TX bytes
        var parentHashOffset = coinbaseTxBytes.Length;
        var parentHashInResult = resultBytes[parentHashOffset..(parentHashOffset + 32)];

        // Expected: double-SHA256 of header, byte-reversed to little-endian
        var expectedHash = Hashes.DoubleSHA256(header).ToBytes();
        Array.Reverse(expectedHash);

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

        // The branch hash bytes should appear somewhere after the coinbase TX and parent hash
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
