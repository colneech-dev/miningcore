using Miningcore.Configuration;

namespace Miningcore.Blockchain.Bitcoin.RSK;

/// <summary>
/// Configuration for RSK (Rootstock) merge mining alongside a SHA256d pool.
/// RSK uses mnr_getWork / mnr_submitBitcoinBlock instead of the standard
/// AuxPoW getauxblock / submitauxblock protocol.
/// </summary>
public class RskChainConfig
{
    /// <summary>Unique identifier for this chain. Defaults to "rsk".</summary>
    public string Id { get; set; } = "rsk";

    /// <summary>Display name. Defaults to "Rootstock (RSK)".</summary>
    public string Name { get; set; } = "Rootstock (RSK)";

    /// <summary>
    /// Slot in the AuxPoW Merkle tree. RSK mainnet uses chain ID 1 in its own
    /// verification but does NOT validate slot position — it only verifies the
    /// Merkle branch. Use any value not already claimed by another aux chain
    /// (Namecoin = 1, so default here is 151 to avoid collision).
    /// </summary>
    public int ChainId { get; set; } = 151;

    /// <summary>RSKj HTTP-RPC endpoint (typically port 4444 on mainnet).</summary>
    public DaemonEndpointConfig[] Daemons { get; set; }

    /// <summary>How often to poll mnr_getWork (seconds). Defaults to 5.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// If true, log mnr_getWork failures at Debug instead of Warn.
    /// Useful while the RSK node is still syncing.
    /// </summary>
    public bool SilentErrors { get; set; } = false;

    /// <summary>Block explorer URL for RSK block hashes. Use {hash} as placeholder.</summary>
    public string ExplorerBlockLink { get; set; }

    /// <summary>Confirmations required before an RSK block is marked confirmed. Defaults to 100.</summary>
    public int RequiredConfirmations { get; set; } = 100;
}
