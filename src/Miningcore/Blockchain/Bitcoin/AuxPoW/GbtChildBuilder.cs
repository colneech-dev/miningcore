using System.Security.Cryptography;
using Miningcore.Extensions;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.AuxPoW;

/// <summary>
/// A child block assembled locally from a getblocktemplate response.
/// </summary>
public class GbtChildBlock
{
    /// <summary>Child block hash in display (big-endian) hex — the value committed in the parent coinbase.</summary>
    public string Hash { get; init; }
    public byte[] Header { get; init; }
    /// <summary>varint(txCount) + coinbase (witness serialised) + template transactions.</summary>
    public byte[] TxVector { get; init; }
    public string Bits { get; init; }
    public string Target { get; init; }
    public int Height { get; init; }
    public long CoinbaseValue { get; init; }
    public string PreviousBlockHash { get; init; }
    public int ChainId { get; init; }
    /// <summary>Identifies the template content ignoring curtime, so unchanged work keeps the same hash.</summary>
    public string Fingerprint { get; init; }
}

/// <summary>
/// Builds a child block for chains that merge-mine through a plain Bitcoin-style
/// getblocktemplate (extended with auxpow_* fields) instead of createauxblock — e.g. VargaMesh (VMESH).
/// The child coinbase pays the pool address in full (no third-party output), commits to the
/// segwit witness root, and the child header's nonce field carries the chain id.
/// </summary>
public static class GbtChildBuilder
{
    private const string Bech32Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

    public static GbtChildBlock Build(JToken gbt, string payoutAddress, string hrp, int expectedChainId, string coinbaseTag)
    {
        foreach(var key in new[] { "height", "version", "previousblockhash", "bits", "target", "curtime", "coinbasevalue", "transactions", "default_witness_commitment" })
            if(gbt[key] == null)
                throw new InvalidOperationException($"getblocktemplate missing '{key}'");

        var chainId = gbt["auxpow_chainid"]?.Value<int>() ?? throw new InvalidOperationException("template is not an AuxPoW template");
        if(chainId != expectedChainId)
            throw new InvalidOperationException($"unexpected auxpow chain id {chainId} (expected {expectedChainId})");

        var version = gbt["version"].Value<uint>();
        if((version & 0x100) == 0)
            throw new InvalidOperationException("VERSION_AUXPOW bit not set in template version");

        var height = gbt["height"].Value<int>();
        var coinbaseValue = gbt["coinbasevalue"].Value<long>();
        var spk = SegwitScript(payoutAddress, hrp);

        // scriptSig: BIP34 height + coinbaseaux + tag
        var aux = new List<byte>();
        if(gbt["coinbaseaux"] is JObject cbAux)
            foreach(var p in cbAux.Properties())
                aux.AddRange(p.Value.Value<string>().HexToByteArray());

        var scriptSig = Push(ScriptNum(height)).Concat(aux).Concat(Push(System.Text.Encoding.ASCII.GetBytes(coinbaseTag))).ToArray();
        if(scriptSig.Length < 2 || scriptSig.Length > 100)
            throw new InvalidOperationException("coinbase scriptSig length invalid");

        var witnessCommitment = gbt["default_witness_commitment"].Value<string>().HexToByteArray();

        var vin = Cat(new byte[] { 1 }, new byte[32], new byte[] { 0xff, 0xff, 0xff, 0xff }, VarInt(scriptSig.Length), scriptSig, new byte[] { 0xff, 0xff, 0xff, 0xff });
        var outputs = Cat(
            VarInt(2),
            BitConverter.GetBytes((ulong) coinbaseValue), VarInt(spk.Length), spk,
            BitConverter.GetBytes(0UL), VarInt(witnessCommitment.Length), witnessCommitment);

        var ver = BitConverter.GetBytes(2);
        var locktime = new byte[4];
        var coinbaseNoWitness = Cat(ver, vin, outputs, locktime);
        var coinbase = Cat(ver, new byte[] { 0, 1 }, vin, outputs, new byte[] { 1, 0x20 }, new byte[32], locktime);

        var txs = ((JArray) gbt["transactions"]).ToList();
        var leaves = new List<byte[]> { Dsha(coinbaseNoWitness) };
        leaves.AddRange(txs.Select(t => t["txid"].Value<string>().HexToByteArray().ReverseInPlace()));
        var merkleRoot = MerkleRoot(leaves);

        var header = Cat(
            BitConverter.GetBytes(version),
            gbt["previousblockhash"].Value<string>().HexToByteArray().ReverseInPlace(),
            merkleRoot,
            BitConverter.GetBytes(gbt["curtime"].Value<uint>()),
            BitConverter.GetBytes(Convert.ToUInt32(gbt["bits"].Value<string>(), 16)),
            BitConverter.GetBytes((uint) chainId));   // child nonce == chain id

        if(header.Length != 80)
            throw new InvalidOperationException("child header is not 80 bytes");

        var hash = Dsha(header).ReverseInPlace().ToHexString();

        var txVector = Cat(VarInt(leaves.Count), coinbase, Cat(txs.Select(t => t["data"].Value<string>().HexToByteArray()).ToArray()));

        return new GbtChildBlock
        {
            Hash = hash,
            Header = header,
            TxVector = txVector,
            Bits = gbt["bits"].Value<string>(),
            Target = gbt["target"].Value<string>(),
            Height = height,
            CoinbaseValue = coinbaseValue,
            PreviousBlockHash = gbt["previousblockhash"].Value<string>(),
            ChainId = chainId,
            Fingerprint = $"{gbt["previousblockhash"].Value<string>()}|{merkleRoot.ToHexString()}|{gbt["bits"].Value<string>()}|{coinbaseValue}",
        };
    }

