using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Nano.Configuration;
using BTCPayServer.Plugins.Nano.Payments;
using BTCPayServer.Plugins.Nano.Services;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Plugins.Nano.Controllers
{
    [Route("stores/{storeId}/nanolike")]
    [OnlyIfSupportAttribute("XNO")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UINanoLikeStoreController : Controller
    {
        private readonly NanoLikeConfiguration _NanoLikeConfiguration;
        private readonly StoreRepository _StoreRepository;
        private readonly NanoRPCProvider _NanoRpcProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;

        public UINanoLikeStoreController(NanoLikeConfiguration nanoLikeConfiguration,
            StoreRepository storeRepository, NanoRPCProvider nanoRpcProvider,
            PaymentMethodHandlerDictionary handlers,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _NanoLikeConfiguration = nanoLikeConfiguration;
            _StoreRepository = storeRepository;
            _NanoRpcProvider = nanoRpcProvider;
            _handlers = handlers;
            _BtcPayNetworkProvider = btcPayNetworkProvider;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public async Task<IActionResult> GetStoreNanoLikePaymentMethods()
        {
            return View(await GetVM(StoreData));
        }
[NonAction]
        public async Task<NanoLikePaymentMethodListViewModel> GetVM(StoreData storeData)
        {
            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
            
            return new NanoLikePaymentMethodListViewModel()
            {
                Items = _NanoLikeConfiguration.NanoLikeConfigurationItems.Select(pair => GetNanoLikePaymentMethodViewModel(storeData, pair.Key, excludeFilters))
            };
        }

        private NanoLikePaymentMethodViewModel GetNanoLikePaymentMethodViewModel(StoreData storeData, string cryptoCode, IPaymentFilter excludeFilters)
        {
            var nano = storeData.GetPaymentMethodConfigs(_handlers)
                .Where(s => s.Value is NanoPaymentPromptDetails)
                .Select(s => (PaymentMethodId: s.Key, Details: (NanoPaymentPromptDetails)s.Value));
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var settings = nano.Where(method => method.PaymentMethodId == pmi).Select(m => m.Details).SingleOrDefault();
            _NanoRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
            _NanoLikeConfiguration.NanoLikeConfigurationItems.TryGetValue(cryptoCode, out var configurationItem);

            return new NanoLikePaymentMethodViewModel()
            {
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)),
                Summary = summary,
                CryptoCode = cryptoCode,
            };
        }

        [HttpGet("{cryptoCode}")]
        public async Task<IActionResult> GetStoreNanoLikePaymentMethod(string cryptoCode)
        {
            if (!_NanoLikeConfiguration.NanoLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            var vm = GetNanoLikePaymentMethodViewModel(StoreData, cryptoCode, StoreData.GetStoreBlob().GetExcludedPaymentMethods());
            return View(nameof(GetStoreNanoLikePaymentMethod), vm);
        }

        [HttpPost("{cryptoCode}")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> GetStoreNanoLikePaymentMethod(NanoLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_NanoLikeConfiguration.NanoLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem))
            {
                return NotFound();
            }

            if (command == "add-account")
            {
                
            }
            else if (command == "upload-wallet")
            {
                
            }

            if (!ModelState.IsValid)
            {

                var vm = GetNanoLikePaymentMethodViewModel(StoreData, cryptoCode,
                    StoreData.GetStoreBlob().GetExcludedPaymentMethods());

                vm.Enabled = viewModel.Enabled;
                vm.NewAccountLabel = viewModel.NewAccountLabel;
                vm.AccountIndex = viewModel.AccountIndex;
                vm.SettlementConfirmationThresholdChoice = viewModel.SettlementConfirmationThresholdChoice;
                vm.CustomSettlementConfirmationThreshold = viewModel.CustomSettlementConfirmationThreshold;
                return View(vm);
            }

            var storeData = StoreData;
            var blob = storeData.GetStoreBlob();
            storeData.SetPaymentMethodConfig(_handlers[PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)], new NanoPaymentPromptDetails()
            {
                
            });

            blob.SetExcluded(PaymentTypes.CHAIN.GetPaymentMethodId(viewModel.CryptoCode), !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreNanoLikePaymentMethods",
                new { StatusMessage = $"{cryptoCode} settings updated successfully", storeId = StoreData.Id });
        }

        public class NanoLikePaymentMethodListViewModel
        {
            public IEnumerable<NanoLikePaymentMethodViewModel> Items { get; set; }
        }

        public class NanoLikePaymentMethodViewModel : IValidatableObject
        {
            public NanoRPCProvider.NanoLikeSummary Summary { get; set; }
            public string CryptoCode { get; set; }
            public string NewAccountLabel { get; set; }
            public long AccountIndex { get; set; }
            public bool Enabled { get; set; }

            public IEnumerable<SelectListItem> Accounts { get; set; }
            public bool WalletFileFound { get; set; }
            [Display(Name = "View-Only Wallet File")]
            public IFormFile WalletFile { get; set; }
            [Display(Name = "Wallet Keys File")]
            public IFormFile WalletKeysFile { get; set; }
            [Display(Name = "Wallet Password")]
            public string WalletPassword { get; set; }
            [Display(Name = "Consider the invoice settled when the payment transaction …")]
            public NanoLikeSettlementThresholdChoice SettlementConfirmationThresholdChoice { get; set; }
            [Display(Name = "Required Confirmations"), Range(0, 100)]
            public long? CustomSettlementConfirmationThreshold { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (SettlementConfirmationThresholdChoice is NanoLikeSettlementThresholdChoice.Custom
                    && CustomSettlementConfirmationThreshold is null)
                {
                    yield return new ValidationResult(
                        "You must specify the number of required confirmations when using a custom threshold.",
                        new[] { nameof(CustomSettlementConfirmationThreshold) });
                }
            }
        }

        public enum NanoLikeSettlementThresholdChoice
        {
            [Display(Name = "Store Speed Policy", Description = "Use the store's speed policy")]
            StoreSpeedPolicy,
            [Display(Name = "Zero Confirmation", Description = "Is unconfirmed")]
            ZeroConfirmation,
            [Display(Name = "At Least One", Description = "Has at least 1 confirmation")]
            AtLeastOne,
            [Display(Name = "At Least Ten", Description = "Has at least 10 confirmations")]
            AtLeastTen,
            [Display(Name = "Custom", Description = "Custom")]
            Custom
        }
    }
}
