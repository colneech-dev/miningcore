using System;
using Autofac;
using Miningcore.Mapping;
using Miningcore.Persistence.Model;
using Xunit;

using EntityAuxBlock = Miningcore.Persistence.Postgres.Entities.AuxBlock;
using ResponseAuxBlock = Miningcore.Api.Responses.AuxBlock;

namespace Miningcore.Tests.Persistence;

/// <summary>
/// Verifies that the Mapperly mappings for AuxBlock round-trip correctly
/// through the model → entity → model and model → API response paths.
/// These catch missing mappings, renamed properties, or type mismatches silently
/// introduced when the model or entity is changed.
/// </summary>
public class AuxBlockMapperTests : TestBase
{
    private readonly MiningcoreMapper mapper;

    public AuxBlockMapperTests()
    {
        mapper = container.Resolve<MiningcoreMapper>();
    }

    [Fact]
    public void AuxBlock_ModelToEntity_RoundTrip()
    {
        var model = new AuxBlock
        {
            Id = 42,
            PoolId = "dgb-solo",
            ChainId = "spacexpanse",
            ChainName = "SpaceXpanse",
            BlockHeight = 3831133,
            AuxBlockHash = "71adb2b474aca0816cd7bf0e5af46f28ac9e846909a78caf36ea49bb8f6366ec",
            ParentBlockHash = "abcdef1234567890",
            Status = BlockStatus.Confirmed,
            ConfirmationProgress = 1.0,
            Reward = 100m,
            Miner = "YOUR_DGB_ADDRESS",
            Worker = "worker01",
            Source = "TestCluster",
            SubmittedVia = "live",
            Created = new DateTime(2026, 5, 20, 6, 5, 14, DateTimeKind.Utc),
        };

        var entity = mapper.ToEntity(model);

        Assert.Equal(model.PoolId, entity.PoolId);
        Assert.Equal(model.ChainId, entity.ChainId);
        Assert.Equal(model.ChainName, entity.ChainName);
        Assert.Equal((long?)model.BlockHeight, entity.BlockHeight);
        Assert.Equal(model.AuxBlockHash, entity.AuxBlockHash);
        Assert.Equal(model.ParentBlockHash, entity.ParentBlockHash);
        Assert.Equal("confirmed", entity.Status);
        Assert.Equal(model.ConfirmationProgress, entity.ConfirmationProgress);
        Assert.Equal(model.Reward, entity.Reward);
        Assert.Equal(model.Miner, entity.Miner);
        Assert.Equal(model.Worker, entity.Worker);
        Assert.Equal(model.Source, entity.Source);
        Assert.Equal(model.SubmittedVia, entity.SubmittedVia);
        Assert.Equal(model.Created, entity.Created);

        // map back
        var roundTripped = mapper.ToModel(entity);

        Assert.Equal(model.PoolId, roundTripped.PoolId);
        Assert.Equal(model.ChainId, roundTripped.ChainId);
        Assert.Equal(model.BlockHeight, roundTripped.BlockHeight);
        Assert.Equal(model.AuxBlockHash, roundTripped.AuxBlockHash);
        Assert.Equal(BlockStatus.Confirmed, roundTripped.Status);
        Assert.Equal(model.Miner, roundTripped.Miner);
        Assert.Equal(model.Worker, roundTripped.Worker);
    }

    [Fact]
    public void AuxBlock_ModelToApiResponse_MapsAllFields()
    {
        var model = new AuxBlock
        {
            PoolId = "dgb-solo",
            ChainId = "spacexpanse",
            ChainName = "SpaceXpanse",
            BlockHeight = 3831133,
            AuxBlockHash = "71adb2b474aca0816cd7bf0e5af46f28ac9e846909a78caf36ea49bb8f6366ec",
            ParentBlockHash = "abcdef1234567890",
            Status = BlockStatus.Pending,
            ConfirmationProgress = 0.0,
            Reward = 100m,
            Miner = "YOUR_DGB_ADDRESS",
            Worker = "worker02",
            Source = "TestCluster",
            SubmittedVia = "live",
            Created = new DateTime(2026, 5, 20, 6, 5, 14, DateTimeKind.Utc),
        };

        var response = mapper.ToResponse(model);

        Assert.Equal(model.PoolId, response.PoolId);
        Assert.Equal(model.ChainId, response.ChainId);
        Assert.Equal(model.ChainName, response.ChainName);
        Assert.Equal(model.BlockHeight, response.BlockHeight);
        Assert.Equal(model.AuxBlockHash, response.AuxBlockHash);
        Assert.Equal(model.ParentBlockHash, response.ParentBlockHash);
        Assert.Equal("pending", response.Status);
        Assert.Equal(model.Reward, response.Reward);
        Assert.Equal(model.Miner, response.Miner);
        Assert.Equal(model.Worker, response.Worker);
        Assert.Equal(model.SubmittedVia, response.SubmittedVia);
        Assert.Equal(model.Created, response.Created);
    }

    [Fact]
    public void AuxBlock_NullableFields_MapCorrectly()
    {
        var model = new AuxBlock
        {
            PoolId = "gvia-solo",
            ChainId = "spacexpanse",
            ChainName = "SpaceXpanse",
            BlockHeight = null,
            AuxBlockHash = "6f9c7b4548823282f407fbe0e6d241d8345a11f51434e24a8c679a319a75d01a",
            ParentBlockHash = null,
            Status = BlockStatus.Confirmed,
            ConfirmationProgress = 1.0,
            Reward = null,
            Miner = null,
            Worker = null,
            Source = null,
            SubmittedVia = "backfill",
            Created = new DateTime(2026, 5, 19, 7, 2, 24, DateTimeKind.Utc),
        };

        var entity = mapper.ToEntity(model);

        Assert.Null(entity.BlockHeight);
        Assert.Null(entity.ParentBlockHash);
        Assert.Null(entity.Reward);
        Assert.Null(entity.Miner);
        Assert.Null(entity.Worker);

        var response = mapper.ToResponse(model);

        Assert.Null(response.BlockHeight);
        Assert.Null(response.ParentBlockHash);
        Assert.Null(response.Reward);
        Assert.Null(response.Miner);
        Assert.Null(response.Worker);
    }
}
