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

    [Fact]
    public void NoError_IsNotInsufficientFunds() => Assert.False(PayoutErrorClassifier.IsInsufficientFunds(null));

    [Theory]
    [InlineData("Account has insufficient funds")]   // old account-based wallets
    [InlineData("Insufficient funds")]
    public void WalletInsufficientFundsCode_IsInsufficientFunds(string message)
    {
        Assert.True(PayoutErrorClassifier.IsInsufficientFunds(new JsonRpcError(-6, message, null)));
    }

    [Theory]
    [InlineData("Wallet error: Insufficient funds")]
    [InlineData("INSUFFICIENT FUNDS")]
    public void GenericWalletErrorSayingInsufficientFunds_IsInsufficientFunds(string message)
    {
        Assert.True(PayoutErrorClassifier.IsInsufficientFunds(new JsonRpcError(-4, message, null)));
    }

    [Theory]
    [InlineData(-4, "Transaction creation failed")]    // e.g. too many tiny inputs: a different problem that needs an operator
    [InlineData(-13, "Please enter the wallet passphrase")]
    [InlineData(-5, "Invalid address")]
    [InlineData(-26, "insufficient funds")]            // verify codes keep their own, stricter handling
    public void OtherErrors_AreNotInsufficientFunds(int code, string message)
    {
        Assert.False(PayoutErrorClassifier.IsInsufficientFunds(new JsonRpcError(code, message, null)));
    }

    [Fact]
    public void InsufficientFunds_NeverMeansTheOutcomeIsUnknown()
    {
        // the two classifications must not overlap, or an unfunded batch would halt the pool like a possibly-sent one
        var error = new JsonRpcError(-6, "Account has insufficient funds", null);
        Assert.True(PayoutErrorClassifier.IsInsufficientFunds(error));
        Assert.False(PayoutErrorClassifier.IsOutcomeUnknown(error));
    }
}
