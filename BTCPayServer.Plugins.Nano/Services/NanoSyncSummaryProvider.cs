using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.Nano.Services
{
    public class NanoSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly NanoRPCProvider _NanoRpcProvider;

        public NanoSyncSummaryProvider(NanoRPCProvider NanoRpcProvider)
        {
            _NanoRpcProvider = NanoRpcProvider;
        }

        public bool AllAvailable()
        {
            return _NanoRpcProvider.Summaries.All(pair => pair.Value.PippinAvailable);
        }

        public string Partial { get; } = "NanoSyncSummary";
        public IEnumerable<ISyncStatus> GetStatuses()
        {
            return _NanoRpcProvider.Summaries.Select(pair => new NanoSyncStatus()
            {
                Summary = pair.Value, CryptoCode = pair.Key
            });
        }
    }

    public class NanoSyncStatus: SyncStatus, ISyncStatus
    {
        public override bool Available
        {
            get
            {
                return true;
            }
        }

        public NanoRPCProvider.NanoLikeSummary Summary { get; set; }
    }
}
