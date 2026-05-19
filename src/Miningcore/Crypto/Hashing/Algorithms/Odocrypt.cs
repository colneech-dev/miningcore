using Miningcore.Contracts;
using Miningcore.Native;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("odocrypt")]
public unsafe class Odocrypt : IHashAlgorithm
{
    private const uint EpochLength = 10 * 24 * 60 * 60; // 864000 seconds

    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.RequiresNonNull(extra);
        Contract.Requires<ArgumentException>(extra.Length > 0);
        Contract.Requires<ArgumentException>(result.Length >= 32);
        Contract.Requires<ArgumentException>(data.Length <= 80);

        var nTime = (ulong) extra[0];
        var key = (uint) (nTime / EpochLength);

        fixed(byte* input = data)
        fixed(byte* output = result)
        {
            Multihash.odocrypt(input, output, (uint) data.Length, key);
        }
    }
}
