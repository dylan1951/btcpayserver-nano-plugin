namespace BTCPayServer.Plugins.Nano.RPC
{
    public class NanoEvent
    {
        public string Hash { get; set; }
        public string Destination { get; set; }
        public string Amount { get; set; }
        public string CryptoCode { get; set; }
    }
}
