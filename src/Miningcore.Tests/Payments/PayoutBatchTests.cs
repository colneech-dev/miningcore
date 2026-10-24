using System;
using System.Linq;
using Miningcore.Mapping;
using Miningcore.Persistence.Model;
using Xunit;

namespace Miningcore.Tests.Payments;

public class PayoutBatchTests
{
    [Fact]
    public void Recipients_RoundTrip_KeepsAddressesAmountsAndPoolId()
    {
        var balances = new[]
        {
            new Balance { PoolId = "p1", Address = "D1", Amount = 1.23456789m },
            new Balance { PoolId = "p1", Address = "D2", Amount = 0.5m },
        };

        var json = PayoutBatchRecipients.Serialize(balances);
        var back = PayoutBatchRecipients.Deserialize("p1", json);

        Assert.Equal(2, back.Length);
        Assert.Equal(balances.Select(x => (x.Address, x.Amount)), back.Select(x => (x.Address, x.Amount)));
        Assert.All(back, x => Assert.Equal("p1", x.PoolId));
    }

    [Fact]
    public void Recipients_Deserialize_ToleratesNullAndEmpty()
    {
        Assert.Empty(PayoutBatchRecipients.Deserialize("p1", null));
        Assert.Empty(PayoutBatchRecipients.Deserialize("p1", "[]"));
    }

    [Fact]
    public void Mapper_StoresStatusLowercase_AndReadsItBack()
    {
        var mapper = new MiningcoreMapper();

        var model = new PayoutBatch { Id = 7, PoolId = "p1", Status = PayoutBatchStatus.Sent, Recipients = "[]", Created = DateTime.UtcNow, Updated = DateTime.UtcNow };
        var entity = mapper.ToEntity(model);
        Assert.Equal("sent", entity.Status);

        entity.Status = "COMPLETED";
        Assert.Equal(PayoutBatchStatus.Completed, mapper.ToModel(entity).Status);
    }
}
