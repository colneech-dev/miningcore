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
/// Each aux chain is placed at slot = GetExpectedIndex(nonce, chainId, h) in the tree,
/// where h = log2(treeSize). This is the Namecoin LCG formula used by auxpow.cpp::CAuxPow::check.
/// The nonce is chosen so all chains have unique slots; it is embedded in the coinbase commitment.
/// treeSize is the smallest power of 2 where a valid nonce exists. Empty slots use a zero hash.
/// </summary>
public static class AuxPowSerializer
{
    // Magic bytes that prefix the aux chain commitment in the coinbase scriptSig
    // "fabe" + "mm" (merged mining)
    public static readonly byte[] MergeMiningHeader = new byte[] { 0xfa, 0xbe, 0x6d, 0x6d };

    private static readonly byte[] ZeroHash = new byte[32];

    /// <summary>
    /// Computes the slot (leaf position) for a chain in the aux merkle tree.
    /// This is the LCG formula from Namecoin's auxpow.cpp::getExpectedIndex.
    /// The aux daemon uses the same formula when verifying submitted AuxPoW —
    /// so this MUST be used for both tree construction and proof serialization.
    /// </summary>
    /// <param name="nonce">Nonce embedded in the coinbase commitment's 4-byte nonce field</param>
    /// <param name="chainId">The chain's registered chain ID</param>
    /// <param name="h">log2(treeSize) — number of levels in the tree</param>
    public static uint GetExpectedIndex(uint nonce, int chainId, int h)
    {
        unchecked
        {
            uint rand = nonce;
            rand = rand * 1103515245u + 12345u;
            rand += (uint) chainId;
            rand = rand * 1103515245u + 12345u;
            return h == 0 ? 0u : rand % (1u << h);
        }
    }

    /// <summary>
    /// Builds the aux chain merkle tree from all active aux blocks.
    /// Returns a flat binary-tree node array (root at index 1), the tree size,
    /// and the nonce that was chosen to give each chain a unique LCG-derived slot.
    ///
    /// nodes[1]           = root
    /// nodes[2..3]        = level 1
    /// nodes[treeSize .. 2*treeSize-1] = leaves (leaf i = aux chain at slot i, or zero)
    /// </summary>
    public static (byte[][] nodes, int treeSize, uint nonce) BuildAuxTree(IReadOnlyList<AuxBlockData> activeAuxBlocks)
    {
        if(activeAuxBlocks == null || activeAuxBlocks.Count == 0)
            return (new byte[][] { null!, ZeroHash, ZeroHash }, 1, 0u);

        // Find smallest power-of-2 treeSize and a nonce such that every chain maps
        // to a unique slot under the Namecoin LCG formula (GetExpectedIndex).
        int treeSize = NextPowerOfTwo(activeAuxBlocks.Count);
        uint nonce = FindNonce(activeAuxBlocks, ref treeSize);
        int h = Log2(treeSize);

        // nodes[0] unused; nodes[1]=root; leaves at nodes[treeSize..2*treeSize-1]
        var nodes = new byte[2 * treeSize][];
        nodes[0] = ZeroHash;

        // Fill all leaf slots with zeros (empty slots)
        for(int i = 0; i < treeSize; i++)
            nodes[treeSize + i] = ZeroHash;

        // Place each aux chain hash at its LCG-derived slot.
        // aux.Hash is big-endian (display/RPC format); reverse to little-endian (internal wire format).
        var usedSlots = new HashSet<int>(activeAuxBlocks.Count);
        foreach(var aux in activeAuxBlocks)
        {
            int slot = (int) GetExpectedIndex(nonce, aux.ChainId, h);
            if(!usedSlots.Add(slot))
                throw new InvalidOperationException($"AuxPoW tree slot collision for chainId={aux.ChainId} at slot {slot} — FindNonce should have prevented this");
            nodes[treeSize + slot] = aux.Hash.HexToByteArray().Reverse().ToArray();
        }

        // Build tree bottom-up: each parent = SHA256d(left || right).
        // When treeSize == 1 the single leaf IS the root (nodes[1] already set above); loop is a no-op.
        var combined = new byte[64];
        for(int i = treeSize - 1; i >= 1; i--)
        {
            nodes[2 * i].CopyTo(combined, 0);
            nodes[2 * i + 1].CopyTo(combined, 32);
            nodes[i] = DoubleSHA256(combined);
        }

        return (nodes, treeSize, nonce);
    }

