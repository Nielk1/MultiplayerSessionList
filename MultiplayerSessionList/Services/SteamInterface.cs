using Microsoft.Extensions.Caching.Memory;
using System.Net.Http;
using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Steam.Models.SteamCommunity;

namespace MultiplayerSessionList.Services
{
    public class SteamInterface
    {
        //private HttpClient httpClient;
        private IMemoryCache memCache;

        private string SteamApiKey;
        private SteamWebInterfaceFactory steamFactory;

        /// <summary>
        /// SteamUser WebAPI Interface
        /// </summary>
        private SteamUser steamInterface;


        public SteamInterface(IConfiguration configuration, IMemoryCache memCache)
        {
            //this.httpClient = new HttpClient();

            SteamApiKey = configuration["Steam:ApiKey"];

            this.memCache = memCache;
            steamFactory = new SteamWebInterfaceFactory(SteamApiKey);
            steamInterface = steamFactory.CreateSteamWebInterface<SteamUser>(new HttpClient());
        }

        public async Task<PlayerSummaryModel> Users(ulong playerID)
        {
            PlayerSummaryModel data = memCache.Get<PlayerSummaryModel>($"SteamInterface.GetPlayerSummary({playerID})");
            if (data != null)
                return data;

            try
            {
                ISteamWebResponse<PlayerSummaryModel> wrappedData = await steamInterface.GetPlayerSummaryAsync(playerID);
                data = wrappedData.Data;
                if (data == null)
                    return data;
                memCache.Set($"SteamInterface.GetPlayerSummary({playerID})", data, TimeSpan.FromHours(1));
                return data;
            }
            catch { }

            return null;
        }
    }
}
