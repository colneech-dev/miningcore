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
                new AuxChainConfig { Id = "namecoin", Name = "Namecoin", Daemons = MinimalDaemons() },
                new AuxChainConfig { Id = "elastos",  Name = "Elastos",  Daemons = MinimalDaemons() }
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
            .WithErrorMessage("AuxChain config error: duplicate auxChain id 'namecoin'");
    }
}
