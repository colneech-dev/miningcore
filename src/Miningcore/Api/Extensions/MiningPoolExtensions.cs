using Miningcore.Mapping;
using Miningcore.Api.Responses;
using Miningcore.Blockchain;
using Miningcore.Blockchain.Alephium.Configuration;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Ergo.Configuration;
using Miningcore.Blockchain.Handshake.Configuration;
using Miningcore.Blockchain.Kaspa.Configuration;
using Miningcore.Blockchain.Warthog.Configuration;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Mining;

namespace Miningcore.Api.Extensions;

public static class MiningPoolExtensions
{
    public static PoolInfo ToPoolInfo(this PoolConfig poolConfig, MiningcoreMapper mapper, Persistence.Model.PoolStats stats, IMiningPool pool)
    {
        var poolInfo = mapper.ToPoolInfo(poolConfig);

        poolInfo.PoolStats = mapper.ToMiningStats(stats);
        poolInfo.NetworkStats = pool?.NetworkStats ?? (stats != null ? mapper.ToBlockchainStats(stats) : new BlockchainStats());

        // pool wallet link
        var addressInfobaseUrl = poolConfig.Template?.ExplorerAccountLink;
        if(!string.IsNullOrEmpty(addressInfobaseUrl))
            poolInfo.AddressInfoLink = string.Format(addressInfobaseUrl, poolInfo.Address);

        // pool fees
        poolInfo.PoolFeePercent = poolConfig.RewardRecipients != null ? (float) poolConfig.RewardRecipients.Sum(x => x.Percentage) : 0;

        // strip security critical stuff
        if(poolInfo.PaymentProcessing?.Extra != null && poolInfo.Coin != null)
        {
            var extra = poolInfo.PaymentProcessing.Extra;

            switch(poolInfo.Coin.Family)
            {
                case "alephium":
                    extra.StripValue(nameof(AlephiumPaymentProcessingConfigExtra.WalletPassword));
                    break;
                case "bitcoin":
                    extra.StripValue(nameof(BitcoinPoolPaymentProcessingConfigExtra.WalletPassword));
                    break;
                case "ergo":
                    extra.StripValue(nameof(ErgoPaymentProcessingConfigExtra.WalletPassword));
                    break;
                case "handshake":
                    extra.StripValue(nameof(HandshakePoolPaymentProcessingConfigExtra.WalletPassword));
                    break;
                case "kaspa":
                    extra.StripValue(nameof(KaspaPaymentProcessingConfigExtra.WalletPassword));
                    break;
                case "warthog":
                    extra.StripValue(nameof(WarthogPaymentProcessingConfigExtra.WalletPrivateKey));
                    break;
            }
        }

        if(poolInfo.Ports != null)
        {
            foreach(var port in poolInfo.Ports.Keys)
            {
                var portInfo = poolInfo.Ports[port];

                portInfo.TlsPfxFile = null;
                portInfo.TlsPfxPassword = null;
            }
        }
        return poolInfo;
    }
}
