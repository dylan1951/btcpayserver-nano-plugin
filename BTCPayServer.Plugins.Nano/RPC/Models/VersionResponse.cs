using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public partial class VersionResponse
    {
        [JsonProperty("rpc_version")] public string RpcVersion { get; set; }
    }
}
