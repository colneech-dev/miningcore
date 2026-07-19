using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("odocrypt")]
public unsafe class Odocrypt : IHashAlgorithm
{
    // OdoCrypt "shapechange" interval in seconds. This is a per-network
    // consensus parameter (DigiByte chainparams.cpp nOdoShapechangeInterval):
    //   mainnet  864000 (10 days)
    //   testnet   86400 (1 day)
    //   regtest      60 (1 minute)
    // Override for non-mainnet pools via the MININGCORE_ODO_INTERVAL env var.
    private static readonly uint EpochLength =
        uint.TryParse(Environment.GetEnvironmentVariable("MININGCORE_ODO_INTERVAL"),
                      out var v) && v > 0
            ? v
            : 10u * 24 * 60 * 60;

    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.RequiresNonNull(extra);
        Contract.Requires<ArgumentException>(extra.Length > 0);
        Contract.Requires<ArgumentException>(result.Length >= 32);
        Contract.Requires<ArgumentException>(data.Length <= 80);

        var nTime = (ulong) extra[0];

        // DigiByte consensus OdoKey(): nTime rounded DOWN to the interval
        // boundary, NOT nTime / interval. The previous `nTime / EpochLength`
        // produced a tiny quotient (~2024) instead of the floored timestamp
        // (~1.7e9), so every share hashed with the wrong S-box tables and was
        // rejected. Ref: digibyte/src/primitives/block.cpp OdoKey().
        var key = (uint) (nTime - nTime % EpochLength);

        fixed(byte* input = data)
        fixed(byte* output = result)
        {
            Multihash.odocrypt(input, output, (uint) data.Length, key);
        }
    }
}
