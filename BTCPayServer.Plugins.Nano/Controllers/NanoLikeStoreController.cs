using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Nano.Configuration;
using BTCPayServer.Plugins.Nano.Payments;
using BTCPayServer.Plugins.Nano.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

            return new NanoLikePaymentMethodViewModel()
            {
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)),
                CryptoCode = cryptoCode,
                WalletSeed = settings?.WalletSeed
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
            if (!_NanoLikeConfiguration.NanoLikeConfigurationItems.TryGetValue(cryptoCode, out _))
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                var vm = GetNanoLikePaymentMethodViewModel(StoreData, cryptoCode,
                    StoreData.GetStoreBlob().GetExcludedPaymentMethods());

                vm.Enabled = viewModel.Enabled;
                vm.WalletSeed = viewModel.WalletSeed;
                return View(vm);
            }

            var storeData = StoreData;
            var blob = storeData.GetStoreBlob();
            storeData.SetPaymentMethodConfig(_handlers[PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)], new NanoPaymentPromptDetails()
            {
                WalletSeed = viewModel.WalletSeed
            });

            blob.SetExcluded(PaymentTypes.CHAIN.GetPaymentMethodId(viewModel.CryptoCode), !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreNanoLikePaymentMethod",
                new { StatusMessage = $"{cryptoCode} settings updated successfully", storeId = StoreData.Id });
        }

        public class NanoLikePaymentMethodListViewModel
        {
            public IEnumerable<NanoLikePaymentMethodViewModel> Items { get; set; }
        }

        public class NanoLikePaymentMethodViewModel : IValidatableObject
        {
            public string CryptoCode { get; set; }
            public bool Enabled { get; set; }
            public string WalletSeed { get; set; }
            
            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                yield break;
            }
        }
    }
}
