using Miningcore.Configuration;

namespace Miningcore.Blockchain.Bitcoin.Hathor;

/// <summary>
/// Configuration for Hathor (HTR) merge mining alongside a SHA256d pool.
/// Hathor uses its own merged-mining protocol (RFC 0006), not standard AuxPoW:
///   - Block templates via HTTP GET /v1a/get_block_template?address=...&capabilities=mergedmining
///   - The template's "mining base hash" is committed in the parent coinbase as
///     an OP_RETURN push of MAGIC ("Hath") + hash (32 bytes)
///   - Solved parent work is submitted as funds||graph||aux_pow via POST /v1a/submit_block
/// </summary>
public class HathorChainConfig
{
    /// <summary>Unique identifier for this chain. Defaults to "hathor".</summary>
    public string Id { get; set; } = "hathor";

    /// <summary>Display name. Defaults to "Hathor (HTR)".</summary>
    public string Name { get; set; } = "Hathor (HTR)";

    /// <summary>
    /// Internal routing id used to match aux candidates back to this manager.
    /// Hathor itself has no AuxPoW chain id — its commitment is standalone like RSK's.
    /// Must not collide with any auxChains chainId or the RSK chainId on the same pool.
    /// </summary>
    public int ChainId { get; set; } = 4646;

    /// <summary>hathor-core node HTTP API endpoint (typically port 8080, path /v1a is implied).</summary>
    public DaemonEndpointConfig[] Daemons { get; set; }

    /// <summary>HTR address that receives the block reward (from a Hathor wallet, not the node).</summary>
    public string Address { get; set; }

    /// <summary>How often to poll get_block_template (seconds). Defaults to 5.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>If true, log template/submit failures at Debug instead of Warn (e.g. while node syncs).</summary>
    public bool SilentErrors { get; set; } = false;

    /// <summary>Block explorer URL for Hathor block hashes. Use {hash} as placeholder.</summary>
    public string ExplorerBlockLink { get; set; } = "https://explorer.hathor.network/transaction/{hash}";

    /// <summary>Confirmations (blocks on top) required before a block is marked confirmed. Defaults to 300 (reward lock).</summary>
    public int RequiredConfirmations { get; set; } = 300;
}
