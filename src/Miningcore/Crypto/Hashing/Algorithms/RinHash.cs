using System;
using System.Security.Cryptography;
using Isopoh.Cryptography.Argon2;
using Miningcore.Contracts;
using Org.BouncyCastle.Crypto.Digests;
using System.Text;
using Blake3;

namespace Miningcore.Crypto.Hashing.Algorithms;

/// <summary>
/// RinHash: BLAKE3  Argon2d  SHA3-256
/// </summary>
[Identifier("rinhash")]
public unsafe class RinHash : IHashAlgorithm
{
    private static readonly byte[] Salt;

    static RinHash()
    {
        Salt = Encoding.UTF8.GetBytes("RinCoinSalt");
    }

    public unsafe void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        // 1. BLAKE3
        var hash = Blake3.Hasher.Hash(data);
        var blake3 = hash.AsSpanUnsafe().ToArray();

        // 2. Argon2d
        var config = new Argon2Config
        {
            Type = Argon2Type.DataDependentAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = 2,
            MemoryCost = 64, // 64 MB
            Lanes = 1,
            Threads = 1,
            Password = blake3,
            Salt = Salt,
            HashLength = 32
        };

        var argon2 = new Argon2(config);
        var arresult = argon2.Hash();

        // SHA3-256 via BouncyCastle (already a dependency). This replaced the SHA3.Net package, whose
        // only purpose was this call and which dragged in BouncyCastle.NetCore 1.8.2 and the
        // NETStandard.Library 1.6 chain of System.* packages with open advisories.
        var sha3Digest = new Sha3Digest(256);
        sha3Digest.BlockUpdate(arresult.Buffer, 0, arresult.Buffer.Length);
        var sha3 = new byte[sha3Digest.GetDigestSize()];
        sha3Digest.DoFinal(sha3, 0);

        sha3.CopyTo(result);
    }
}
