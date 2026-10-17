using System.Collections.Generic;
using Miningcore.Blockchain.Bitcoin;
using Xunit;

namespace Miningcore.Tests.Blockchain.Bitcoin;

public class AlgoHashrateTests
{
    // getmininginfo.networkhashesps from a live DigiByte node
    private static readonly Dictionary<string, double> DigiByte = new()
    {
        ["sha256d"] = 2.77416259606547e+16,
        ["scrypt"] = 11880453549171.54,
        ["skein"] = 974724270707714.4,
        ["qubit"] = 55428837042627.97,
        ["odo"] = 8884278417519.559,
    };

    [Theory]
    [InlineData("sha256d", 2.77416259606547e+16)]
    [InlineData("scrypt", 11880453549171.54)]
    [InlineData("odocrypt", 8884278417519.559)]      // template hasher "odocrypt" -> daemon key "odo"
    [InlineData("skein", 974724270707714.4)]
    [InlineData("qubit", 55428837042627.97)]
    [InlineData("SCRYPT", 11880453549171.54)]
    public void Picks_the_pools_own_algorithm(string headerHasher, double expected)
    {
        Assert.Equal(expected, BitcoinUtils.PickAlgoHashrate(DigiByte, headerHasher));
    }

    [Fact]
    public void Scrypt_and_odo_pools_no_longer_get_the_sha256_figure()
    {
        Assert.NotEqual(BitcoinUtils.PickAlgoHashrate(DigiByte, "sha256d"), BitcoinUtils.PickAlgoHashrate(DigiByte, "scrypt"));
        Assert.NotEqual(BitcoinUtils.PickAlgoHashrate(DigiByte, "sha256d"), BitcoinUtils.PickAlgoHashrate(DigiByte, "odocrypt"));
    }

    [Fact]
    public void Single_algorithm_daemons_are_left_alone()
    {
        Assert.Null(BitcoinUtils.PickAlgoHashrate(null, "scrypt"));                       // no networkhashesps field
        Assert.Null(BitcoinUtils.PickAlgoHashrate(new Dictionary<string, double>(), "scrypt"));
        Assert.Null(BitcoinUtils.PickAlgoHashrate(DigiByte, "x11"));                       // unmapped hasher
        Assert.Null(BitcoinUtils.PickAlgoHashrate(DigiByte, null));
        Assert.Null(BitcoinUtils.PickAlgoHashrate(new Dictionary<string, double> { ["scrypt"] = 0 }, "scrypt"));
    }
}
