using System;
using BTCPayServer.Filters;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Nano.RPC
{
    [Route("[controller]")]
    [OnlyIfSupportAttribute("XNO")]
    public class NanoLikeCallbackController : Controller
    {
        private readonly EventAggregator _eventAggregator;

        public NanoLikeCallbackController(EventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }
        
        [HttpPost("tx")]
        public IActionResult OnTransactionNotify([FromQuery] string cryptoCode, [FromBody] TransactionCallback callback)
        {
            Console.WriteLine($"OnTransactionNotify: {cryptoCode}, {callback.Hash}, {callback.Destination}, {callback.Amount}");
            _eventAggregator.Publish(new NanoEvent()
            {
                Hash = callback.Hash,
                Destination = callback.Destination,
                Amount = callback.Amount,
                CryptoCode = cryptoCode.ToUpperInvariant()
            });
            return Ok();
        }
    }
    
    public class TransactionCallback
    {
        public string Hash { get; set; }
        public string Destination { get; set; }
        public string Amount { get; set; }
    }
}
