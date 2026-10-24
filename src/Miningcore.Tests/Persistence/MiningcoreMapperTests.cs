using System;
using System.Collections.Generic;
using Miningcore.Configuration;
using Miningcore.Mapping;
using Miningcore.Persistence.Model;
using Newtonsoft.Json.Linq;
using Xunit;

using EntityBlock = Miningcore.Persistence.Postgres.Entities.Block;

namespace Miningcore.Tests.Persistence;

/// <summary>
/// Behaviour the Mapperly mapper must preserve from the AutoMapper profile it replaced:
/// lowercase block status in rows and responses, case-insensitive parsing back, the
/// share-to-block difficulty rule, null pass-through, and API copies that do not alias
/// the live pool configuration (the API layer strips wallet secrets from them).
/// </summary>
public class MiningcoreMapperTests
{
    private readonly MiningcoreMapper mapper = new();

    [Fact]
    public void BlockStatus_IsPersistedAndServedLowercase()
    {
        var block = new Block { PoolId = "p", Status = BlockStatus.Confirmed, Created = DateTime.UtcNow };

        Assert.Equal("confirmed", mapper.ToEntity(block).Status);
        Assert.Equal("confirmed", mapper.ToResponse(block).Status);
    }

    [Theory]
    [InlineData("pending", BlockStatus.Pending)]
    [InlineData("Orphaned", BlockStatus.Orphaned)]
    [InlineData("CONFIRMED", BlockStatus.Confirmed)]
    public void BlockStatus_ParsesFromRowIgnoringCase(string stored, BlockStatus expected)
    {
        var entity = new EntityBlock { PoolId = "p", Status = stored, Created = DateTime.UtcNow };

        Assert.Equal(expected, mapper.ToModel(entity).Status);
    }

    [Fact]
    public void ShareToBlock_UsesHashDifficultyOnlyWhenPositive()
    {
        var share = new Miningcore.Blockchain.Share
        {
            PoolId = "p", Miner = "m", Worker = "w", Source = "s",
            BlockHeight = 123, BlockReward = 12.5m, BlockHash = "abc", BlockType = "block",
            Difficulty = 64, HashDifficulty = 0, NetworkDifficulty = 1000, Created = DateTime.UtcNow,
        };

        var block = mapper.ToBlock(share);

        Assert.Equal(123UL, block.BlockHeight);
        Assert.Equal(12.5m, block.Reward);
        Assert.Equal("abc", block.Hash);
        Assert.Equal("block", block.Type);
        Assert.Null(block.Difficulty);                 // share difficulty must not leak into the block
        Assert.Equal(default, block.Status);           // left for the caller (ShareRecorder sets Pending)

        share.HashDifficulty = 777;
        Assert.Equal(777, mapper.ToBlock(share).Difficulty);
    }

    [Fact]
    public void NullSources_MapToNull()
    {
        Assert.Null(mapper.ToResponse((Block) null));
        Assert.Null(mapper.ToModel((EntityBlock) null));
        Assert.Null(mapper.ToMiningStats((PoolStats) null));
        Assert.Null(mapper.ToBlockchainStats((PoolStats) null));
        Assert.Null(mapper.ToBlock(null));
        Assert.Null(mapper.ToPoolInfo(null));
    }

    [Fact]
    public void PersistedStats_RoundTripToLiveStats()
    {
        var row = new PoolStats
        {
            PoolId = "p", ConnectedMiners = 3, ConnectedWorkers = 5, PoolHashrate = 1e9, SharesPerSecond = 2.5,
            NetworkHashrate = 1e12, NetworkDifficulty = 4e6, BlockHeight = 20_144_221, ConnectedPeers = 8,
            LastNetworkBlockTime = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc), Created = DateTime.UtcNow,
        };

        var live = mapper.ToMiningStats(row);
        var net = mapper.ToBlockchainStats(row);

