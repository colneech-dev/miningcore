using System.Diagnostics;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace Miningcore.Blockchain.Bitcoin;

public static class BitcoinUtils
{
    public static IDestination AddressToDestination(string address, Network expectedNetwork)
    {
        try
        {
            // For coins NBitcoin knows about this correctly handles P2PKH and P2SH.
            return BitcoinAddress.Create(address, expectedNetwork);
        }
        catch(FormatException)
        {
            // NBitcoin doesn't recognise this altcoin's address prefix.
            // Fall back to raw Base58Check decode. Only safe for P2PKH addresses —
            // all pool addresses must be P2PKH (legacy), never P2SH or segwit.
            var decoded = Encoders.Base58Check.DecodeData(address);
            var networkVersionBytes = expectedNetwork.GetVersionBytes(Base58Type.PUBKEY_ADDRESS, true);
            decoded = decoded.Skip(networkVersionBytes.Length).ToArray();
            return new KeyId(decoded);
        }
    }

    public static IDestination BechSegwitAddressToDestination(string address, Network expectedNetwork, string bechPrefix)
    {
        var encoder = Encoders.Bech32(bechPrefix);
        var decoded = encoder.Decode(address, out var witVersion);
        var result = new WitKeyId(decoded);

        Debug.Assert(result.GetAddress(expectedNetwork).ToString() == address);
        return result;
    }

    public static IDestination BCashAddressToDestination(string address, Network expectedNetwork)
    {
        var bcash = NBitcoin.Altcoins.BCash.Instance.GetNetwork(expectedNetwork.ChainName);
        var trashAddress = bcash.Parse<NBitcoin.Altcoins.BCash.BTrashPubKeyAddress>(address);
        return trashAddress.ScriptPubKey.GetDestinationAddress(bcash);
    }

    public static IDestination LitecoinAddressToDestination(string address, Network expectedNetwork)
    {
        var litecoin = NBitcoin.Altcoins.Litecoin.Instance.GetNetwork(expectedNetwork.ChainName);
        var encoder = litecoin.GetBech32Encoder(Bech32Type.WITNESS_PUBKEY_ADDRESS, true);

        var decoded = encoder.Decode(address, out var witVersion);
        var result = new WitKeyId(decoded);

        Debug.Assert(result.GetAddress(litecoin).ToString() == address);
        return result;
    }

    /// <summary>
    /// Maps a coin template's header hasher to the algorithm key a multi-algorithm daemon uses in
    /// getmininginfo.networkhashesps, and returns that algorithm's hashrate (null if not applicable).
    /// </summary>
    public static double? PickAlgoHashrate(Dictionary<string, double> perAlgo, string headerHasher)
    {
        if(perAlgo == null || perAlgo.Count == 0 || string.IsNullOrEmpty(headerHasher))
            return null;

        var key = headerHasher.ToLowerInvariant() switch
        {
            "sha256d" => "sha256d",
            "scrypt" => "scrypt",
            "skein" => "skein",
            "qubit" => "qubit",
            "odocrypt" => "odo",
            "groestl-myriad" or "groestl" => "groestl",
            _ => null
        };

        return key != null && perAlgo.TryGetValue(key, out var hps) && hps > 0 ? hps : null;
    }
}
