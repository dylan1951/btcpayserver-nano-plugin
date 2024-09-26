using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.Nano.Payments
{
    public class NanoLikePaymentData : CryptoPaymentData
    {
        public string TransactionHash { get; set; }
        public string GetPaymentProof()
        {
            return null;
        }
    }
}
