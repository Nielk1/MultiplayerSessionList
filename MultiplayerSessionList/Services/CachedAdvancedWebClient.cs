using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Services
{
    public class CachedAdvancedWebClient
    {
        private readonly IHttpClientFactory httpClientFactory;
        private readonly IMemoryCache memCache;
        // private readonly ILogger<CachedAdvancedWebClient>? _logger; // Optionally inject a logger

        public CachedAdvancedWebClient(IMemoryCache memCache, IHttpClientFactory clientFactory)
        {
            this.httpClientFactory = clientFactory;
            this.memCache = memCache;
        }

        public Task<CachedData<T>?> GetObject<T>(string url) =>
            GetObject<T>(url, TimeSpan.FromHours(1));

        public Task<CachedData<T>?> GetObject<T>(string url, TimeSpan cacheTime) =>
            GetObject<T>(url, cacheTime, cacheTime);

        public async Task<CachedData<T>?> GetObject<T>(
            string url,
            TimeSpan cacheTime,
            TimeSpan newTime,
            string? accept = null,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = url + (accept != null ? $"|accept:{accept}" : "");
            CachedData<T>? dataCached = memCache.Get<CachedData<T>>(cacheKey);
            if (dataCached != null && dataCached.Expires > DateTime.UtcNow)
                return dataCached;

            try
            {
                string actualUrl = url.StartsWith("//") ? "https:" + url : url;
                Uri destUrl = new Uri(actualUrl);
                string clientName = $"Hostname_{destUrl.Host}";
                using var httpClient = httpClientFactory.CreateClient(clientName);
                if (!string.IsNullOrEmpty(accept))
                    httpClient.DefaultRequestHeaders.Accept.ParseAdd(accept);

                using var response = await httpClient.GetAsync(actualUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return dataCached;

                T? data;
                var contentString = await response.Content.ReadAsStringAsync(cancellationToken);
                if (typeof(T) == typeof(string))
                    data = (T)(object)contentString;
                else
                    data = JsonConvert.DeserializeObject<T>(contentString);

                var cacheData = new CachedData<T>(data, DateTime.UtcNow.Add(newTime), response.Content.Headers.LastModified?.UtcDateTime);
                memCache.Set(cacheKey, cacheData, cacheTime);
                return cacheData;
            }
            catch (Exception ex)
            {
                // _logger?.LogError(ex, "Failed to fetch or deserialize data from {Url}", url);
                // TODO deal with exceptions as needed
                // might be work caching an error occured
            }

            return dataCached;
        }
    }

    public class CachedData<T>
    {
        public T? Data { get; set; }
        public DateTime Expires { get; set; }

        public DateTime? LastModified { get; set; }

        public CachedData(T? Data, DateTime Expires, DateTime? LastModified)
        {
            this.Data = Data;
            this.Expires = Expires;
            this.LastModified = LastModified;
        }
    }
}