using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Services
{
    public class CachedAdvancedWebClient
    {
        private HttpClient httpClient;
        private IMemoryCache memCache;

        public CachedAdvancedWebClient(IMemoryCache memCache, HttpClient client)
        {
            //this.httpClient = new HttpClient();
            this.httpClient = client;
            this.memCache = memCache;
        }

        public async Task<CachedData<T>> GetObject<T>(string url)
        {
            return await GetObject<T>(url, TimeSpan.FromHours(1));
        }
        public async Task<CachedData<T>> GetObject<T>(string url, TimeSpan cacheTime)
        {
            return await GetObject<T>(url, cacheTime, cacheTime);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="url">URL to fetch</param>
        /// <param name="cacheTime">how long to cache</param>
        /// <param name="newTime">how long until cache is considered invalid even if present</param>
        /// <returns></returns>
        public async Task<CachedData<T>> GetObject<T>(string url, TimeSpan cacheTime, TimeSpan newTime)
        {
            // if we have cached data, get it, then return it if it's not expired
            CachedData<T> dataCached = memCache.Get<CachedData<T>>(url);
            if (dataCached != null)
                if (dataCached.Expires > DateTime.UtcNow)
                    return dataCached;

            try
            {
                string actualUrl = url;
                if (actualUrl.StartsWith("//"))
                    actualUrl = "https:" + actualUrl;
                var response = await httpClient.GetAsync(actualUrl);
                T data = default(T);
                if (typeof(T) == typeof(string))
                {
                    data = (T)Convert.ChangeType(await response.Content.ReadAsStringAsync(), typeof(T));
                }
                else
                {
                    data = JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync());
                }
                //if (data == null) // TODO consider caching null
                //    return null;
                var cacheData = new CachedData<T>(data, DateTime.UtcNow.Add(newTime), response.Content.Headers.LastModified?.UtcDateTime);
                memCache.Set(url, cacheData, cacheTime);
                return cacheData;
            }
            catch { }

            if (dataCached != null)
                return dataCached;

            return null;
        }
    }
    public class CachedData<T>
    {
        public T Data { get; set; }
        public DateTime Expires { get; set; }

        public DateTime? LastModified { get; set; }

        public CachedData(T Data, DateTime Expires, DateTime? LastModified)
        {
            this.Data = Data;
            this.Expires = Expires;
            this.LastModified = LastModified;
        }
    }
}