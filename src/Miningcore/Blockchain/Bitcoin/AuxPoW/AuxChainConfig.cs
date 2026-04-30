using Miningcore.Configuration;

namespace Miningcore.Blockchain.Bitcoin.AuxPoW;

/// <summary>
/// Configuration for a single auxiliary chain (e.g. Namecoin) to merge-mine
/// alongside the primary pool chain.
/// </summary>
public class AuxChainConfig
{
    /// <summary>Unique identifier for this aux chain (e.g. "namecoin")</summary>
    public string Id { get; set; }

    /// <summary>Display name (e.g. "Namecoin")</summary>
    public string Name { get; set; }

    /// <summary>Aux chain ID used in AuxPoW merkle tree (Namecoin = 1)</summary>
    public int ChainId { get; set; }

    /// <summary>RPC connection to the aux chain daemon</summary>
    public DaemonEndpointConfig[] Daemons { get; set; }

    /// <summary>Address to receive aux chain block rewards</summary>
    public string Address { get; set; }

    /// <summary>How often to poll getauxblock (seconds). Defaults to 10.</summary>
    public int PollIntervalSeconds { get; set; } = 10;

    /// <summary>RPC method to fetch aux work. Use "createauxblock" for chains that require an address param. Defaults to "getauxblock".</summary>
    public string GetAuxBlockMethod { get; set; } = "getauxblock";

    /// <summary>If true, log getauxblock failures at Debug instead of Warn (useful for chains that are still syncing or not always available).</summary>
    public bool SilentErrors { get; set; } = false;
}
