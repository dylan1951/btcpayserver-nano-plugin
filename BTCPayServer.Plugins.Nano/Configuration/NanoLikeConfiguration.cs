using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Nano.Configuration
{
    public class NanoLikeConfiguration
    {
        public Dictionary<string, NanoLikeConfigurationItem> NanoLikeConfigurationItems { get; set; } = new();
    }

    public class NanoLikeConfigurationItem
    {
        public Uri PippinUri { get; set; }
    }
}
