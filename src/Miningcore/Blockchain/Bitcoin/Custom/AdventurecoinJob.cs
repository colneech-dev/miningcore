using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Crypto;
using Miningcore.Extensions;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using Contract = Miningcore.Contracts.Contract;
using Transaction = NBitcoin.Transaction;
using System.Numerics;

namespace Miningcore.Blockchain.Bitcoin.Custom;

public class AdventurecoinJob : BitcoinJob
{

    #region Developer

    protected override Money CreateDeveloperOutputs(Transaction tx, Money reward)
    {
        if (developerParameters.Developer != null)
        {
            Developer[] developers;
            if (developerParameters.Developer.Type == JTokenType.Array)
                developers = developerParameters.Developer.ToObject<Developer[]>();
            else
                developers = new[] { developerParameters.Developer.ToObject<Developer>() };

            if(developers != null)
            {
                foreach(var Developer in developers)
                {
                    if(!string.IsNullOrEmpty(Developer.Script))
                    {
                        Script payeeAddress = new (Developer.Script.HexToByteArray());
                        var payeeReward = Developer.Amount;

                        tx.Outputs.Add(payeeReward, payeeAddress);
                        reward -= payeeReward;
                    }
                }
            }
        }

        return reward;
    }

    #endregion //Developer
}
