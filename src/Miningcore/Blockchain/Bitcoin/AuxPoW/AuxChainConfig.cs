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

    /// <summary>RPC method to submit a solved aux block. Use "getauxblock" for older daemons that accept the hash+auxpow via the same method. Defaults to "submitauxblock".</summary>
    public string SubmitAuxBlockMethod { get; set; } = "submitauxblock";

    /// <summary>If true, log getauxblock failures at Debug instead of Warn (useful for chains that are still syncing or not always available).</summary>
    public bool SilentErrors { get; set; } = false;

    /// <summary>Optional ZeroMQ socket for instant new-block notifications (e.g. "tcp://namecoin:28336").
    /// Matches the daemon's -zmqpubhashblock setting. When set, the aux block is invalidated immediately
    /// on each ZMQ message instead of waiting for the next poll interval.</summary>
    public string ZmqBlockNotifySocket { get; set; }

    /// <summary>ZMQ topic to subscribe to. Defaults to "hashblock".</summary>
    public string ZmqBlockNotifyTopic { get; set; }
}
