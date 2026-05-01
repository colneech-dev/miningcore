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
///   - Aux chain merkle index (leaf position in aux chain tree)
///   - Parent block header (80 bytes)
///
/// The parent coinbase scriptSig contains exactly ONE commitment to the
/// root of the aux chain merkle tree:
///   0xfabe6d6d + auxMerkleRoot(32) + treeSize(4LE) + nonce(4LE)
///
/// Each aux chain is placed at slot = chainId % treeSize in the tree.
/// treeSize is the smallest power of 2 such that all (chainId % treeSize)
/// are unique. Empty slots use a zero hash.
/// </summary>
public static class AuxPowSerializer
{
    // Magic bytes that prefix the aux chain commitment in the coinbase scriptSig
    // "fabe" + "mm" (merged mining)
    public static readonly byte[] MergeMiningHeader = new byte[] { 0xfa, 0xbe, 0x6d, 0x6d };

    private static readonly byte[] ZeroHash = new byte[32];

    /// <summary>
    /// Builds the aux chain merkle tree from all active aux blocks.
    /// Returns a flat binary-tree node array (root at index 1) and the tree size.
    ///
    /// nodes[1]           = root
    /// nodes[2..3]        = level 1
    /// nodes[treeSize .. 2*treeSize-1] = leaves (leaf i = aux chain at slot i, or zero)
    /// </summary>
    public static (byte[][] nodes, int treeSize) BuildAuxTree(IReadOnlyList<AuxBlockData> activeAuxBlocks)
    {
        if(activeAuxBlocks == null || activeAuxBlocks.Count == 0)
        {
            // Single-slot tree with zero root
            return (new byte[][] { null!, ZeroHash, ZeroHash }, 1);
        }

        // Find smallest power-of-2 tree size with no slot collisions
        int treeSize = NextPowerOfTwo(activeAuxBlocks.Count);
        while(HasSlotCollision(activeAuxBlocks, treeSize))
            treeSize <<= 1;

        // nodes[0] unused; nodes[1]=root; leaves at nodes[treeSize..2*treeSize-1]
        var nodes = new byte[2 * treeSize][];
        nodes[0] = ZeroHash; // unused sentinel

        // Fill all leaf slots with zeros (empty slots)
        for(int i = 0; i < treeSize; i++)
            nodes[treeSize + i] = ZeroHash;

        // Place each aux chain hash at its slot
        // aux.Hash is big-endian (display/RPC format); reverse to little-endian (internal wire format)
        foreach(var aux in activeAuxBlocks)
        {
            int slot = aux.ChainId % treeSize;
            nodes[treeSize + slot] = aux.Hash.HexToByteArray().Reverse().ToArray();
        }

        if(treeSize == 1)
        {
            // Tree of 1: root IS the leaf; node computation loop won't run
            nodes[1] = nodes[treeSize]; // nodes[1] = nodes[1] (same index, already set)
        }
        else
        {
            // Build tree bottom-up: each parent = SHA256d(left || right)
            var combined = new byte[64];
            for(int i = treeSize - 1; i >= 1; i--)
            {
                nodes[2 * i].CopyTo(combined, 0);
                nodes[2 * i + 1].CopyTo(combined, 32);
                nodes[i] = DoubleSHA256(combined);
            }
        }

        return (nodes, treeSize);
    }

    /// <summary>
    /// Computes the merkle branch from an aux chain's leaf to the tree root.
    /// Returns the branch hashes and the leaf index (merkle index for AuxPoW serialization).
    /// </summary>
    public static (List<byte[]> branch, uint leafIndex) GetAuxMerkleBranch(byte[][]? nodes, int treeSize, int chainId)
    {
        if(nodes == null || treeSize <= 0)
            return (new List<byte[]>(), 0u);

        uint leafIndex = (uint)(chainId % treeSize);
        var branch = new List<byte[]>();

        int idx = treeSize + (int)leafIndex; // leaf node index in array

        while(idx > 1)
        {
            branch.Add(nodes[idx ^ 1]); // sibling hash
            idx >>= 1;                  // move to parent
        }

        return (branch, leafIndex);
    }

