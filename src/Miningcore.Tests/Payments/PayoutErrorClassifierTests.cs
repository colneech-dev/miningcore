using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Miningcore.JsonRpc;
using Miningcore.Payments;
using Xunit;

namespace Miningcore.Tests.Payments;

public class PayoutErrorClassifierTests
{
    private static JsonRpcError Transport(Exception inner = null) => new(-500, "transport", null, inner);

    [Fact]
    public void NoError_IsNotUnknown() => Assert.False(PayoutErrorClassifier.IsOutcomeUnknown(null));

    [Fact]
    public void ConnectionRefused_ProvesNothingWasSent()
    {
        var refused = new HttpRequestException("refused", new SocketException((int) SocketError.ConnectionRefused));
        Assert.False(PayoutErrorClassifier.IsOutcomeUnknown(Transport(refused)));
    }

    [Theory]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.NetworkUnreachable)]
    [InlineData(SocketError.HostUnreachable)]
    public void NameResolutionAndRoutingFailures_ProveNothingWasSent(SocketError code)
    {
        Assert.False(PayoutErrorClassifier.IsOutcomeUnknown(Transport(new HttpRequestException("x", new SocketException((int) code)))));
    }

    [Fact]
    public void Timeout_IsUnknown() => Assert.True(PayoutErrorClassifier.IsOutcomeUnknown(Transport(new TaskCanceledException())));

    [Fact]
    public void ConnectionResetMidRequest_IsUnknown()
    {
        var reset = new HttpRequestException("reset", new SocketException((int) SocketError.ConnectionReset));
        Assert.True(PayoutErrorClassifier.IsOutcomeUnknown(Transport(reset)));
    }

    [Fact]
    public void TransportFailureWithoutDetail_IsUnknown() => Assert.True(PayoutErrorClassifier.IsOutcomeUnknown(Transport()));

    [Theory]
    [InlineData(-25)]
    [InlineData(-26)]
    [InlineData(-27)]
    public void WalletVerifyCodes_MayFollowARecordedTransaction(int code)
    {
        Assert.True(PayoutErrorClassifier.IsOutcomeUnknown(new JsonRpcError(code, "verify", null)));
    }

    [Theory]
    [InlineData(-6)]   // insufficient funds
    [InlineData(-13)]  // wallet unlock needed
    [InlineData(-5)]   // invalid address
    [InlineData(-4)]   // wallet error raised before sending
    public void OtherDaemonErrors_MeanNothingWasSent(int code)
    {
        Assert.False(PayoutErrorClassifier.IsOutcomeUnknown(new JsonRpcError(code, "daemon", null)));
    }
}