    /// <summary>Full block for submitblock: child header + AuxPoW proof + transactions.</summary>
    public static string BuildSubmitHex(GbtChildBlock block, string auxPoWHex) =>
        Cat(block.Header, auxPoWHex.HexToByteArray(), block.TxVector).ToHexString();

    // ---- bech32 witness program -> scriptPubKey ----

    public static byte[] SegwitScript(string address, string hrp)
    {
        address = address.Trim();
        if(address != address.ToLowerInvariant() && address != address.ToUpperInvariant())
            throw new FormatException("mixed-case bech32 address");
        address = address.ToLowerInvariant();

        var sep = address.LastIndexOf('1');
        if(sep < 1 || sep + 7 > address.Length)
            throw new FormatException("malformed bech32 address");
        if(address[..sep] != hrp)
            throw new FormatException($"address prefix '{address[..sep]}' != '{hrp}'");

        var data = address[(sep + 1)..].Select(c =>
        {
            var i = Bech32Charset.IndexOf(c);
            if(i < 0) throw new FormatException("invalid bech32 character");
            return i;
        }).ToArray();

        var checksumConst = Polymod(HrpExpand(hrp).Concat(data).ToArray());
        var witVer = data[0];
        // bech32 (v0) uses constant 1, bech32m (v1+) uses 0x2bc830a3
        if(checksumConst != (witVer == 0 ? 1u : 0x2bc830a3u))
            throw new FormatException("bech32 checksum mismatch");

        var program = ConvertBits(data[1..^6], 5, 8, false);
        if(program.Length < 2 || program.Length > 40)
            throw new FormatException("invalid witness program length");

        return Cat(new[] { (byte) (witVer == 0 ? 0 : 0x50 + witVer) }, new[] { (byte) program.Length }, program);
    }

    private static int[] HrpExpand(string hrp) =>
        hrp.Select(c => (int) c >> 5).Concat(new[] { 0 }).Concat(hrp.Select(c => (int) c & 31)).ToArray();

    private static uint Polymod(int[] values)
    {
        uint[] gen = { 0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3 };
        uint chk = 1;
        foreach(var v in values)
        {
            var b = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ (uint) v;
            for(var i = 0; i < 5; i++)
                if(((b >> i) & 1) != 0)
                    chk ^= gen[i];
        }
        return chk;
    }

    private static byte[] ConvertBits(int[] data, int from, int to, bool pad)
    {
        var acc = 0; var bits = 0; var maxv = (1 << to) - 1;
        var ret = new List<byte>();
        foreach(var v in data)
        {
            acc = (acc << from) | v;
            bits += from;
            while(bits >= to)
            {
                bits -= to;
                ret.Add((byte) ((acc >> bits) & maxv));
            }
            acc &= (1 << bits) - 1;
        }
        if(pad && bits > 0)
            ret.Add((byte) ((acc << (to - bits)) & maxv));
        else if(!pad && (bits >= from || ((acc << (to - bits)) & maxv) != 0))
            throw new FormatException("invalid bech32 padding");
        return ret.ToArray();
    }

    // ---- primitives ----

    private static byte[] ReverseInPlace(this byte[] b) { Array.Reverse(b); return b; }

    private static byte[] Dsha(byte[] b) => SHA256.HashData(SHA256.HashData(b));

    private static byte[] MerkleRoot(List<byte[]> level)
    {
        while(level.Count > 1)
        {
            if((level.Count & 1) == 1)
                level.Add(level[^1]);
            level = Enumerable.Range(0, level.Count / 2).Select(i => Dsha(Cat(level[2 * i], level[2 * i + 1]))).ToList();
        }
        return level[0];
    }

    private static byte[] ScriptNum(long n)
    {
        var o = new List<byte>();
        while(n != 0) { o.Add((byte) (n & 0xff)); n >>= 8; }
        if(o.Count == 0) o.Add(0);
        if((o[^1] & 0x80) != 0) o.Add(0);
        return o.ToArray();
    }

    private static byte[] Push(byte[] d)
    {
        if(d.Length >= 76) throw new InvalidOperationException("push too large");
        return Cat(new[] { (byte) d.Length }, d);
    }

    private static byte[] VarInt(long n) =>
        n < 0xfd ? new[] { (byte) n } :
        n <= 0xffff ? Cat(new byte[] { 0xfd }, BitConverter.GetBytes((ushort) n)) :
        Cat(new byte[] { 0xfe }, BitConverter.GetBytes((uint) n));

    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
