using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.AuxPoW;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Miningcore.Tests.Blockchain.Bitcoin;

/// <summary>
/// The aux chains API shows each chain's network hashrate (read from the daemon's getnetworkhashps) and its
/// difficulty in the chain's own units. These cover the two small pieces of logic behind that.
/// </summary>
public class AuxNetworkHashrateTests
{
    [Fact]
    public void ParseHashrate_Number()
    {
        Assert.Equal((double?) 1.5e12, AuxPowManager.ParseHashrate(JToken.Parse("1500000000000")));
        Assert.Equal((double?) 220799454977.2223, AuxPowManager.ParseHashrate(JToken.Parse("220799454977.2223")));
    }

    [Fact]
    public void ParseHashrate_NumericString()
    {
        Assert.Equal((double?) 22921044358822.17, AuxPowManager.ParseHashrate(JToken.FromObject("22921044358822.17")));
    }

    [Theory]
    [InlineData("0")]          // a daemon with no estimate yet reports 0
    [InlineData("-1")]
    [InlineData("\"abc\"")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[1,2]")]
    public void ParseHashrate_RejectsNonsense(string json)
    {
        Assert.Null(AuxPowManager.ParseHashrate(JToken.Parse(json)));
    }

    [Fact]
    public void ParseHashrate_NullToken()
    {
        Assert.Null(AuxPowManager.ParseHashrate(null));
    }

    [Fact]
    public void Difficulty_UsesBitcoinDiff1ByDefault()
    {
        // bits 0x1d00ffff is difficulty 1 on Bitcoin's scale
        var target = new Target(0x1d00ffffU).ToUInt256();
        var diff = DifficultyUnits.TargetDifficulty(target, DifficultyUnits.ParseDiff1(null));
        Assert.NotNull(diff);
        Assert.InRange(diff.Value, 0.999999, 1.000001);
    }

    [Fact]
    public void Difficulty_HonoursAConfiguredDiff1()
    {
        // The same target measured against a diff1 sixteen times easier reads sixteen times harder.
        var target = new Target(0x1d00ffffU).ToUInt256();
        var easier = DifficultyUnits.ParseDiff1("0000000ffff0" + new string((char) 48, 52));   // Bitcoin diff1 (00000000ffff + 52 zeros) times 16
        var viaBitcoin = DifficultyUnits.TargetDifficulty(target, DifficultyUnits.ParseDiff1(null));
        var viaCustom = DifficultyUnits.TargetDifficulty(target, easier);

        Assert.NotNull(viaBitcoin);
        Assert.NotNull(viaCustom);
        Assert.True(viaCustom.Value > viaBitcoin.Value * 15.9 && viaCustom.Value < viaBitcoin.Value * 16.1,
            $"expected about 16x, got {viaCustom.Value / viaBitcoin.Value}");
    }

    [Fact]
    public void Difficulty_NullTarget_IsNull()
    {
        Assert.Null(DifficultyUnits.TargetDifficulty(null, DifficultyUnits.ParseDiff1(null)));
    }
}
