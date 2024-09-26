using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.Nano.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Nano.Services
{
    public class NanoLikeSummaryUpdaterHostedService : IHostedService
    {
        private readonly NanoRPCProvider _NanoRpcProvider;
        private readonly NanoLikeConfiguration _NanoLikeConfiguration;
        private CancellationTokenSource _Cts;

        public Logs Logs { get; }

        public NanoLikeSummaryUpdaterHostedService(NanoRPCProvider NanoRpcProvider, NanoLikeConfiguration NanoLikeConfiguration, Logs logs)
        {
            _NanoRpcProvider = NanoRpcProvider;
            _NanoLikeConfiguration = NanoLikeConfiguration;
            Logs = logs;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            foreach (var NanoLikeConfigurationItem in _NanoLikeConfiguration.NanoLikeConfigurationItems)
            {
                _ = StartLoop(_Cts.Token, NanoLikeConfigurationItem.Key);
            }
            return Task.CompletedTask;
        }

        private async Task StartLoop(CancellationToken cancellation, string cryptoCode)
        {
            Logs.PayServer.LogInformation($"Starting listening Nano-like daemons ({cryptoCode})");
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        await _NanoRpcProvider.UpdateSummary(cryptoCode);
                        if (_NanoRpcProvider.IsAvailable(cryptoCode))
                        {
                            await Task.Delay(TimeSpan.FromMinutes(1), cancellation);
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                        }
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        Logs.PayServer.LogError(ex, $"Unhandled exception in Summary updater ({cryptoCode})");
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                    }
                }
            }
            catch when (cancellation.IsCancellationRequested) { }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _Cts?.Cancel();
            return Task.CompletedTask;
        }
    }
}
