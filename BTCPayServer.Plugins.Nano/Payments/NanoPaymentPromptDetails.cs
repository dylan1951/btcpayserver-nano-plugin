using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.Nano.Payments
{
    public class NanoPaymentPromptDetails
    {
        public string WalletSeed { get; set; }
        public string NanoRpcUrl { get; set; } // New field for Nano RPC URL
        public string NanoWebsocketUrl { get; set; } // New field for Nano Websocket URL
    }
}
