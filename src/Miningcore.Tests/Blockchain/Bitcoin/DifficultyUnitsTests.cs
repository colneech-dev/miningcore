using System;
using System.Linq;
using System.Numerics;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Extensions;
using NBitcoin;
using Xunit;

namespace Miningcore.Tests.Blockchain.Bitcoin;

/// <summary>
/// Pins the unit convention: stored difficulties are diff1 / value with no stratum share multiplier.
/// </summary>
public class DifficultyUnitsTests
{
    // Bitcoin's difficulty-1 target as a 32-byte hash in display (big-endian) order
    private static byte[] Diff1Hash() => ("00000000ffff0000" + new string('0', 48)).HexToByteArray();

    // HashDifficulty takes the hash in internal (little-endian) order, as the hashers produce it
    private static byte[] Internal(byte[] display) => display.Reverse().ToArray();

    [Fact]
    public void HashDifficulty_OfTheDiff1Target_IsOne()
    {
        Assert.Equal(1.0, DifficultyUnits.HashDifficulty(Internal(Diff1Hash()), BitcoinConstants.Diff1), 12);
    }

    [Fact]
    public void HashDifficulty_IgnoresTheStratumMultiplier()
    {
        // a hash 65536x below the diff1 target has difficulty 65536 on a scrypt chain too, not 65536^2
        var hash = ("000000000000ffff" + new string('0', 48)).HexToByteArray();
        Assert.Equal(65536.0, DifficultyUnits.HashDifficulty(Internal(hash), BitcoinConstants.Diff1), 6);
    }

    [Fact]
    public void HashDifficulty_OfZero_IsZero()
    {
        Assert.Equal(0.0, DifficultyUnits.HashDifficulty(new byte[32], BitcoinConstants.Diff1));
    }

    [Fact]
    public void TargetDifficulty_OfTheDiff1Target_IsOne()
    {
        var target = new uint256(Diff1Hash(), false);
        Assert.Equal(1.0, DifficultyUnits.TargetDifficulty(target, BitcoinConstants.Diff1)!.Value, 12);
    }

    [Fact]
    public void TargetDifficulty_NullOrZero_IsNull()
    {
        Assert.Null(DifficultyUnits.TargetDifficulty(null, BitcoinConstants.Diff1));
        Assert.Null(DifficultyUnits.TargetDifficulty(uint256.Zero, BitcoinConstants.Diff1));
    }

    [Fact]
    public void Rescale_SameDiff1_IsIdentity()
    {
        Assert.Equal(1234.5, DifficultyUnits.Rescale(1234.5, BitcoinConstants.Diff1, BitcoinConstants.Diff1));
    }

    [Fact]
    public void Rescale_ToAChainWithALargerDiff1_ScalesUp()
    {
        var bigger = BitcoinConstants.Diff1 * 65536;
        Assert.Equal(2.0 * 65536, DifficultyUnits.Rescale(2.0, BitcoinConstants.Diff1, bigger), 6);
    }

    [Fact]
    public void ParseDiff1_DefaultsToBitcoin_AndReadsHighNibbleUnsigned()
    {
        Assert.Equal(BitcoinConstants.Diff1, DifficultyUnits.ParseDiff1(null));
        Assert.Equal(BitcoinConstants.Diff1, DifficultyUnits.ParseDiff1(""));
        Assert.Equal(BitcoinConstants.Diff1, DifficultyUnits.ParseDiff1("00ffff0000000000000000000000000000000000000000000000000000"));
        Assert.True(DifficultyUnits.ParseDiff1("ff" + new string('0', 62)).Sign > 0);
    }
}