        Assert.Equal(3, live.ConnectedMiners);
        Assert.Equal(5, live.ConnectedWorkers);
        Assert.Equal(1e9, live.PoolHashrate);
        Assert.Equal(20_144_221UL, net.BlockHeight);
        Assert.Equal(8, net.ConnectedPeers);
        Assert.Equal("Unknown", net.NodeVersion);      // untouched default, as before

        var back = mapper.ToModel(live, net, "p", row.Created);
        Assert.Equal(row with { Id = 0 }, back);        // records: value equality

        var noNetwork = mapper.ToModel(live, null, "p", row.Created);
        Assert.Equal(0, noNetwork.BlockHeight);
        Assert.Equal(3, noNetwork.ConnectedMiners);
    }

    [Fact]
    public void PoolInfo_DoesNotAliasConfiguration()
    {
        var config = new PoolConfig
        {
            Id = "dgb-sha256",
            Address = "addr",
            Ports = new Dictionary<int, PoolEndpoint> { [3032] = new PoolEndpoint { Difficulty = 1024, Tls = true, TlsPfxFile = "/secret.pfx", TlsPfxPassword = "pw" } },
            PaymentProcessing = new PoolPaymentProcessingConfig
            {
                Enabled = true, MinimumPayment = 10, PayoutScheme = PayoutScheme.PPLNS,
                PayoutSchemeConfig = JObject.Parse("{\"factor\": 3.5}"),
                Extra = new Dictionary<string, object> { ["walletPassword"] = "hunter2", ["other"] = 1 },
            },
        };

        var info = mapper.ToPoolInfo(config);

        Assert.Equal("dgb-sha256", info.Id);
        Assert.Equal("PPLNS", info.PaymentProcessing.PayoutScheme);
        Assert.Equal(3.5m, info.PaymentProcessing.PayoutSchemeConfig.Factor);
        Assert.Equal(5.0m, info.PaymentProcessing.PayoutSchemeConfig.BlockFinderPercentage);   // default kept for an absent key

        // the API layer mutates these copies; the live configuration must be unaffected
        info.PaymentProcessing.Extra.Remove("walletPassword");
        info.Ports[3032].TlsPfxFile = null;
        info.Ports[3032].TlsPfxPassword = null;

        Assert.Equal("hunter2", config.PaymentProcessing.Extra["walletPassword"]);
        Assert.Equal("/secret.pfx", config.Ports[3032].TlsPfxFile);
        Assert.Equal("pw", config.Ports[3032].TlsPfxPassword);
        Assert.True(info.Ports[3032].Tls);
        Assert.Equal(1024, info.Ports[3032].Difficulty);
    }

    [Fact]
    public void PoolInfo_CopiesEveryPoolEndpointProperty()
    {
        // MiningcoreMapper.Clone copies PoolEndpoint by hand so the served copy can be scrubbed without touching the
        // live configuration. A property added to PoolEndpoint later would be dropped silently: this fails instead.
        var endpoint = new PoolEndpoint();
        var expected = new System.Collections.Generic.Dictionary<string, object>();

        foreach(var p in typeof(PoolEndpoint).GetProperties())
        {
            object value = p.PropertyType switch
            {
                var t when t == typeof(string) => "value-of-" + p.Name,
                var t when t == typeof(bool) => true,
                var t when t == typeof(double) => 12.5,
                var t when t == typeof(int) => 7,
                var t when t.IsClass => Activator.CreateInstance(t),
                _ => throw new Xunit.Sdk.XunitException($"extend this test for PoolEndpoint.{p.Name} ({p.PropertyType.Name})")
            };

            p.SetValue(endpoint, value);
            expected[p.Name] = value;
        }

        var config = new PoolConfig { Id = "p1", Ports = new System.Collections.Generic.Dictionary<int, PoolEndpoint> { [3333] = endpoint } };
        var copy = new MiningcoreMapper().ToPoolInfo(config).Ports[3333];

        foreach(var p in typeof(PoolEndpoint).GetProperties())
            Assert.True(Equals(expected[p.Name], p.GetValue(copy)), $"PoolEndpoint.{p.Name} was not copied by the mapper's Clone");
    }
}
