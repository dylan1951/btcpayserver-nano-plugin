using System;

namespace BTCPayServer.Plugins.Nano.Controllers
{
    public class NanoPaymentViewModel
    {
        public string Crypto { get; set; }
        public string Confirmations { get; set; }
        public string DepositAddress { get; set; }
        public string Amount { get; set; }
        public string TransactionId { get; set; }
        public DateTimeOffset ReceivedTime { get; set; }
        public string TransactionLink { get; set; }
    }
}
