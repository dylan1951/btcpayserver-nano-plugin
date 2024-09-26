using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Dom;
using BTCPayServer.Common;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Plugins.Nano.RPC.Models;
using BTCPayServer.Plugins.Nano.Services;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Nano.Payments
{
    public class NanoLikePaymentMethodHandler : IPaymentMethodHandler
    {
        private readonly NanoLikeSpecificBtcPayNetwork _network;
        public NanoLikeSpecificBtcPayNetwork Network => _network;
        public JsonSerializer Serializer { get; }
        private readonly NanoRPCProvider _NanoRpcProvider;
        public PaymentMethodId PaymentMethodId { get; }
        public NanoLikePaymentMethodHandler(BTCPayNetworkBase network, NanoRPCProvider NanoRpcProvider)
        {
            _network = (NanoLikeSpecificBtcPayNetwork)network;
            PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(_network.CryptoCode);
            Serializer = BlobSerializer.CreateSerializer().Serializer;
            _NanoRpcProvider = NanoRpcProvider;
        }
        public Task BeforeFetchingRates(PaymentMethodContext context)
        {
            context.Prompt.Currency = _network.CryptoCode;
            context.Prompt.Divisibility = _network.Divisibility;
            if (context.Prompt.Activated)
            {
                var pippinClient = _NanoRpcProvider.PippinClients[_network.CryptoCode];
                var config = ParsePaymentMethodConfig(context.PaymentMethodConfig);
                context.State = new Prepare()
                {
                    ReserveAddress = s => pippinClient.SendCommandAsync<AccountCreateRequest, AccountCreateResponse>(new AccountCreateRequest
                    {
                        Wallet = "220e2ed1-7832-4f01-9e18-8bf210d12e2d"
                    })
                };
            }
            return Task.CompletedTask;
        }
        public async Task ConfigurePrompt(PaymentMethodContext context)
        {
            if (!_NanoRpcProvider.IsAvailable(_network.CryptoCode))
                throw new PaymentMethodUnavailableException($"Node or wallet not available");
            var invoice = context.InvoiceEntity;
            var nanoPrepare = (Prepare)context.State;
            var address = await nanoPrepare.ReserveAddress(invoice.Id);
            
            context.TrackedDestinations.Add(address.Account);
            context.Prompt.PaymentMethodFee = 0;
            context.Prompt.Destination = address.Account;
            context.Prompt.Details = JObject.FromObject(new NanoPaymentPromptDetails()
            {
                
            }, Serializer);
        }

        object IPaymentMethodHandler.ParsePaymentPromptDetails(Newtonsoft.Json.Linq.JToken details)
        {
            return ParsePaymentPromptDetails(details);
        }
        public NanoPaymentPromptDetails ParsePaymentPromptDetails(Newtonsoft.Json.Linq.JToken details)
        {
            return details.ToObject<NanoPaymentPromptDetails>(Serializer);
        }
        object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
        {
            return ParsePaymentMethodConfig(config);
        }
        public NanoPaymentPromptDetails ParsePaymentMethodConfig(JToken config)
        {
            return config.ToObject<NanoPaymentPromptDetails>(Serializer) ?? throw new FormatException($"Invalid {nameof(NanoLikePaymentMethodHandler)}");
        }
        
        class Prepare
        {
            public Func<string, Task<AccountCreateResponse>> ReserveAddress;
        }

        public CheckoutUIPaymentMethodSettings GetCheckoutUISettings()
        {
            return new CheckoutUIPaymentMethodSettings
            {
                ExtensionPartial = "Bitcoin/BitcoinLikeMethodCheckout",
                CheckoutBodyVueComponentName = "BitcoinLikeMethodCheckout",
                CheckoutHeaderVueComponentName = "BitcoinLikeMethodCheckoutHeader",
                NoScriptPartialName = "Bitcoin/BitcoinLikeMethodCheckoutNoScript"
            };
        }

        public NanoLikePaymentData ParsePaymentDetails(JToken details)
        {
            return details.ToObject<NanoLikePaymentData>(Serializer) ?? throw new FormatException($"Invalid {nameof(NanoLikePaymentData)}");
        }
        object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
        {
            return ParsePaymentDetails(details);
        }
    }
}
