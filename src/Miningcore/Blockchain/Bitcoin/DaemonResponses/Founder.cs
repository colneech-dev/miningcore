using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.DaemonResponses
{
    public class Founder
    {
        public string Payee { get; set; }
        public string Script { get; set; }
        public long Amount { get; set; }
    }

    // Used by coins that return "founderreward": { "founderpayee": "...", "amount": N }
    public class FounderRewardEntry
    {
        [JsonProperty("founderpayee")]
        public string Founderpayee { get; set; }
        public long Amount { get; set; }
    }

    public class FounderBlockTemplateExtra
    {
        public JToken Founder { get; set; }

        [JsonProperty("founder_payments_started")]
        public bool FounderPaymentsStarted { get; set; }

        // fxtc-style: single founderreward object
        [JsonProperty("founderreward")]
        public FounderRewardEntry FounderReward { get; set; }

        [JsonProperty("founder_reward_enforced")]
        public bool FounderRewardEnforced { get; set; }
    }
}
