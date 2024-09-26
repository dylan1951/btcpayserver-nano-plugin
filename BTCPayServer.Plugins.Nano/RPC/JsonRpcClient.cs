using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Nano.RPC
{
    public class JsonRpcClient(Uri address, HttpClient client = null)
    {
        private readonly HttpClient _httpClient = client ?? new HttpClient();

        public async Task<TResponse> SendCommandAsync<TRequest, TResponse>(TRequest request, CancellationToken cts = default)
            where TRequest : INanoRequest
        {
            var httpRequest = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = address,
                Content = new StringContent(
                    JsonConvert.SerializeObject(request),
                    Encoding.UTF8, "application/json")
            };

            var rawResult = await _httpClient.SendAsync(httpRequest, cts);
            var rawJson = await rawResult.Content.ReadAsStringAsync();
            
            rawResult.EnsureSuccessStatusCode();
            
            var errorResponse = JsonConvert.DeserializeObject<NanoErrorResponse>(rawJson);
            if (errorResponse?.Error != null)
            {
                throw new NanoRpcException(errorResponse.Error);
            }
            
            var response = JsonConvert.DeserializeObject<TResponse>(rawJson);
            return response;
        }
    }
    
    public interface INanoRequest
    {
        string Action { get; }
    }
    
    public class NanoErrorResponse
    {
        [JsonProperty("error")]
        public string Error { get; set; }
    }
    
    public class NanoRpcException(string message) : Exception(message);
}