    /// <summary>
    /// Computes the merkle branch from an aux chain's leaf to the tree root.
    /// Returns the branch hashes and the LCG-derived leaf index for AuxPoW serialization.
    /// </summary>
    /// <param name="nonce">The nonce returned by BuildAuxTree (embedded in the coinbase commitment)</param>
    public static (List<byte[]> branch, uint leafIndex) GetAuxMerkleBranch(byte[][]? nodes, int treeSize, uint nonce, int chainId)
    {
        if(nodes == null || treeSize <= 0)
            return (new List<byte[]>(), 0u);

        int h = Log2(treeSize);
        uint leafIndex = GetExpectedIndex(nonce, chainId, h);
        var branch = new List<byte[]>();

        int idx = treeSize + (int) leafIndex;

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
    /// <paramref name="merkleRoot"/> must be in display/big-endian byte order.
    /// The aux daemon (auxpow.cpp::CAuxPow::check) reverses its internally-computed root
    /// before searching the coinbase, so passing the internal/LE tree root will not be found.
    /// </summary>
    public static byte[] BuildCoinbaseCommitment(byte[] merkleRoot, int treeSize, uint nonce = 0)
    {
        if(merkleRoot == null || merkleRoot.Length != 32)
            throw new ArgumentException("merkleRoot must be exactly 32 bytes", nameof(merkleRoot));

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
    /// <param name="parentHeaderBytes">80-byte parent block header (serialised Bitcoin header format)</param>
    /// <param name="coinbaseBranch">Merkle branch from coinbase to parent block merkle root</param>
    /// <param name="auxBranch">Merkle branch from aux chain leaf to aux tree root using Namecoin LCG slot assignment</param>
    /// <param name="auxIndex">Leaf slot of this aux chain determined by FindNonce LCG (not simple chainId % treeSize)</param>
    /// <returns>Hex-encoded AuxPoW proof</returns>
    public static string BuildAuxPoWHex(
        string parentCoinbaseTxHex,
        byte[] parentHeaderBytes,
        List<byte[]> coinbaseBranch,
        List<byte[]>? auxBranch = null,
        uint auxIndex = 0)
    {
        if(parentHeaderBytes.Length != 80)
            throw new ArgumentException($"Parent block header must be exactly 80 bytes, got {parentHeaderBytes.Length}", nameof(parentHeaderBytes));

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

    /// <summary>
    /// Searches for a nonce in [0, 65535] where all chains have unique LCG-derived slots.
    /// If no nonce works at the current treeSize, doubles it and retries.
    /// </summary>
    private static uint FindNonce(IReadOnlyList<AuxBlockData> blocks, ref int treeSize)
    {
        while(true)
        {
            for(uint n = 0; n <= 65535; n++)
            {
                if(!HasLcgCollision(blocks, treeSize, n))
                    return n;
            }
            treeSize <<= 1;
        }
    }

    private static bool HasLcgCollision(IReadOnlyList<AuxBlockData> blocks, int treeSize, uint nonce)
    {
        int h = Log2(treeSize);
        var slots = new HashSet<uint>(blocks.Count);
        foreach(var b in blocks)
            if(!slots.Add(GetExpectedIndex(nonce, b.ChainId, h)))
                return true;
        return false;
    }

    private static int Log2(int n)
    {
        int h = 0;
        while((1 << h) < n) h++;
        return h;
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
