using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Nano.Configuration;
using BTCPayServer.Plugins.Nano.RPC;
using BTCPayServer.Plugins.Nano.RPC.Models;
using NBitcoin;

namespace BTCPayServer.Plugins.Nano.Services
{
    public class NanoRPCProvider
    {
        private readonly NanoLikeConfiguration _NanoLikeConfiguration;
        private readonly EventAggregator _eventAggregator;
        public ImmutableDictionary<string, JsonRpcClient> PippinClients;

        private readonly ConcurrentDictionary<string, NanoLikeSummary> _summaries = new();

        public ConcurrentDictionary<string, NanoLikeSummary> Summaries => _summaries;

        public NanoRPCProvider(NanoLikeConfiguration NanoLikeConfiguration, EventAggregator eventAggregator, IHttpClientFactory httpClientFactory)
        {
            _NanoLikeConfiguration = NanoLikeConfiguration;
            _eventAggregator = eventAggregator;
            PippinClients =
                _NanoLikeConfiguration.NanoLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.PippinUri, httpClientFactory.CreateClient()));
        }

        public bool IsAvailable(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return _summaries.ContainsKey(cryptoCode) && IsAvailable(_summaries[cryptoCode]);
        }

        private bool IsAvailable(NanoLikeSummary summary)
        {
            return true;
        }

        public async Task<NanoLikeSummary> UpdateSummary(string cryptoCode)
        {
            if (!PippinClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var pippinClient))
            {
                return null;
            }

            var summary = new NanoLikeSummary();
            try
            {
                await pippinClient.SendCommandAsync<VersionRequest, VersionResponse>(new VersionRequest());
                summary.PippinAvailable = true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                summary.PippinAvailable = false;
            }

            var changed = !_summaries.ContainsKey(cryptoCode) || IsAvailable(cryptoCode) != IsAvailable(summary);

            _summaries.AddOrReplace(cryptoCode, summary);
            if (changed)
            {
                _eventAggregator.Publish(new NanoDaemonStateChange() { Summary = summary, CryptoCode = cryptoCode });
            }

            return summary;
        }


        public class NanoDaemonStateChange
        {
            public string CryptoCode { get; set; }
            public NanoLikeSummary Summary { get; set; }
        }

        public class NanoLikeSummary
        {
            public bool PippinAvailable { get; set; }

            public override String ToString() { return String.Format(CultureInfo.InvariantCulture, "{0}", PippinAvailable); }
        }
    }
}
