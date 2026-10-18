using System.Globalization;
using System.Numerics;
using Miningcore.Extensions;
using Miningcore.Util;
using NBitcoin;

namespace Miningcore.Blockchain.Bitcoin;

/// <summary>
/// Difficulty unit helpers.
///
/// Every difficulty the pool stores is in "daemon units": the chain's difficulty-1 target divided
/// by a hash or a target, which is the number the daemon's own getdifficulty reports. The stratum
/// share multiplier (65536 for scrypt, 256 or 4096 for groestl coins, ...) is a display convention
/// for miners and never reaches the database; shares.difficulty, blocks.difficulty,
/// auxblocks.difficulty and every networkdifficulty column share this unit, so effort and luck are
/// plain ratios. Merged mining uses the same SHA256d parent hash against each aux chain's target,
/// so a chain's hash difficulty is the parent's rescaled by the ratio of the two difficulty-1
/// targets (identical for every Bitcoin-derived chain). See docs/decisions.md, 2026-10-18.
/// </summary>
public static class DifficultyUnits
{
    /// <summary>Parses a difficulty-1 target from hex; null or empty means Bitcoin's.</summary>
    public static BigInteger ParseDiff1(string hex)
    {
        if(string.IsNullOrWhiteSpace(hex))
            return BitcoinConstants.Diff1;

        // a leading zero keeps BigInteger from reading a high first nibble as a sign bit
        return BigInteger.Parse("0" + hex.Trim(), NumberStyles.HexNumber);
    }

    /// <summary>
    /// Difficulty of a hash: diff1 / hash. <paramref name="hash"/> is in internal (little-endian)
    /// byte order, exactly as the header hasher produces it. Zero when the hash is zero.
    /// </summary>
    public static double HashDifficulty(Span<byte> hash, BigInteger diff1)
    {
        var value = hash.ToBigInteger();
        return value.IsZero ? 0 : (double) new BigRational(diff1, value);
    }

    /// <summary>Difficulty of a target: diff1 / target. Null when there is no usable target.</summary>
    public static double? TargetDifficulty(uint256 target, BigInteger diff1)
    {
        if(target == null)
            return null;

        var value = new BigInteger(target.ToBytes(), isUnsigned: true);
        return value.IsZero ? null : (double) new BigRational(diff1, value);
    }

    /// <summary>Expresses a difficulty measured against one difficulty-1 target against another.</summary>
    public static double Rescale(double difficulty, BigInteger fromDiff1, BigInteger toDiff1)
    {
        if(fromDiff1 == toDiff1)
            return difficulty;

        return difficulty * (double) new BigRational(toDiff1, fromDiff1);
    }
}
