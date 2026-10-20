using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Xunit;

namespace Miningcore.Tests.Blockchain.Bitcoin;

/// <summary>
/// Which parameters the aux work request carries. ElevenSeventyFive's getauxblock requires the payout address as its
/// first argument and has no createauxblock; Namecoin-style daemons must keep getting no parameters.
/// </summary>
public class AuxWorkRequestArgsTests
{
    private const string Address = "MvUhqnXyqrDEVH8MMjjKXMA8tuNZyVd9Ts";

    private static AuxChainConfig Config(string method, bool passAddress = false, string address = Address) => new()
    {
        GetAuxBlockMethod = method,
        PassAddressToGetAuxBlock = passAddress,
        Address = address
    };

    [Fact]
    public void CreateAuxBlock_SendsAddress()
    {
        var args = Assert.IsType<object[]>(AuxPowManager.BuildWorkRequestArgs(Config("createauxblock")));
        Assert.Equal(new object[] { Address }, args);
    }

    [Fact]
    public void CreateAuxBlock_WithoutAddress_SendsNothing()
    {
        Assert.Null(AuxPowManager.BuildWorkRequestArgs(Config("createauxblock", address: null)));
    }

    [Fact]
    public void GetAuxBlock_DefaultsToNoParameters()
    {
        // Namecoin-style getauxblock(hash, auxpow): a lone address would be read as a block hash
        Assert.Null(AuxPowManager.BuildWorkRequestArgs(Config("getauxblock")));
    }

    [Fact]
    public void GetAuxBlock_WithFlag_SendsAddress()
    {
        var args = Assert.IsType<object[]>(AuxPowManager.BuildWorkRequestArgs(Config("getauxblock", passAddress: true)));
        Assert.Equal(new object[] { Address }, args);
    }

    [Fact]
    public void GetAuxBlock_WithFlagButNoAddress_SendsNothing()
    {
        Assert.Null(AuxPowManager.BuildWorkRequestArgs(Config("getauxblock", passAddress: true, address: "")));
    }

    [Fact]
    public void Flag_DoesNotAffectOtherMethods()
    {
        // createauxblock is unchanged by the flag, getblocktemplate keeps its rules object
        Assert.Equal(new object[] { Address }, (object[]) AuxPowManager.BuildWorkRequestArgs(Config("createauxblock", passAddress: true)));

        var gbt = Assert.IsType<object[]>(AuxPowManager.BuildWorkRequestArgs(Config("getblocktemplate", passAddress: true)));
        Assert.Single(gbt);
        Assert.NotEqual(Address, gbt[0]);
    }

    [Fact]
    public void Flag_DefaultsToOff()
    {
        Assert.False(new AuxChainConfig().PassAddressToGetAuxBlock);
    }
}