    /// <summary>
    /// Builds the coinbase commitment bytes to embed in the parent coinbase scriptSig.
    /// Format: 0xfabe6d6d + merkleRoot(32) + treeSize(4LE) + nonce(4LE)
    /// </summary>
    public static byte[] BuildCoinbaseCommitment(byte[] merkleRoot, int treeSize, uint nonce = 0)
    {
        using var ms = new MemoryStream(44);
        ms.Write(MergeMiningHeader);
        ms.Write(merkleRoot);
        ms.Write(BitConverter.GetBytes(treeSize));
        ms.Write(BitConverter.GetBytes(nonce));
        return ms.ToArray();
    }

    /// <summary>
    /// Serializes the full AuxPoW proof for submitauxblock.
    /// Includes the aux chain merkle branch and leaf index so the aux daemon
    /// can verify the chain's position in the multi-chain merkle tree.
    /// </summary>
    /// <param name="parentCoinbaseTxHex">Full serialized parent coinbase transaction (hex)</param>
    /// <param name="parentHeaderBytes">80-byte parent block header</param>
    /// <param name="coinbaseBranch">Merkle branch from coinbase to parent block merkle root</param>
    /// <param name="auxBranch">Merkle branch from aux chain leaf to aux tree root (empty for single-chain tree)</param>
    /// <param name="auxIndex">Leaf position of this aux chain in the aux tree (chainId % treeSize)</param>
    /// <returns>Hex-encoded AuxPoW proof</returns>
    public static string BuildAuxPoWHex(
        string parentCoinbaseTxHex,
        byte[] parentHeaderBytes,
        List<byte[]> coinbaseBranch,
        List<byte[]>? auxBranch = null,
        uint auxIndex = 0)
    {
        var coinbaseTxBytes = parentCoinbaseTxHex.HexToByteArray();

        using var ms = new MemoryStream();

        // 1. Parent coinbase transaction (raw bytes, no length prefix)
        ms.Write(coinbaseTxBytes);

        // 2. Parent block hash (double-SHA256 of parent header, internal/little-endian byte order)
        var parentHash = Hashes.DoubleSHA256(parentHeaderBytes).ToBytes();
        ms.Write(parentHash);

        // 3. Coinbase merkle branch (path from coinbase tx to block merkle root)
        WriteVarInt(ms, (ulong) coinbaseBranch.Count);
        foreach(var branch in coinbaseBranch)
            ms.Write(branch);

        // 4. Coinbase branch side mask (always 0: coinbase is always the first/leftmost tx)
        ms.Write(BitConverter.GetBytes(0u));

        // 5. Aux chain merkle branch (path from aux chain leaf to aux tree root)
        var auxBranchList = auxBranch ?? new List<byte[]>();
        WriteVarInt(ms, (ulong) auxBranchList.Count);
        foreach(var h in auxBranchList)
            ms.Write(h);

        // 6. Aux chain merkle index (leaf position; encodes the path direction at each level)
        ms.Write(BitConverter.GetBytes(auxIndex));

        // 7. Parent block header (80 bytes)
        ms.Write(parentHeaderBytes);

        return ms.ToArray().ToHexString();
    }

    private static bool HasSlotCollision(IReadOnlyList<AuxBlockData> blocks, int treeSize)
    {
        var slots = new HashSet<int>(blocks.Count);
        foreach(var b in blocks)
            if(!slots.Add(b.ChainId % treeSize))
                return true;
        return false;
    }

    private static int NextPowerOfTwo(int n)
    {
        if(n <= 1) return 1;
        int p = 1;
        while(p < n) p <<= 1;
        return p;
    }

    private static byte[] DoubleSHA256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var first = sha256.ComputeHash(data, 0, data.Length);
        return sha256.ComputeHash(first);
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
