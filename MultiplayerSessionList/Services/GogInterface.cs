using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Services
{
    public class GogInterface
    {
        private HttpClient httpClient;
        private IMemoryCache memCache;

        public GogInterface(IMemoryCache memCache)
        {
            this.httpClient = new HttpClient();
            this.memCache = memCache;
        }

        public static ulong CleanGalaxyUserId(ulong GalaxyUserId)
        {
            return GalaxyUserId & 0x00ffffffffffffff;
        }

        public async Task<GogUserData> Users(ulong GalaxyUserId)
        {
            GogUserData data = memCache.Get<GogUserData>($"https://users.gog.com/users/{GalaxyUserId}");
            if (data != null)
                return data;

            try
            {
                string rawJson = await httpClient.GetStringAsync($"https://users.gog.com/users/{GalaxyUserId}");
                data = JsonConvert.DeserializeObject<GogUserData>(rawJson);
                if (data == null)
                    return data;
                memCache.Set($"https://users.gog.com/users/{GalaxyUserId}", data, TimeSpan.FromHours(1));
                return data;
            }
            catch { }

            return null;
        }

        public class GogUserData
        {
            public string id { get; set; }
            public string username { get; set; }
            public DateTime created_date { get; set; }
            public GogUserAvatarData Avatar { get; set; }
            public bool is_employee { get; set; }
            public string[] tags { get; set; }
        }

        public class GogUserAvatarData
        {
            public string gog_image_id { get; set; }
            public string small { get; set; }
            public string small_2x { get; set; }
            public string medium { get; set; }
            public string medium_2x { get; set; }
            public string large { get; set; }
            public string large_2x { get; set; }
            public string sdk_img_32 { get; set; }
            public string sdk_img_64 { get; set; }
            public string sdk_img_184 { get; set; }
            public string menu_small { get; set; }
            public string menu_small_2 { get; set; }
            public string menu_big { get; set; }
            public string menu_big_2 { get; set; }
        }
    }
}