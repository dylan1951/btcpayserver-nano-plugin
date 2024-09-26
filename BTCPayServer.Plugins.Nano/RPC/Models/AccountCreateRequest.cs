using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class AccountCreateRequest : INanoRequest
    {
        [JsonProperty("action")]
        public string Action
        {
            get { return "account_create"; }
        }
        
        [JsonProperty("wallet")] public string Wallet { get; set; }
    }
}
