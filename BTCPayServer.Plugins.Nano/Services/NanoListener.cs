using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Plugins.Nano.Configuration;
using BTCPayServer.Plugins.Nano.Payments;
using BTCPayServer.Plugins.Nano.RPC;
using BTCPayServer.Plugins.Nano.Utils;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Scripting;
using NBitpayClient;
using NBXplorer;
using Newtonsoft.Json.Linq;
using static BTCPayServer.Client.Models.InvoicePaymentMethodDataModel;

namespace BTCPayServer.Plugins.Nano.Services
{
    public class NanoListener : IHostedService
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly NanoRPCProvider _NanoRpcProvider;
        private readonly NanoLikeConfiguration _NanoLikeConfiguration;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly ILogger<NanoListener> _logger;
        private readonly PaymentService _paymentService;
        private readonly InvoiceActivator _invoiceActivator;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly CompositeDisposable leases = new CompositeDisposable();
        private readonly Channel<Func<Task>> _requests = Channel.CreateUnbounded<Func<Task>>();
        private CancellationTokenSource _Cts;

        public NanoListener(InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            NanoRPCProvider NanoRpcProvider,
            NanoLikeConfiguration NanoLikeConfiguration,
            BTCPayNetworkProvider networkProvider,
            ILogger<NanoListener> logger, 
            PaymentService paymentService,
            InvoiceActivator invoiceActivator,
            PaymentMethodHandlerDictionary handlers)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _NanoRpcProvider = NanoRpcProvider;
            _NanoLikeConfiguration = NanoLikeConfiguration;
            _networkProvider = networkProvider;
            _logger = logger;
            _paymentService = paymentService;
            _invoiceActivator = invoiceActivator;
            _handlers = handlers;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_NanoLikeConfiguration.NanoLikeConfigurationItems.Any())
            {
                return Task.CompletedTask;
            }
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            leases.Add(_eventAggregator.Subscribe<NanoEvent>(OnNanoEvent));
            leases.Add(_eventAggregator.Subscribe<NanoRPCProvider.NanoDaemonStateChange>(e =>
            {
                if (_NanoRpcProvider.IsAvailable(e.CryptoCode))
                {
                    _logger.LogInformation($"{e.CryptoCode} just became available");
                    _ = UpdateAnyPendingNanoLikePayment(e.CryptoCode);
                }
                else
                {
                    _logger.LogInformation($"{e.CryptoCode} just became unavailable");
                }
            }));
            _ = WorkThroughQueue(_Cts.Token);
            return Task.CompletedTask;
        }

        private async Task WorkThroughQueue(CancellationToken token)
        {
            while (await _requests.Reader.WaitToReadAsync(token) && _requests.Reader.TryRead(out var action)) {
                token.ThrowIfCancellationRequested();
                try {
                    await action.Invoke();
                }
                catch (Exception e)
                {
                    _logger.LogError($"error with action item {e}");
                }
            }
        }

        private void OnNanoEvent(NanoEvent obj)
        {
            if (!_NanoRpcProvider.IsAvailable(obj.CryptoCode))
            {
                return;
            }

            if (!string.IsNullOrEmpty(obj.Hash))
            {
                if (!_requests.Writer.TryWrite(() => OnTransaction(obj.CryptoCode, obj))) {
                    _logger.LogWarning($"Failed to write new tx task to channel");
                }
            }
        }

        private async Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            _logger.LogInformation(
                $"Invoice {invoice.Id} received payment {payment.Value} {payment.Currency} {payment.Id}");
            
            var prompt = invoice.GetPaymentPrompt(payment.PaymentMethodId);

            if (prompt != null &&
                prompt.Activated &&
                prompt.Destination == payment.Destination &&
                prompt.Calculate().Due > 0.0m)
            {
                //todo: wtf does this do
                await _invoiceActivator.ActivateInvoicePaymentMethod(invoice.Id, payment.PaymentMethodId, true);
                invoice = await _invoiceRepository.GetInvoice(invoice.Id);
            }

            _eventAggregator.Publish(
                new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        }

        private async Task UpdatePaymentStates(string cryptoCode, InvoiceEntity[] invoices)
        {
            if (!invoices.Any())
            {
                return;
            }

            var NanoWalletRpcClient = _NanoRpcProvider.PippinClients[cryptoCode];
            var network = _networkProvider.GetNetwork(cryptoCode);

            var paymentId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (NanoLikePaymentMethodHandler)_handlers[paymentId];

            //get all the required data in one list (invoice, its existing payments and the current payment method details)
            var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                    ExistingPayments: GetAllNanoLikePayments(entity, cryptoCode),
                    Prompt: entity.GetPaymentPrompt(paymentId),
                    PaymentMethodDetails: handler.ParsePaymentPromptDetails(entity.GetPaymentPrompt(paymentId).Details)))
                .Select(tuple => (
                    tuple.Invoice,
                    tuple.PaymentMethodDetails,
                    tuple.Prompt,
                    ExistingPayments: tuple.ExistingPayments.Select(entity =>
                        (Payment: entity, PaymentData: handler.ParsePaymentDetails(entity.Details),
                            tuple.Invoice))
                ));

            var existingPaymentData = expandedInvoices.SelectMany(tuple => tuple.ExistingPayments);

            var accountToAddressQuery = new Dictionary<long, List<long>>();
            //create list of subaddresses to account to query the Nano wallet
            // foreach (var expandedInvoice in expandedInvoices)
            // {
            //     var addressIndexList =
            //         accountToAddressQuery.GetValueOrDefault(expandedInvoice.PaymentMethodDetails.AccountIndex,
            //             new List<long>());
            //
            //     addressIndexList.AddRange(
            //         expandedInvoice.ExistingPayments.Select(tuple => tuple.PaymentData.SubaddressIndex));
            //     addressIndexList.Add(expandedInvoice.PaymentMethodDetails.AddressIndex);
            //     accountToAddressQuery.AddOrReplace(expandedInvoice.PaymentMethodDetails.AccountIndex, addressIndexList);
            // }

            // var tasks = accountToAddressQuery.ToDictionary(datas => datas.Key,
            //     datas => NanoWalletRpcClient.SendCommandAsync<GetTransfersRequest, GetTransfersResponse>(
            //         "get_transfers",
            //         new GetTransfersRequest()
            //         {
            //             AccountIndex = datas.Key,
            //             In = true,
            //             SubaddrIndices = datas.Value.Distinct().ToList()
            //         }));

            // await Task.WhenAll(tasks.Values);


            var transferProcessingTasks = new List<Task>();

            var updatedPaymentEntities = new BlockingCollection<(PaymentEntity Payment, InvoiceEntity invoice)>();
            // foreach (var keyValuePair in tasks)
            // {
            //     var transfers = keyValuePair.Value.Result.In;
            //     if (transfers == null)
            //     {
            //         continue;
            //     }
            //
            //     transferProcessingTasks.AddRange(transfers.Select(transfer =>
            //     {
            //         InvoiceEntity invoice = null;
            //         var existingMatch = existingPaymentData.SingleOrDefault(tuple =>
            //             tuple.Payment.Destination == transfer.Address &&
            //             tuple.PaymentData.TransactionId == transfer.Txid);
            //
            //         if (existingMatch.Invoice != null)
            //         {
            //             invoice = existingMatch.Invoice;
            //         }
            //         else
            //         {
            //             var newMatch = expandedInvoices.SingleOrDefault(tuple =>
            //                 tuple.Prompt.Destination == transfer.Address);
            //
            //             if (newMatch.Invoice == null)
            //             {
            //                 return Task.CompletedTask;
            //             }
            //
            //             invoice = newMatch.Invoice;
            //         }
            //
            //
            //         return HandlePaymentData(cryptoCode, transfer.Address, transfer.Amount, transfer.SubaddrIndex.Major,
            //             transfer.SubaddrIndex.Minor, transfer.Txid, transfer.Confirmations, transfer.Height, invoice,
            //             updatedPaymentEntities);
            //     }));
            // }

            transferProcessingTasks.Add(
                _paymentService.UpdatePayments(updatedPaymentEntities.Select(tuple => tuple.Item1).ToList()));
            await Task.WhenAll(transferProcessingTasks);
            foreach (var valueTuples in updatedPaymentEntities.GroupBy(entity => entity.Item2))
            {
                if (valueTuples.Any())
                {
                    _eventAggregator.Publish(new Events.InvoiceNeedUpdateEvent(valueTuples.Key.Id));
                }
            }
        }
        
        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            _Cts?.Cancel();
            return Task.CompletedTask;
        }

        private async Task OnTransaction(string cryptoCode, NanoEvent transaction)
        {
            var network = _networkProvider.GetNetwork(cryptoCode);
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (NanoLikePaymentMethodHandler)_handlers[pmi];
            
            // find the invoice corresponding to this address, else skip
             var invoice = await _invoiceRepository.GetInvoiceFromAddress(pmi, transaction.Destination);
             if (invoice == null)
             {
                 Console.WriteLine("couldn't find invoice with destination address: " + transaction.Destination);
                 return;
             }

             var details = new NanoLikePaymentData()
             {
                 TransactionHash = transaction.Hash,
             };
             var paymentData = new PaymentData()
             {
                 Status = PaymentStatus.Settled,
                 Amount = NanoMoney.Convert(UInt128.Parse(transaction.Amount)),
                 Created = DateTimeOffset.UtcNow,
                 Id = $"{transaction.Hash}",
                 Currency = network.CryptoCode,
                 InvoiceDataId = invoice.Id
             }.Set(invoice, handler, details);
            
             var payment = await _paymentService.AddPayment(paymentData, [transaction.Hash]);
             if (payment != null)
             {
                 Console.WriteLine("successfully added payment to _paymentService");
                 await ReceivedPayment(invoice, payment);
             }
             else
             {
                 Console.WriteLine("failed to add payment to _paymentService");
             }
        }

        private async Task UpdateAnyPendingNanoLikePayment(string cryptoCode)
        {
            var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var invoices = await _invoiceRepository.GetMonitoredInvoices(paymentMethodId);
            if (!invoices.Any())
                return;
            invoices = invoices.Where(entity => entity.GetPaymentPrompt(paymentMethodId)?.Activated is true).ToArray();
            await UpdatePaymentStates(cryptoCode, invoices);
        }

        private IEnumerable<PaymentEntity> GetAllNanoLikePayments(InvoiceEntity invoice, string cryptoCode)
        {
            return invoice.GetPayments(false)
                .Where(p => p.PaymentMethodId == PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode));
        }
    }
}
