using Miningcore.Configuration;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.Configuration;

public class BitcoinPoolConfigExtra
{
    public BitcoinAddressType AddressType { get; set; } = BitcoinAddressType.Legacy;

    public string BechPrefix { get; set; } = "bc";

    /// <summary>
    /// Maximum number of tracked jobs.
    /// Default: 12 - you should increase this value if your blockrefreshinterval is higher than 300ms
    /// </summary>
    public int? MaxActiveJobs { get; set; }

    /// <summary>
    /// Set to true to limit RPC commands to old Bitcoin command set
    /// </summary>
    public bool? HasLegacyDaemon { get; set; }

    /// <summary>
    /// Set to true to fall back to multiple sendtoaddress RPC calls for payments
    /// </summary>
    public bool HasBrokenSendMany { get; set; } = false;

    /// <summary>
    /// Arbitrary string appended at end of coinbase tx
    /// Overrides property of same name from BitcoinTemplate
    /// </summary>
    public string CoinbaseTxComment { get; set; }

    /// <summary>
    /// Blocktemplate stream published via ZMQ
    /// </summary>
    public ZmqPubSubEndpointConfig BtStream { get; set; }

    /// <summary>
    /// Custom Arguments for getblocktemplate RPC
    /// </summary>
    public JToken GBTArgs { get; set; }

    /// <summary>
    /// Override the version-rolling mask negotiated with miners (hex string, e.g. "1ffce000").
    /// Use to exclude version bits reserved by the coin (e.g. LCC uses bit 16 for PoW type).
    /// Defaults to BitcoinConstants.VersionRollingPoolMask (0x1fffe000).
    /// </summary>
    public string VersionRollingMask { get; set; }

    /// <summary>
    /// Set to false to refuse version-rolling negotiation with miners.
    /// Use for coins that encode PoW type in nVersion bits covered by the rolling mask (e.g. LCC bit 16).
    /// Defaults to true (version rolling enabled).
    /// </summary>
    public bool EnableVersionRolling { get; set; } = true;

    /// <summary>
    /// Hex mask of nVersion bits that must be zero for a share to be submitted as a block candidate.
    /// If any of these bits are set in the submitted nVersion, the share is accepted for difficulty
    /// purposes but not submitted to the daemon.
    /// Use for coins that encode PoW type in nVersion (e.g. LCC uses bit 16: 00010000).
    /// </summary>
    public string VersionBlockedBits { get; set; }

    /// <summary>
    /// Optional list of auxiliary chains to merge-mine alongside this pool.
    /// Each entry specifies a chain daemon (e.g. Namecoin) whose blocks will
    /// be committed in this pool's coinbase and submitted when difficulty is met.
    /// </summary>
    public AuxPoW.AuxChainConfig[] AuxChains { get; set; }

    /// <summary>
    /// Optional RSK (Rootstock) merge mining configuration.
    /// RSK uses a different RPC protocol (mnr_getWork / mnr_submitBitcoinBlock)
    /// but shares the same AuxPoW Merkle tree in the coinbase.
    /// </summary>
    public RSK.RskChainConfig RskChain { get; set; }
}
