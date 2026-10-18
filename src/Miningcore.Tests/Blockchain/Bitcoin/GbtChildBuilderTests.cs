using System;
using System.Linq;
using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Miningcore.Extensions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Miningcore.Tests.Blockchain.Bitcoin;

public class GbtChildBuilderTests
{
    private const string Address = "vm1qsfsgj70pdv86nguvexzvsflmcknz4lrsj87wx9";
    private const string Tx1 = "01000000010000000000000000000000000000000000000000000000000000000000000000ffffffff0100000000000000000000000000";

    private static JObject Template(string txid1 = null) => JObject.Parse($@"{{
        ""height"": 22700, ""version"": 536871168,
        ""previousblockhash"": ""00000000000000aa{new string('1', 48)}"",
        ""bits"": ""1a0fffff"",
        ""target"": ""0000000fffff0000000000000000000000000000000000000000000000000000"",
        ""curtime"": 1760000000, ""coinbasevalue"": 2500000000,
        ""transactions"": [{{ ""txid"": ""{txid1 ?? "TXID"}"", ""data"": ""{Tx1}"" }}],
        ""auxpow"": true, ""auxpow_chainid"": 22093,
        ""default_witness_commitment"": ""6a24aa21a9ed{new string('2', 64)}"",
        ""coinbaseaux"": {{ ""flags"": """" }}
    }}");

    // txid of Tx1 = reversed double-SHA256 of its bytes
    private static string Txid1() =>
        System.Security.Cryptography.SHA256.HashData(System.Security.Cryptography.SHA256.HashData(Tx1.HexToByteArray()))
            .AsEnumerable().Reverse().ToArray().ToHexString();

    [Fact]
    public void Bech32_matches_bridge_script()
    {
        Assert.Equal("001482608979e16b0fa9a38cc984c827fbc5a62afc70", GbtChildBuilder.SegwitScript(Address, "vm").ToHexString());
    }

    [Fact]
    public void Bech32_rejects_bad_checksum_and_prefix()
    {
        Assert.Throws<FormatException>(() => GbtChildBuilder.SegwitScript(Address[..^1] + "q", "vm"));
        Assert.Throws<FormatException>(() => GbtChildBuilder.SegwitScript(Address, "bc"));
    }

    [Fact]
    public void Builds_child_identical_to_reference_implementation()
    {
        // expected values computed with the bridge's own helpers (coordinator/vmesh_template_builder.py), minus the dev-fee output
        var child = GbtChildBuilder.Build(Template(Txid1()), Address, "vm", 22093, "/test/");

        Assert.Equal("7984b314e56ac4906d01b42ca98b3581c40e2b94d3df61b226970d3e97ac1a7c", child.Hash);
        Assert.Equal(80, child.Header.Length);
        Assert.StartsWith("02020000000001010000", child.TxVector.ToHexString());
        Assert.Equal(22700, child.Height);
        Assert.Equal(2_500_000_000L, child.CoinbaseValue);
    }

    [Fact]
    public void Rejects_wrong_chain_or_missing_auxpow_bit()
    {
        var t = Template(Txid1());
        t["auxpow_chainid"] = 5;
        Assert.Throws<InvalidOperationException>(() => GbtChildBuilder.Build(t, Address, "vm", 22093, "/t/"));

        t = Template(Txid1());
        t["version"] = 536870912;   // 0x20000000, no 0x100
        Assert.Throws<InvalidOperationException>(() => GbtChildBuilder.Build(t, Address, "vm", 22093, "/t/"));
    }

    [Fact]
    public void Fingerprint_ignores_curtime()
    {
        var a = GbtChildBuilder.Build(Template(Txid1()), Address, "vm", 22093, "/t/");
        var t = Template(Txid1()); t["curtime"] = 1760000030;
        var b = GbtChildBuilder.Build(t, Address, "vm", 22093, "/t/");
        Assert.NotEqual(a.Hash, b.Hash);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void Submit_hex_is_header_auxpow_txvector()
    {
        var child = GbtChildBuilder.Build(Template(Txid1()), Address, "vm", 22093, "/t/");
        var hex = GbtChildBuilder.BuildSubmitHex(child, "abcd");
        Assert.Equal(child.Header.ToHexString() + "abcd" + child.TxVector.ToHexString(), hex);
    }
}
