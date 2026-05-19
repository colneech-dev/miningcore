using System.Collections.Generic;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Configuration;
using Miningcore.Extensions;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Miningcore.Tests.Extensions;

public class SerializationExtensionsTests : TestBase
{
    class Foo
    {
        public int Bar { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    class Empty
    {
    }

    [Fact]
    public void SafeExtensionData_Empty()
    {
        var result = JsonConvert.DeserializeObject<Foo>("{ \"bar\": 1, \"foo\": {} }");

        var extra = result!.Extra.SafeExtensionDataAs<Empty>();
        Assert.NotNull(extra);
    }

    class Simple
    {
        public int Baz { get; set; }
    }

    [Fact]
    public void SafeExtensionData_Embedded()
    {
        var result = JsonConvert.DeserializeObject<Foo>("{ \"bar\": 1, \"baz\": 42 }");

        var extra = result!.Extra.SafeExtensionDataAs<Simple>();
        Assert.NotNull(extra);
        Assert.Equal(42, extra.Baz);
    }

    class Wrapped
    {
        public int Baz { get; set; }
    }

    [Fact]
    public void SafeExtensionData_Wrapped()
    {
        var result = JsonConvert.DeserializeObject<Foo>("{ \"bar\": 1, \"foo\": { \"baz\": 42 } }");

        var extra = result!.Extra.SafeExtensionDataAs<Wrapped>("foo");
        Assert.NotNull(extra);
        Assert.Equal(42, extra.Baz);
    }

    [Fact]
    public void SafeExtensionData_Double_Wrapped()
    {
        var result = JsonConvert.DeserializeObject<Foo>("{ \"bar\": 1, \"foo\": { \"qux\": { \"baz\": 42 } } }");

        var extra = result!.Extra.SafeExtensionDataAs<Wrapped>("foo", "qux");
        Assert.NotNull(extra);
        Assert.Equal(42, extra.Baz);
    }

    [Fact]
    public void SafeExtensionData_Triple_Wrapped()
    {
        var result = JsonConvert.DeserializeObject<Foo>("{ \"bar\": 1, \"foo\": { \"qux\": { \"thud\": { \"baz\": 42 } } } }");

        var extra = result!.Extra.SafeExtensionDataAs<Wrapped>("foo", "qux", "thud");
        Assert.NotNull(extra);
        Assert.Equal(42, extra.Baz);
    }

    // Simulates ReadConfig which uses CamelCasePropertyNamesContractResolver
    private static T DeserializeWithCamelCase<T>(string json)
    {
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };
        return JsonConvert.DeserializeObject<T>(json, settings);
    }

    [Fact]
    public void AuxChainConfig_ZmqSocket_Survives_SafeExtensionDataAs()
    {
        // Simulates a pool config JSON as ReadConfig would parse it
        var poolJson = @"{
            ""id"": ""dgb-solo"",
            ""coin"": ""dgb"",
            ""enabled"": true,
            ""ports"": {},
            ""daemons"": [],
            ""auxChains"": [
                {
                    ""id"": ""namecoin"",
                    ""name"": ""Namecoin"",
                    ""chainId"": 1,
                    ""zmqBlockNotifySocket"": ""tcp://namecoin:28332"",
                    ""daemons"": []
                }
            ]
        }";

        var poolConfig = DeserializeWithCamelCase<PoolConfig>(poolJson);
        Assert.NotNull(poolConfig.Extra);
        Assert.True(poolConfig.Extra.ContainsKey("auxChains"), "auxChains should be in Extra");

        var bitcoinExtra = poolConfig.Extra.SafeExtensionDataAs<BitcoinPoolConfigExtra>();
        Assert.NotNull(bitcoinExtra);
        Assert.NotNull(bitcoinExtra.AuxChains);
        Assert.Single(bitcoinExtra.AuxChains);

        var auxChain = bitcoinExtra.AuxChains[0];
        Assert.Equal("namecoin", auxChain.Id);
        // This is the key assertion - ZmqBlockNotifySocket must survive deserialization
        Assert.Equal("tcp://namecoin:28332", auxChain.ZmqBlockNotifySocket);
    }
}
