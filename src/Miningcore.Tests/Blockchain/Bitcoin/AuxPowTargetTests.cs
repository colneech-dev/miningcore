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

    // The resolution reported alongside the value drives RefreshAsync's logging: None and
    // Mismatch are the two cases that would otherwise fail silently and must be warned about.

    [Fact]
    public void Resolution_BigEndianTarget_IsBigEndian()
    {
        AuxPowManager.ResolveTarget("0000000000000009f9fa00000000000000000000000000000000000000000000", "1909f9fa", out var how);
        Assert.Equal(AuxPowManager.TargetResolution.BigEndian, how);
    }

    [Fact]
    public void Resolution_LittleEndianTarget_IsLittleEndian()
    {
        var leHex = Reverse("00000000ffff0000000000000000000000000000000000000000000000000000");
        AuxPowManager.ResolveTarget(leHex, "1d00ffff", out var how);
        Assert.Equal(AuxPowManager.TargetResolution.LittleEndian, how);
    }

    [Fact]
    public void Resolution_BitsOnly_IsFromBits()
    {
        AuxPowManager.ResolveTarget(null, "1a130141", out var how);
        Assert.Equal(AuxPowManager.TargetResolution.FromBits, how);
    }

    [Fact]
    public void Resolution_TargetOnly_IsUnchecked()
    {
        AuxPowManager.ResolveTarget("0000000000000000000000000000000000000000000000000000ffff00000000", null, out var how);
        Assert.Equal(AuxPowManager.TargetResolution.LittleEndianUnchecked, how);
    }

    [Fact]
    public void Resolution_Inconsistent_IsMismatch()
    {
        AuxPowManager.ResolveTarget("0000000000000009f9fa00000000000000000000000000000000000000000000", "1d00ffff", out var how);
        Assert.Equal(AuxPowManager.TargetResolution.Mismatch, how);
    }

    [Fact]
    public void Resolution_NothingReported_IsNone()
    {
        var value = AuxPowManager.ResolveTarget(null, null, out var how);
        Assert.Null(value);
        Assert.Equal(AuxPowManager.TargetResolution.None, how);
    }
}
