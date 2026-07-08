using System;
using System.Linq;
using Miningcore.Blockchain.Bitcoin.Hathor;
using Miningcore.Extensions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Miningcore.Tests.Blockchain.Bitcoin;

/// <summary>
/// Vectors generated independently with a Python replica of hathor-core's own
/// serialization (vertex_parser/_block.py, hathorlib output_value_to_bytes,
/// aux_pow.py, difficulty.py) using a real mainnet template fetched 2026-07-08
/// (weight=56.91169126895325, timestamp=1783531454, height 6763200 era).
/// </summary>
public class HathorSerializerTests
{
    private const string ScriptHex = "76a914abcdefabcdefabcdefabcdefabcdefabcdefabcd88ac";

    private static readonly string[] Parents =
    {
        "00000000000000044eb655b879d0bb9fc4232f9cee5a479e4af7e6b35af5d111",
        "000000007da62808f5e35060b81b134e987f87855e29a387b35d25abe1fa713b",
        "0000002892c3a0ed881fce9221b785ce02721a1d222b9a5170a63fb9c0b08b79",
    };

    private const double Weight = 56.91169126895325;
    private const uint Timestamp = 1783531454;

    private const string ExpectedFunds = "000301000000c800001976a914abcdefabcdefabcdefabcdefabcdefabcdefabcd88ac";
    private const string ExpectedGraph =
        "404c74b24cac19fa6a4e87be0300000000000000044eb655b879d0bb9fc4232f9cee5a479e4af7e6b35af5d111" +
        "000000007da62808f5e35060b81b134e987f87855e29a387b35d25abe1fa713b" +
        "0000002892c3a0ed881fce9221b785ce02721a1d222b9a5170a63fb9c0b08b7900";
    private const string ExpectedBaseHash = "6108d1113e4cea09fedaca9e096ec5158c5437fd689a902ecc1ca52e950e4b60";
    private const string ExpectedTarget = "0000000000000088146b65688070000000000000000000000000000000000000";

    private static byte[] Funds() => HathorSerializer.SerializeFunds(0, 3,
        new[] { (200L, (byte) 0, ScriptHex.HexToByteArray()) });

    private static byte[] Graph() => HathorSerializer.SerializeGraph(Weight, Timestamp,
        Parents.Select(p => p.HexToByteArray()).ToList(), Array.Empty<byte>());

    [Fact]
    public void SerializeFunds_MatchesHathorCore()
    {
        Assert.Equal(ExpectedFunds, Funds().ToHexString());
    }

    [Fact]
    public void SerializeFunds_LargeValue_Uses8ByteNegatedEncoding()
    {
        var funds = HathorSerializer.SerializeFunds(1, 3, new[] { (0x80000000L, (byte) 0, new byte[] { 0x51 }) });
        Assert.Equal("010301ffffffff8000000000000151", funds.ToHexString());
    }

    [Fact]
    public void SerializeGraph_MatchesHathorCore()
    {
        Assert.Equal(ExpectedGraph, Graph().ToHexString());
    }

    [Fact]
    public void ComputeMiningBaseHash_MatchesHathorCore()
    {
        var baseHash = HathorSerializer.ComputeMiningBaseHash(Funds(), Graph());
        Assert.Equal(ExpectedBaseHash, baseHash.ToHexString());
    }

