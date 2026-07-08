using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.Hathor;

/// <summary>
/// Byte-exact serialization for Hathor merged mining, ported from hathor-core:
///   - block "funds" struct   (vertex_parser/_block.py::serialize_block_funds)
///   - block "graph" struct   (vertex_parser/_common.py::serialize_graph_fields + data)
///   - mining base hash       (Block.get_mining_base_hash: sha256d(sha256(funds) || sha256(graph)))
///   - BitcoinAuxPow bytes    (transaction/aux_pow.py::__bytes__)
///   - weight → target        (difficulty.py Weight.to_target: 2**(256-weight) - 1, float semantics)
/// </summary>
public static class HathorSerializer
{
    /// <summary>ASCII "Hath" — must immediately precede the mining base hash in the coinbase.</summary>
    public static readonly byte[] MagicNumber = { 0x48, 0x61, 0x74, 0x68 };

    /// <summary>
    /// Serialize the block funds struct: signal_bits(1) + version(1) + num_outputs(1) + outputs.
    /// Each output: value (4B or 8B big-endian, hathorlib output_value_to_bytes) +
    /// token_data(1) + script_len(2 BE) + script.
    /// </summary>
    public static byte[] SerializeFunds(int signalBits, int version, IReadOnlyList<(long Value, byte TokenData, byte[] Script)> outputs)
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte) signalBits);
        ms.WriteByte((byte) version);
        ms.WriteByte((byte) outputs.Count);

        foreach(var (value, tokenData, script) in outputs)
        {
            var valueBytes = EncodeOutputValue(value);
            ms.Write(valueBytes, 0, valueBytes.Length);
            ms.WriteByte(tokenData);
            ms.WriteByte((byte) (script.Length >> 8));
            ms.WriteByte((byte) (script.Length & 0xff));
            ms.Write(script, 0, script.Length);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// hathorlib output_value_to_bytes: values above int.MaxValue are stored as
    /// 8-byte big-endian two's-complement of the NEGATED value, else 4-byte big-endian.
    /// </summary>
    public static byte[] EncodeOutputValue(long value)
    {
        if(value <= 0)
            throw new ArgumentException("Hathor output value must be positive");

        if(value > int.MaxValue)
        {
            var neg = -value;
            var b = new byte[8];
            for(var i = 7; i >= 0; i--)
            {
                b[i] = (byte) (neg & 0xff);
                neg >>= 8;
            }
            return b;
        }
        else
        {
            return new[]
            {
                (byte) ((value >> 24) & 0xff),
                (byte) ((value >> 16) & 0xff),
                (byte) ((value >> 8) & 0xff),
                (byte) (value & 0xff),
            };
        }
    }

    /// <summary>
    /// Serialize the block graph struct: weight (8B IEEE-754 double BE) + timestamp (4B BE) +
    /// num_parents(1) + parents (32B each) + data_len(1) + data.
    /// </summary>
    public static byte[] SerializeGraph(double weight, uint timestamp, IReadOnlyList<byte[]> parents, byte[] data)
    {
        using var ms = new MemoryStream();

        var w = BitConverter.GetBytes(weight);
        if(BitConverter.IsLittleEndian)
            Array.Reverse(w);
        ms.Write(w, 0, 8);

        ms.WriteByte((byte) ((timestamp >> 24) & 0xff));
        ms.WriteByte((byte) ((timestamp >> 16) & 0xff));
        ms.WriteByte((byte) ((timestamp >> 8) & 0xff));
        ms.WriteByte((byte) (timestamp & 0xff));

        ms.WriteByte((byte) parents.Count);
        foreach(var parent in parents)
        {
            if(parent.Length != 32)
                throw new ArgumentException("Hathor parent hash must be 32 bytes");
            ms.Write(parent, 0, 32);
        }

        data ??= Array.Empty<byte>();
        if(data.Length > 255)
            throw new ArgumentException("Hathor block data too long");
        ms.WriteByte((byte) data.Length);
        ms.Write(data, 0, data.Length);

        return ms.ToArray();
    }

    /// <summary>
    /// Block.get_mining_base_hash(): sha256d( sha256(funds) || sha256(graph + headers) ).
    /// Block templates carry no headers, so the second hash is just sha256(graph).
    /// </summary>
    public static byte[] ComputeMiningBaseHash(byte[] funds, byte[] graph)
    {
        using var sha = SHA256.Create();
        var fundsHash = sha.ComputeHash(funds);
        var graphHash = sha.ComputeHash(graph);

        var miningHeader = new byte[64];
        fundsHash.CopyTo(miningHeader, 0);
        graphHash.CopyTo(miningHeader, 32);

        return sha.ComputeHash(sha.ComputeHash(miningHeader));
    }

    /// <summary>
    /// Weight.to_target(): U256(2**(256 - weight) - 1) with Python float semantics
    /// (the -1 is a no-op in double math for realistic weights; the float value is
    /// converted exactly to an integer). Returns 32-byte big-endian target.
    /// </summary>
    public static byte[] WeightToTargetBytes(double weight)
    {
        var t = (System.Numerics.BigInteger) (Math.Pow(2.0, 256.0 - weight) - 1.0);
        if(t < 0)
            t = 0;

        var le = t.ToByteArray(); // little-endian, may include sign byte
        var be = new byte[32];
        for(var i = 0; i < le.Length && i < 32; i++)
            be[31 - i] = le[i];
        return be;
    }

    /// <summary>
    /// BitcoinAuxPow.__bytes__():
    /// header[0..36] + varint|coinbase_head + varint|coinbase_tail + varint|merkle_path(32B each) + header[68..80].
    /// The coinbase is split at the 32-byte mining base hash; coinbase_head MUST end with "Hath".
    /// </summary>
    public static byte[] BuildAuxPowBytes(byte[] header, byte[] coinbaseHead, byte[] coinbaseTail, IReadOnlyList<byte[]> merklePath)
    {
        if(header.Length != 80)
            throw new ArgumentException("parent header must be 80 bytes");

        using var ms = new MemoryStream();
        ms.Write(header, 0, 36);
        WriteVarInt(ms, (ulong) coinbaseHead.Length);
        ms.Write(coinbaseHead, 0, coinbaseHead.Length);
        WriteVarInt(ms, (ulong) coinbaseTail.Length);
        ms.Write(coinbaseTail, 0, coinbaseTail.Length);
        WriteVarInt(ms, (ulong) merklePath.Count);
        foreach(var link in merklePath)
        {
            if(link.Length != 32)
                throw new ArgumentException("merkle path link must be 32 bytes");
            ms.Write(link, 0, 32);
        }
        ms.Write(header, 68, 12);
        return ms.ToArray();
    }

    /// <summary>
    /// BitcoinAuxPow.calculate_hash(): the on-chain Hathor block hash.
    /// coinbase_txid = sha256d(head || base_hash || tail);
    /// merkle_root = REVERSED( fold(coinbase_txid, path) );
    /// hash = sha256d( header[0..36] || merkle_root || header[68..80] ).
    /// </summary>
    public static byte[] ComputeHathorBlockHash(byte[] header, byte[] coinbaseHead, byte[] baseHash, byte[] coinbaseTail, IReadOnlyList<byte[]> merklePath)
    {
        using var sha = SHA256.Create();

        byte[] Sha256d(byte[] input) => sha.ComputeHash(sha.ComputeHash(input));

        var coinbase = new byte[coinbaseHead.Length + baseHash.Length + coinbaseTail.Length];
        coinbaseHead.CopyTo(coinbase, 0);
        baseHash.CopyTo(coinbase, coinbaseHead.Length);
        coinbaseTail.CopyTo(coinbase, coinbaseHead.Length + baseHash.Length);

        var current = Sha256d(coinbase);
        foreach(var link in merklePath)
        {
            var buf = new byte[64];
            current.CopyTo(buf, 0);
            link.CopyTo(buf, 32);
            current = Sha256d(buf);
        }

        var reversedRoot = current.Reverse().ToArray();

        var final = new byte[80];
        Array.Copy(header, 0, final, 0, 36);
        reversedRoot.CopyTo(final, 36);
        Array.Copy(header, 68, final, 68, 12);

        return Sha256d(final);
    }

    /// <summary>Bitcoin-style varint.</summary>
    public static void WriteVarInt(Stream s, ulong value)
    {
        if(value < 0xfd)
            s.WriteByte((byte) value);
        else if(value <= 0xffff)
        {
            s.WriteByte(0xfd);
            s.WriteByte((byte) (value & 0xff));
            s.WriteByte((byte) ((value >> 8) & 0xff));
        }
        else if(value <= 0xffffffff)
        {
            s.WriteByte(0xfe);
            for(var i = 0; i < 4; i++)
                s.WriteByte((byte) ((value >> (8 * i)) & 0xff));
        }
        else
        {
            s.WriteByte(0xff);
            for(var i = 0; i < 8; i++)
                s.WriteByte((byte) ((value >> (8 * i)) & 0xff));
        }
    }

    /// <summary>
    /// Parse a get_block_template JSON response into serialized funds/graph plus metadata.
    /// </summary>
    public static (byte[] Funds, byte[] Graph, double Weight, long Height) ParseTemplate(JObject template)
    {
        var signalBits = template.Value<int?>("signal_bits") ?? 0;
        var version = template.Value<int?>("version") ?? 3;
        var weight = template.Value<double>("weight");
        var timestamp = template.Value<uint>("timestamp");

        var parents = (template["parents"] as JArray ?? new JArray())
            .Select(p => Convert.FromHexString(p.Value<string>()))
            .ToList();

        var outputs = (template["outputs"] as JArray ?? new JArray())
            .Select(o => (
                Value: o.Value<long>("value"),
                TokenData: (byte) (o.Value<int?>("token_data") ?? 0),
                Script: DecodeScript(o.Value<string>("script"))))
            .ToList();

        var dataStr = template.Value<string>("data") ?? "";
        var data = dataStr.Length > 0 ? Convert.FromBase64String(dataStr) : Array.Empty<byte>();

        var height = template["metadata"]?.Value<long?>("height") ?? 0;

        var funds = SerializeFunds(signalBits, version, outputs);
        var graph = SerializeGraph(weight, timestamp, parents, data);

        return (funds, graph, weight, height);
    }

    private static byte[] DecodeScript(string script)
    {
        if(string.IsNullOrEmpty(script))
            return Array.Empty<byte>();
        return Convert.FromBase64String(script);
    }
}
