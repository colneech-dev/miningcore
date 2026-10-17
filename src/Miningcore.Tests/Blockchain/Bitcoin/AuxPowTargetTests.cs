using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Miningcore.Extensions;
using NBitcoin;
using Xunit;

namespace Miningcore.Tests.Blockchain.Bitcoin;

/// <summary>
/// AuxPowManager.ResolveTarget must read a daemon's reported target in the right byte order.
/// Vectors for S256 and Qbit are real createauxblock responses captured 2026-10-17.
/// </summary>
public class AuxPowTargetTests
{
    private static uint256 FromBits(string bits) => new NBitcoin.Target(bits.HexToByteArray()).ToUInt256();

    private static string Reverse(string hex)
    {
        var b = hex.HexToByteArray();
        System.Array.Reverse(b);
        return b.ToHexString();
    }

    [Fact]
    public void S256_BigEndianTarget_IsReadCorrectly()
    {
        const string bits = "1909f9fa";
        const string target = "0000000000000009f9fa00000000000000000000000000000000000000000000";

        var naiveLittleEndian = new uint256(target.HexToByteArray());
        var resolved = AuxPowManager.ResolveTarget(target, bits);

        Assert.Equal(FromBits(bits), resolved);
        // the old behaviour produced a target ~2^136 times too hard: no share would ever qualify
        Assert.NotEqual(naiveLittleEndian, resolved);
    }

    [Fact]
    public void Qbit_BigEndianTarget_IsReadCorrectly()
    {
        const string bits = "1709ac45";
        const string target = "00000000000000000009ac450000000000000000000000000000000000000000";

        var resolved = AuxPowManager.ResolveTarget(target, bits);

        Assert.Equal(FromBits(bits), resolved);
        Assert.NotEqual(new uint256(target.HexToByteArray()), resolved);
    }

    [Fact]
    public void NamecoinStyle_LittleEndianTarget_IsUnchanged()
    {
        // 1d00ffff expands to 00000000ffff0000...; Namecoin-family daemons report it byte-reversed
        const string bits = "1d00ffff";
        const string beHex = "00000000ffff0000000000000000000000000000000000000000000000000000";
        var leHex = Reverse(beHex);

        var resolved = AuxPowManager.ResolveTarget(leHex, bits);

        Assert.Equal(FromBits(bits), resolved);
        Assert.Equal(new uint256(leHex.HexToByteArray()), resolved);   // identical to the historical reading
    }

    [Fact]
    public void BitsOnly_UsesBits()
    {
        Assert.Equal(FromBits("1a130141"), AuxPowManager.ResolveTarget(null, "1a130141"));
        Assert.Equal(FromBits("1a130141"), AuxPowManager.ResolveTarget("", "1a130141"));
    }

    [Fact]
    public void TargetOnly_KeepsLittleEndianReading()
    {
        const string leHex = "0000000000000000000000000000000000000000000000000000ffff00000000";
        Assert.Equal(new uint256(leHex.HexToByteArray()), AuxPowManager.ResolveTarget(leHex, null));
    }

    [Fact]
    public void InconsistentTargetAndBits_FallsBackToHistoricalReading()
    {
        // neither byte order reproduces these bits: behave exactly as before this change
        const string target = "0000000000000009f9fa00000000000000000000000000000000000000000000";
        Assert.Equal(new uint256(target.HexToByteArray()), AuxPowManager.ResolveTarget(target, "1d00ffff"));
    }

    [Fact]
    public void NothingReported_ReturnsNull()
    {
        Assert.Null(AuxPowManager.ResolveTarget(null, null));
    }
}
