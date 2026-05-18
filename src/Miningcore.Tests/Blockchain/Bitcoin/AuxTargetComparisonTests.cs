using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Miningcore.Extensions;
using NBitcoin;
using Xunit;

namespace Miningcore.Tests.Blockchain.Bitcoin;

/// <summary>
/// Guards against byte-order bugs in the aux chain target comparison.
///
/// In ProcessShareInternal the comparison is:
///   var headerValue = new uint256(headerHash);   // Span<byte>, LE internal format
///   if (headerValue &lt;= aux.TargetValue) ...
///
/// And in AuxPowManager.RefreshAsync:
///   block.TargetValue = new uint256(block.Target.HexToByteArray());  // LE bytes from _target
///
/// Both operands must use the same LE convention. If one is accidentally interpreted
/// as BE the comparison becomes meaningless and every share appears to meet the target,
/// resulting in submissions that the aux daemon then rejects with a plain `false`.
///
/// SpaceXpanse bits = 1905be90 → target = 0x0005be90 × 2^176
/// In LE internal bytes:  [0]*22 + [0x90, 0xbe, 0x05] + [0]*7
/// Display (big-endian):  [0]*7 + [0x05, 0xbe, 0x90] + [0]*22
/// </summary>
public class AuxTargetComparisonTests
{
    // SpaceXpanse target for bits=1905be90 in LE internal format.
    // 0x0005be90 * 2^176 → byte[22]=0x90, byte[23]=0xbe, byte[24]=0x05, rest=0x00
    private static readonly byte[] SpaceXpanseTargetLeBytes = MakeLeBytes((22, 0x90), (23, 0xbe), (24, 0x05));

    // Hash with display byte[7]=0x06 → LE byte[24]=0x06 (above target, must be rejected)
    private static readonly byte[] HashAboveTargetLeBytes = MakeLeBytes((24, 0x06));

    // Hash with display byte[7]=0x03 → LE byte[24]=0x03 (below target, must be accepted)
    private static readonly byte[] HashBelowTargetLeBytes = MakeLeBytes((24, 0x03));

    private static byte[] MakeLeBytes(params (int index, byte value)[] entries)
    {
        var b = new byte[32];
        foreach(var (i, v) in entries)
            b[i] = v;
        return b;
    }

    [Fact]
    public void TargetValue_FromLeBytes_MatchesTargetFromBits()
    {
        // Validates that the LE bytes I derive from bits=1905be90 are correct.
        // If this test fails, the other tests in this class carry wrong assumptions.
        var target = new Target("1905be90".HexToByteArray());
        var fromBits = target.ToUInt256();
        var fromLeBytes = new uint256(SpaceXpanseTargetLeBytes);

        Assert.Equal(fromBits, fromLeBytes);
    }

    [Fact]
    public void HashAboveAuxTarget_IsNotAuxCandidate()
    {
        // hash display byte[7]=0x06 > target display byte[7]=0x05 → must NOT be accepted.
        // Bug pattern: if headerHash (LE) is mistakenly treated as BE, leading zeros become LSB
        // positions, making even a "hard" hash appear numerically tiny and always pass.
        var aux = new AuxBlockData
        {
            Hash = new string('a', 64),
            ChainId = 1899,
            TargetValue = new uint256(SpaceXpanseTargetLeBytes)
        };

        var headerValue = new uint256(HashAboveTargetLeBytes);

        Assert.False(headerValue <= aux.TargetValue,
            "Hash above SpaceXpanse target must not be an aux candidate");
    }

    [Fact]
    public void HashBelowAuxTarget_IsAuxCandidate()
    {
        // hash display byte[7]=0x03 < target display byte[7]=0x05 → must be accepted.
        var aux = new AuxBlockData
        {
            Hash = new string('a', 64),
            ChainId = 1899,
            TargetValue = new uint256(SpaceXpanseTargetLeBytes)
        };

        var headerValue = new uint256(HashBelowTargetLeBytes);

        Assert.True(headerValue <= aux.TargetValue,
            "Hash below SpaceXpanse target should be an aux candidate");
    }

    [Fact]
    public void HashEqualToTarget_IsAuxCandidate()
    {
        // Boundary condition: hash exactly equal to target must pass (<=).
        var aux = new AuxBlockData
        {
            Hash = new string('a', 64),
            ChainId = 1899,
            TargetValue = new uint256(SpaceXpanseTargetLeBytes)
        };

        var headerValue = new uint256(SpaceXpanseTargetLeBytes);

        Assert.True(headerValue <= aux.TargetValue,
            "Hash equal to target must be accepted");
    }

    [Fact]
    public void AuxTargetValue_NullTargetValue_NeverAcceptsCandidate()
    {
        // Guard: if TargetValue was never set (e.g. daemon returned neither target nor bits),
        // no share should be submitted.
        var aux = new AuxBlockData
        {
            Hash = new string('a', 64),
            ChainId = 1899,
            TargetValue = null
        };

        var headerValue = new uint256(HashBelowTargetLeBytes);

        // Mirror the exact null-guard from BitcoinJob.ProcessShareInternal
        var isCandidate = aux.TargetValue != null && headerValue <= aux.TargetValue;

        Assert.False(isCandidate, "Null TargetValue must never produce an aux candidate");
    }

    [Fact]
    public void HashAboveTarget_OrderingIsConsistentWithBitsCalculation()
    {
        // End-to-end: build TargetValue from bits (the fallback path in AuxPowManager),
        // then verify a hash that exceeds that target is correctly rejected.
        var target = new Target("1905be90".HexToByteArray());
        var aux = new AuxBlockData
        {
            Hash = new string('a', 64),
            ChainId = 1899,
            TargetValue = target.ToUInt256()
        };

        var headerValue = new uint256(HashAboveTargetLeBytes);

        Assert.False(headerValue <= aux.TargetValue,
            "Hash above target must be rejected regardless of whether TargetValue came from _target bytes or bits");
    }

    [Fact]
    public void HashBelowTarget_OrderingIsConsistentWithBitsCalculation()
    {
        var target = new Target("1905be90".HexToByteArray());
        var aux = new AuxBlockData
        {
            Hash = new string('a', 64),
            ChainId = 1899,
            TargetValue = target.ToUInt256()
        };

        var headerValue = new uint256(HashBelowTargetLeBytes);

        Assert.True(headerValue <= aux.TargetValue,
            "Hash below target must be accepted regardless of TargetValue source");
    }
}
