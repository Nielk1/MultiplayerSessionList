using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Services
{
    public class CachedJsonWebClient
    {
        private HttpClient httpClient;
        private IMemoryCache memCache;

        public CachedJsonWebClient(IMemoryCache memCache)
        {
            this.httpClient = new HttpClient();
            this.memCache = memCache;
        }

        public async Task<T> GetObject<T>(string url)
        {
            return await GetObject<T>(url, TimeSpan.FromHours(1));
        }
        public async Task<T> GetObject<T>(string url, TimeSpan cacheTime)
        {
            T data = memCache.Get<T>(url);
            if (data != null)
                return data;

            try
            {
                if (url.StartsWith("//"))
                    url = "https:" + url;
                string rawJson = await httpClient.GetStringAsync(url);
                data = JsonConvert.DeserializeObject<T>(rawJson);
                if (data == null)
                    return data;
                memCache.Set(url, data, cacheTime);
                return data;
            }
            catch { }

            return default(T);
        }
    }
}