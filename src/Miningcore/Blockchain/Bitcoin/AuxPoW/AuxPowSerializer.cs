#nullable enable

using System.Security.Cryptography;
using Miningcore.Extensions;
using NBitcoin;
using NBitcoin.Crypto;

namespace Miningcore.Blockchain.Bitcoin.AuxPoW;

/// <summary>
/// Builds the AuxPoW proof structure required by auxiliary chain daemons.
///
/// AuxPoW format (submitted to submitauxblock):
///   - Parent coinbase transaction (full)
///   - Parent block hash (32 bytes)
///   - Coinbase merkle branch (path from coinbase to merkle root)
///   - Coinbase merkle index (always 0)
///   - Aux chain merkle branch (path within aux chain merkle tree)
///   - Aux chain merkle index
///   - Parent block header (80 bytes)
///
/// The aux chain block hash must be committed in the parent coinbase scriptSig
/// using the standard merge mining header:
///   0xfabe6d6d + auxHash + merkleSize + merkleNonce
/// </summary>
public static class AuxPowSerializer
{
    // Magic bytes that prefix the aux chain commitment in the coinbase scriptSig
    // "fabe" + "mm" (merged mining)
    public static readonly byte[] MergeMiningHeader = new byte[] { 0xfa, 0xbe, 0x6d, 0x6d };

    /// <summary>
    /// Builds the coinbase commitment bytes to embed in the parent coinbase scriptSig.
    /// Format: 0xfabe6d6d + auxHash(32) + numChains(4LE) + nonce(4LE)
    /// </summary>
    /// <param name="auxHash">Hex-encoded aux block hash (must be valid 64 hex chars = 32 bytes)</param>
    /// <param name="numChains">Number of chains (default 1)</param>
    /// <param name="nonce">Merge mining nonce</param>
    /// <returns>Commitment bytes, or null if hash is invalid</returns>
    public static byte[]? BuildCoinbaseCommitment(string auxHash, int numChains = 1, uint nonce = 0)
    {
        // Validate input
        if(string.IsNullOrWhiteSpace(auxHash))
            return null;

        // Aux hash must be exactly 64 hex characters (32 bytes)
        // If it's too short, the daemon is still syncing or has returned incomplete data
        var cleanHash = auxHash.StartsWith("0x") ? auxHash[2..] : auxHash;
        if(cleanHash.Length != 64)
            return null; // Invalid hash length - daemon likely syncing or returning bad data

        try
        {
            // AuxPoW spec: aux hash must be in little-endian (byte-reversed) in the coinbase
            var hashBytes = cleanHash.HexToByteArray();
            
            // Double-check we got exactly 32 bytes
            if(hashBytes.Length != 32)
                return null;

            var reversedHash = hashBytes.Reverse().ToArray();

            // AuxPoW spec: hash must be in little-endian in the coinbase
            // getauxblock returns the hash in the correct byte order for embedding
            using var ms = new MemoryStream();
            ms.Write(MergeMiningHeader);
            ms.Write(reversedHash);                                    // aux block hash (32 bytes)
            ms.Write(BitConverter.GetBytes(numChains));                // number of chains (4 bytes LE)
            ms.Write(BitConverter.GetBytes(nonce));                    // nonce (4 bytes LE)
            return ms.ToArray();
        }
        catch
        {
            // If conversion fails (invalid hex chars, etc), return null
            return null;
        }
    }

    /// <summary>
    /// Serializes the full AuxPoW proof for submitauxblock.
    /// </summary>
    /// <param name="parentCoinbaseTx">Full serialized parent coinbase transaction (hex)</param>
    /// <param name="parentHeaderBytes">80-byte parent block header</param>
    /// <param name="coinbaseBranch">Merkle branch from coinbase to merkle root (list of 32-byte hashes)</param>
    /// <returns>Hex-encoded AuxPoW proof</returns>
    public static string BuildAuxPoWHex(
        string parentCoinbaseTxHex,
        byte[] parentHeaderBytes,
        List<byte[]> coinbaseBranch)
    {
        var coinbaseTxBytes = parentCoinbaseTxHex.HexToByteArray();

        using var ms = new MemoryStream();

        // 1. Parent coinbase transaction
        WriteVarInt(ms, (ulong) coinbaseTxBytes.Length);
        ms.Write(coinbaseTxBytes);

        // 2. Parent block hash (double-SHA256 of parent header, little-endian)
        var parentHash = Hashes.DoubleSHA256(parentHeaderBytes).ToBytes();
        ms.Write(parentHash);

        // 3. Coinbase merkle branch (branch from coinbase tx to merkle root)
        WriteVarInt(ms, (ulong) coinbaseBranch.Count);
        foreach(var branch in coinbaseBranch)
            ms.Write(branch);

        // 4. Coinbase branch side mask (0 = always left, coinbase is always first tx)
        ms.Write(BitConverter.GetBytes(0u));

        // 5. Aux chain merkle branch (empty - only one aux chain)
        WriteVarInt(ms, 0);

        // 6. Aux chain branch side mask
        ms.Write(BitConverter.GetBytes(0u));

        // 7. Parent block header (80 bytes)
        ms.Write(parentHeaderBytes);

        return ms.ToArray().ToHexString();
    }

    private static void WriteVarInt(Stream stream, ulong value)
    {
        if(value < 0xfd)
        {
            stream.WriteByte((byte) value);
        }
        else if(value <= 0xffff)
        {
            stream.WriteByte(0xfd);
            stream.Write(BitConverter.GetBytes((ushort) value));
        }
        else if(value <= 0xffffffff)
        {
            stream.WriteByte(0xfe);
            stream.Write(BitConverter.GetBytes((uint) value));
        }
        else
        {
            stream.WriteByte(0xff);
            stream.Write(BitConverter.GetBytes(value));
        }
    }
}
