namespace Miningcore.Blockchain.Bitcoin.AuxPoW;

/// <summary>
/// Data returned by the aux chain daemon's getauxblock RPC call.
/// Contains the current aux chain block template for merge mining.
/// </summary>
public class AuxBlockData
{
    /// <summary>Hash of the aux chain block we are working on (hex)</summary>
    public string Hash { get; set; }

    /// <summary>Chain ID of the aux chain</summary>
    public int ChainId { get; set; }

    /// <summary>Previous block hash (hex)</summary>
    public string PreviousBlockHash { get; set; }

    /// <summary>Coinbase value (in satoshis)</summary>
    public long CoinbaseValue { get; set; }

    /// <summary>Current target bits (compact format, hex)</summary>
    public string Bits { get; set; }

    /// <summary>Current target as full 256-bit hex</summary>
    public string Target { get; set; }

    /// <summary>Block height</summary>
    public int Height { get; set; }

    // Runtime fields (not from RPC)

    /// <summary>Target as uint256 for difficulty comparison</summary>
    public NBitcoin.uint256 TargetValue { get; set; }

    /// <summary>When this aux block was fetched</summary>
    public DateTimeOffset FetchedAt { get; set; }
}