    [Theory]
    [InlineData(14.0, "0004000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(70.09, "000000000000000003c2124066e3836000000000000000000000000000000000")]
    [InlineData(Weight, ExpectedTarget)]
    public void WeightToTarget_MatchesPythonFloatSemantics(double weight, string expectedHex)
    {
        Assert.Equal(expectedHex, HathorSerializer.WeightToTargetBytes(weight).ToHexString());
    }

    [Fact]
    public void BuildAuxPowBytes_MatchesHathorCoreLayout()
    {
        var header = Enumerable.Range(0, 80).Select(i => (byte) i).ToArray();
        var head = "prefix-bytes"u8.ToArray().Concat(HathorSerializer.MagicNumber).ToArray();
        var tail = "suffix"u8.ToArray();
        var path = new[] { Enumerable.Repeat((byte) 1, 32).ToArray(), Enumerable.Repeat((byte) 2, 32).ToArray() };

        var auxPow = HathorSerializer.BuildAuxPowBytes(header, head, tail, path);

        Assert.Equal(
            "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f2021222310" +
            "7072656669782d627974657348617468" + "0673756666697802" +
            "0101010101010101010101010101010101010101010101010101010101010101" +
            "0202020202020202020202020202020202020202020202020202020202020202" +
            "4445464748494a4b4c4d4e4f",
            auxPow.ToHexString());
    }

    [Fact]
    public void ComputeHathorBlockHash_MatchesHathorCore()
    {
        var header = Enumerable.Range(0, 80).Select(i => (byte) i).ToArray();
        var head = "prefix-bytes"u8.ToArray().Concat(HathorSerializer.MagicNumber).ToArray();
        var tail = "suffix"u8.ToArray();
        // path links passed in DISPLAY (reversed) order, matching what the manager submits
        var path = new[] { Enumerable.Repeat((byte) 1, 32).ToArray(), Enumerable.Repeat((byte) 2, 32).ToArray() }
            .Select(p => p.Reverse().ToArray()).ToArray();

        var hash = HathorSerializer.ComputeHathorBlockHash(header, head, ExpectedBaseHash.HexToByteArray(), tail, path);

        Assert.Equal("0a0d56ad9d7943654cc3f264a7eceae66a852bb2cdf094790ffef2bfe22ab3dc", hash.ToHexString());
    }

    [Fact]
    public void ParseTemplate_RealMainnetShape_RoundTrips()
    {
        var json = JObject.Parse(@"{
            ""timestamp"": 1783531454,
            ""version"": 3,
            ""weight"": 56.91169126895325,
            ""signal_bits"": 0,
            ""parents"": [
                ""00000000000000044eb655b879d0bb9fc4232f9cee5a479e4af7e6b35af5d111"",
                ""000000007da62808f5e35060b81b134e987f87855e29a387b35d25abe1fa713b"",
                ""0000002892c3a0ed881fce9221b785ce02721a1d222b9a5170a63fb9c0b08b79""],
            ""outputs"": [{""value"": 200, ""token_data"": 0, ""script"": ""dqkUq83vq83vq83vq83vq83vq83vq82IrA==""}],
            ""metadata"": {""height"": 6763200},
            ""data"": """"
        }");

        var (funds, graph, weight, height) = HathorSerializer.ParseTemplate(json);

        Assert.Equal(ExpectedFunds, funds.ToHexString());
        Assert.Equal(ExpectedGraph, graph.ToHexString());
        Assert.Equal(Weight, weight);
        Assert.Equal(6763200, height);

        Assert.Equal(ExpectedBaseHash, HathorSerializer.ComputeMiningBaseHash(funds, graph).ToHexString());
    }

    [Fact]
    public void MagicNumber_IsHath()
    {
        Assert.Equal(new byte[] { 0x48, 0x61, 0x74, 0x68 }, HathorSerializer.MagicNumber);
        Assert.Equal("Hath", System.Text.Encoding.ASCII.GetString(HathorSerializer.MagicNumber));
    }

    /// <summary>
    /// Independent oracle: hathor-core's own cpu-miner doctest for build_merkle_root_from_path
    /// (merged_mining/bitcoin.py). Inputs are internal-order (stratum) hashes; first is the
    /// coinbase txid, rest is the merkle branch; expected internal-order root from the doctest.
    /// </summary>
    [Fact]
    public void FoldMerkle_MatchesHathorCoreDoctest()
    {
        var internalHashes = new[]
        {
            "240ac8a678139b0df3984aaff62571e0c9bd7f4c32fbc2720332902619d7beb5",
            "dcf3763488560085aa77b99a988917a7849f7fea2bff894efe3a5e9d9f70c9fe",
            "a3b2ce71fdf67801b596d9e352e3a4f8a3e6dc84bfe752c387479fab67fe7c21",
            "7473649a92f93558aa53cedf927a625fcbec8c1ffa5eea0074c8c2db57a04a34",
            "bf300fa3af6ce187fcf96a990a0e00c276ccf324141d278704a6ecfd9e366183",
            "e7d28e53c5982de2948dce75b2d3c9c01281950ba57c08c140b3d10398af1ec4",
            "e4a0d64bc740d24c476560753032e255555a33b6ca42e4137ff16ee7b3339f85",
            "674ff1f7ba4b7c03637be3088414e5d4b5212461891f0f22ece0222235686eee",
        }.Select(h => h.HexToByteArray()).ToArray();

        var txidDisplay = internalHashes[0].Reverse().ToArray();
        var pathDisplay = internalHashes.Skip(1).Select(h => h.Reverse().ToArray()).ToList();

        var internalRoot = HathorSerializer.FoldMerkleToInternalRoot(txidDisplay, pathDisplay);

        Assert.Equal("9fedb4e40f8532eac81338b479049a2e6bcee68d78b56767d43ebf1020ef8a68", internalRoot.ToHexString());
    }
}
