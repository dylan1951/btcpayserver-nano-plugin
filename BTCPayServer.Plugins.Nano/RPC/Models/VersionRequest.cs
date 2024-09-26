using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class VersionRequest : INanoRequest
    {
        [JsonProperty("action")]
        public string Action
        {
            get { return "version"; }
        }
    }
}

