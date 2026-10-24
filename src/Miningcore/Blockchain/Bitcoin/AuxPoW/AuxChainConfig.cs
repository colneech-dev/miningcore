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

    /// <summary>For daemons whose AuxPoW check only requires "chain index == merkle nonce % tree size" and ignores
    /// the chain id (ElevenSeventyFive). The chain is placed at nonce % treeSize instead of the Namecoin LCG slot;
    /// the nonce search still keeps every chain's slot unique. Leave false for Namecoin-style daemons. Defaults to false.</summary>
    public bool SlotByNonce { get; set; } = false;

    /// <summary>RPC connection to the aux chain daemon</summary>
    public DaemonEndpointConfig[] Daemons { get; set; }

    /// <summary>Address to receive aux chain block rewards</summary>
    public string Address { get; set; }

    /// <summary>How often to poll getauxblock (seconds). Defaults to 10, longer than RSK and Hathor (5),
    /// because aux chains can also be invalidated instantly by zmqBlockNotifySocket and after each submit.</summary>
    public int PollIntervalSeconds { get; set; } = 10;

    /// <summary>RPC method to fetch aux work. Use "createauxblock" for chains that require an address param. Defaults to "getauxblock".</summary>
    public string GetAuxBlockMethod { get; set; } = "getauxblock";

    /// <summary>Send Address as the first parameter of GetAuxBlockMethod when it is "getauxblock". Needed by chains whose
    /// getauxblock work request requires a payout address (e.g. ElevenSeventyFive) and have no createauxblock.
    /// Leave false for Namecoin-style daemons, where getauxblock takes (hash, auxpow) and a lone address would be misread.
    /// "createauxblock" always sends the address regardless of this flag. Defaults to false.</summary>
    public bool PassAddressToGetAuxBlock { get; set; } = false;

    /// <summary>Set GetAuxBlockMethod to "getblocktemplate" for chains with no createauxblock RPC (e.g. VMESH):
    /// the child block is assembled locally and solved blocks go out via submitblock. Requires Address
    /// to be a bech32 segwit address with this human-readable prefix (e.g. "vm").</summary>
    public string Bech32Prefix { get; set; }

    /// <summary>Text embedded in the child coinbase scriptSig (template mode only).</summary>
    public string CoinbaseTag { get; set; } = "/miningcore/";

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

    /// <summary>Block explorer URL for aux block hashes. Use {hash} as placeholder.
    /// Example: "https://explorer.coin.org/block/{hash}"</summary>
    public string ExplorerBlockLink { get; set; }

    /// <summary>Confirmations required before an aux block is marked confirmed. Defaults to 100.</summary>
    public int RequiredConfirmations { get; set; } = 100;

    /// <summary>Decimal places of the aux coin, used to turn the coinbase value into the recorded
    /// reward. Defaults to 8 (satoshi-style); set it for chains with a different precision.</summary>
    public int Decimals { get; set; } = 8;

    /// <summary>Estimate the network hashrate from the difficulty and this block time (seconds) instead of asking the daemon.
    /// For multi-algorithm chains whose getnetworkhashps ignores the algorithm and so reports the same figure for every
    /// lane: Myriadcoin has one block a minute shared by five algorithms, so 300 per algorithm. Unset by default.</summary>
    public int? HashrateBlockTimeSeconds { get; set; }

    /// <summary>Difficulty-1 target of the aux chain as hex, for chains whose difficulty is not measured
    /// against Bitcoin's 0x00000000ffff0000... target. Leave unset for every Bitcoin-derived chain.
    /// Used only to express the recorded hash and network difficulty in the aux chain's own units.</summary>
    public string Diff1 { get; set; }
}
