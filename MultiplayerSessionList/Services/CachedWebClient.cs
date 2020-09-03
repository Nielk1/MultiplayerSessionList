using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Services
{
    public class CachedWebClient<T>
    {
        private HttpClient httpClient;
        private IMemoryCache memCache;

        public CachedWebClient(IMemoryCache memCache)
        {
            this.httpClient = new HttpClient();
            this.memCache = memCache;
        }

        public async Task<T> GetJson(string url)
        {
            T data = memCache.Get<T>(url);
            if (data != null)
                return data;

            try
            {
                string rawJson = await httpClient.GetStringAsync(url);
                data = JsonConvert.DeserializeObject<T>(rawJson);
                if (data == null)
                    return data;
                memCache.Set(url, data, TimeSpan.FromHours(1));
                return data;
            }
            catch { }

            return default(T);
        }
    }
}