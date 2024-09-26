using System;
using System.Globalization;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Plugins.Nano.Configuration;
using BTCPayServer.Plugins.Nano.Payments;
using BTCPayServer.Plugins.Nano.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using BTCPayServer.Services;

namespace BTCPayServer.Plugins.Nano;

public class NanoPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.0.0" }
    ];

    public override void Execute(IServiceCollection services)
    {
        var network = new NanoLikeSpecificBtcPayNetwork
        {
            CryptoCode = "XNO",
            DisplayName = "Nano",
            Divisibility = 12,
            DefaultRateRules = new[]
            {
                "XNO_X = XNO_BTC * BTC_X",
                "XNO_BTC = kraken(NANO_BTC)"
            },
            CryptoImagePath = "/imlegacy/nano.png",
            UriScheme = "nano"
        };
        
        const string blockExplorerLink = "https://nanexplorer.com/nano/block/{0}";
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("XNO");
        
        services.AddBTCPayNetwork(network).AddTransactionLinkProvider(network.CryptoCode, new SimpleTransactionLinkProvider(blockExplorerLink));
        services.AddSingleton(provider => (IPaymentMethodViewExtension)ActivatorUtilities.CreateInstance(provider, typeof(BitcoinPaymentMethodViewExtension), pmi));

        services.AddSingleton(provider => ConfigureNanoLikeConfiguration(provider));
        
        services.AddSingleton<NanoRPCProvider>();
        services.AddHostedService<NanoLikeSummaryUpdaterHostedService>();
        services.AddHostedService<NanoListener>();
        
        services.AddSingleton(provider => (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(NanoLikePaymentMethodHandler), network));
        services.AddSingleton(provider => (IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(NanoPaymentLinkExtension), network, pmi));
        services.AddSingleton(provider => (IPaymentModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(NanoPaymentModelExtension), network, pmi));
        
        services.AddSingleton<IUIExtension>(new UIExtension("StoreNavNanoExtension", "store-nav"));
        services.AddSingleton<IUIExtension>(new UIExtension("StoreWalletsNavNanoExtension", "store-wallets-nav"));
        services.AddSingleton<IUIExtension>(new UIExtension("ViewNanoLikePaymentData", "store-invoices-payments"));
        services.AddSingleton<ISyncSummaryProvider, NanoSyncSummaryProvider>();
    }
    
    static NanoLikeConfiguration ConfigureNanoLikeConfiguration(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetService<IConfiguration>();
        var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
        var result = new NanoLikeConfiguration();

        var supportedNetworks = btcPayNetworkProvider.GetAll().OfType<NanoLikeSpecificBtcPayNetwork>();

        foreach (var nanoLikeSpecificBtcPayNetwork in supportedNetworks)
        {
            var daemonUri =
                configuration.GetOrDefault<Uri>($"{nanoLikeSpecificBtcPayNetwork.CryptoCode}_pippin_uri", new Uri("http://127.0.01:11338"));
            // if (daemonUri == null)
            // {
            //     throw new ConfigException($"{nanoLikeSpecificBtcPayNetwork.CryptoCode} is misconfigured");
            // }

            result.NanoLikeConfigurationItems.Add(nanoLikeSpecificBtcPayNetwork.CryptoCode, new NanoLikeConfigurationItem()
            {
                PippinUri = daemonUri
            });
        }
        return result;
    }
    
    class SimpleTransactionLinkProvider : DefaultTransactionLinkProvider
    {
        public SimpleTransactionLinkProvider(string blockExplorerLink) : base(blockExplorerLink)
        {
        }

        public override string? GetTransactionLink(string paymentId)
        {
            if (string.IsNullOrEmpty(BlockExplorerLink))
                return null;
            return string.Format(CultureInfo.InvariantCulture, BlockExplorerLink, paymentId);
        }
    }
}
