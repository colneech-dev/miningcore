using System;
using System.Collections.Generic;
using FluentValidation.TestHelper;
using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Miningcore.Tests.Configuration;

public class PoolConfigValidationTests
{
    private readonly PoolConfigValidator validator = new();

    private static readonly JsonSerializer CamelCaseSerializer = new()
    {
        ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() }
    };

    /// <summary>
    /// Converts a BitcoinPoolConfigExtra into the IDictionary format that PoolConfig.Extra
    /// holds at runtime (after JSON deserialization), so SafeExtensionDataAs can round-trip it.
    /// </summary>
    private static IDictionary<string, object> ExtraFrom(BitcoinPoolConfigExtra config) =>
        JObject.FromObject(config, CamelCaseSerializer).ToObject<Dictionary<string, object>>();

    private static PoolConfig BasePoolConfig() => new()
    {
        Id = "fractal-solo",
        Coin = "fractal",
        Address = "bc1qtest",
        Daemons = new[] { new DaemonEndpointConfig { Host = "localhost", Port = 8332 } }
    };

    [Fact]
    public void PoolConfig_ShouldFailWhenCoinIsItsOwnAuxChain()
    {
        var pool = BasePoolConfig();
        pool.Extra = ExtraFrom(new BitcoinPoolConfigExtra
        {
            AuxChains = new[]
            {
                new AuxChainConfig { Id = "fractal", Name = "Fractal Bitcoin" },
                new AuxChainConfig { Id = "namecoin", Name = "Namecoin" }
            }
        });

        var result = validator.TestValidate(pool);
        result.ShouldHaveValidationErrorFor(x => x.Extra)
            .WithErrorMessage("Pool cannot have itself (fractal) as an auxiliary coin");
    }

    private static DaemonEndpointConfig[] MinimalDaemons() =>
        new[] { new DaemonEndpointConfig { Host = "localhost", Port = 8332 } };

    [Fact]
    public void PoolConfig_ShouldPassWithOnlyOtherAuxCoins()
    {
        var pool = BasePoolConfig();
        pool.Extra = ExtraFrom(new BitcoinPoolConfigExtra
        {
            AuxChains = new[]
            {
                // distinct chainIds: two chains on one aux-merkle slot are now a validation error
                new AuxChainConfig { Id = "namecoin", Name = "Namecoin", ChainId = 1, Daemons = MinimalDaemons() },
                new AuxChainConfig { Id = "elastos",  Name = "Elastos",  ChainId = 6, Daemons = MinimalDaemons() }
            }
        });

        var result = validator.TestValidate(pool);
        result.ShouldNotHaveValidationErrorFor(x => x.Extra);
    }

    [Fact]
    public void PoolConfig_ShouldPassWithEmptyAuxChains()
    {
        var pool = BasePoolConfig();
        pool.Extra = ExtraFrom(new BitcoinPoolConfigExtra
        {
            AuxChains = Array.Empty<AuxChainConfig>()
        });

        var result = validator.TestValidate(pool);
        result.ShouldNotHaveValidationErrorFor(x => x.Extra);
    }

    [Fact]
    public void PoolConfig_ShouldPassWithNullAuxChains()
    {
        var pool = BasePoolConfig();
        pool.Extra = ExtraFrom(new BitcoinPoolConfigExtra { AuxChains = null });

        var result = validator.TestValidate(pool);
        result.ShouldNotHaveValidationErrorFor(x => x.Extra);
    }

    [Fact]
    public void PoolConfig_ShouldPassWithNullExtra()
    {
        var pool = BasePoolConfig();
        pool.Extra = null;

        var result = validator.TestValidate(pool);
        result.ShouldNotHaveValidationErrorFor(x => x.Extra);
    }

    [Fact]
    public void PoolConfig_ShouldFailWithDuplicateAuxChainId()
    {
        var pool = BasePoolConfig();
        pool.Extra = ExtraFrom(new BitcoinPoolConfigExtra
        {
            AuxChains = new[]
            {
                new AuxChainConfig { Id = "namecoin", Name = "Namecoin", Daemons = MinimalDaemons() },
                new AuxChainConfig { Id = "namecoin", Name = "Namecoin (duplicate)", Daemons = MinimalDaemons() }
            }
        });

        var result = validator.TestValidate(pool);
        result.ShouldHaveValidationErrorFor(x => x.Extra)
            .WithErrorMessage("Merged mining config error: duplicate auxChain id 'namecoin'");
    }

    [Fact]
    public void PoolConfig_ShouldFailWhenTwoAuxChainsShareAChainId()
    {
        var pool = BasePoolConfig();
        pool.Extra = ExtraFrom(new BitcoinPoolConfigExtra
        {
            AuxChains = new[]
            {
                new AuxChainConfig { Id = "namecoin", ChainId = 1, Daemons = MinimalDaemons() },
                new AuxChainConfig { Id = "other", ChainId = 1, Daemons = MinimalDaemons() }
            }
        });

        var result = validator.TestValidate(pool);
        result.ShouldHaveValidationErrorFor(x => x.Extra)
            .WithErrorMessage("Merged mining config error: auxChain 'other' and 'namecoin' share chainId 1");
    }

    [Fact]
    public void PoolConfig_ShouldFailWhenRskChainHasNoDaemons()
    {
        var pool = BasePoolConfig();
        pool.Extra = ExtraFrom(new BitcoinPoolConfigExtra
        {
            RskChain = new Miningcore.Blockchain.Bitcoin.RSK.RskChainConfig { Id = "rsk" }
        });

        var result = validator.TestValidate(pool);
        result.ShouldHaveValidationErrorFor(x => x.Extra)
            .WithErrorMessage("Merged mining config error: rskChain 'rsk' is missing daemons");
    }

    [Fact]
    public void PoolConfig_ShouldFailWhenAuxDecimalsAreOutOfRange()
    {
        var pool = BasePoolConfig();
        pool.Extra = ExtraFrom(new BitcoinPoolConfigExtra
        {
            AuxChains = new[] { new AuxChainConfig { Id = "namecoin", ChainId = 1, Daemons = MinimalDaemons(), Decimals = 40 } }
        });

        var result = validator.TestValidate(pool);
        result.ShouldHaveValidationErrorFor(x => x.Extra)
            .WithErrorMessage("Merged mining config error: auxChain 'namecoin' has an invalid 'decimals' value 40");
    }

    [Fact]
    public void DaemonConfig_ShouldRejectPortsAbove65535()
    {
        var pool = BasePoolConfig();
        pool.Daemons = new[] { new DaemonEndpointConfig { Host = "localhost", Port = 70000 } };

        var result = validator.TestValidate(pool);
        result.ShouldHaveValidationErrorFor("Daemons[0].Port");
    }
}
