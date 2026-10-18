using System.Net.Sockets;
using Miningcore.JsonRpc;

namespace Miningcore.Payments;

/// <summary>
/// Decides whether a failed send may nevertheless have moved coins. This is what keeps payouts
/// fail-closed without halting a pool for failures that provably sent nothing.
/// </summary>
public static class PayoutErrorClassifier
{
    // Bitcoin RPC verify codes: the wallet may already have recorded the transaction when it reports these
    // (wallets that commit before broadcasting), so they are not proof that nothing was sent.
    private const int RpcVerifyError = -25;
    private const int RpcVerifyRejected = -26;
    private const int RpcVerifyAlreadyInChain = -27;

    /// <summary>True when coins may have left the wallet (so the batch must stay pending).</summary>
    public static bool IsOutcomeUnknown(JsonRpcError error)
    {
        if(error == null)
            return false;

        // RpcClient reports transport failures as -500 with no daemon response
        if(error.Code == -500)
            return !IsConnectionNeverEstablished(error.InnerException);

        return error.Code is RpcVerifyError or RpcVerifyRejected or RpcVerifyAlreadyInChain;
    }

    /// <summary>Connection refused, unknown host or unreachable network: the request never reached the daemon.</summary>
    public static bool IsConnectionNeverEstablished(Exception ex)
    {
        for(var e = ex; e != null; e = e.InnerException)
        {
            if(e is SocketException se && se.SocketErrorCode is
                   SocketError.ConnectionRefused or SocketError.HostNotFound or SocketError.NetworkUnreachable or
                   SocketError.HostUnreachable or SocketError.TryAgain or SocketError.NoData)
                return true;
        }

        return false;
    }
}
