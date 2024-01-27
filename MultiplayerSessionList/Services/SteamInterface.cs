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
using Steam.Models.GameServers;
using SteamWebAPI2.Models.GameServers;

namespace MultiplayerSessionList.Services
{
    public class SteamInterface
    {
        //private HttpClient httpClient;
        private IMemoryCache memCache;

        private HttpClient httpClient;

        private string SteamApiKey;
        private SteamWebInterfaceFactory steamFactory;

        /// <summary>
        /// SteamUser WebAPI Interface
        /// </summary>
        private SteamUser steamInterface;
        private GameServersService steamGameServersInterface;


        public SteamInterface(IConfiguration configuration, IMemoryCache memCache)
        {
            this.httpClient = new HttpClient();
            
            SteamApiKey = configuration["Steam:ApiKey"];

            this.memCache = memCache;
            steamFactory = new SteamWebInterfaceFactory(SteamApiKey);
            steamInterface = steamFactory.CreateSteamWebInterface<SteamUser>(httpClient);
            steamGameServersInterface = steamFactory.CreateSteamWebInterface<GameServersService>(httpClient);
        }

        public async Task<GetServerResponse[]> GetGames(string filter = null, UInt32? limit = null)
        {
            var response = await steamGameServersInterface.GetServerListAsync(filter, limit);
            return response.Data.Response.Servers;
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

        [Obsolete("Use pre-baked data instead, consider restoring this function in the future")]
        public async Task<string> GetSteamWorkshopName(string workshopId)
        {
            if (string.IsNullOrWhiteSpace(workshopId)) return null;

            string WorkshopName = memCache.Get<string>($"SteamInterface.GetSteamWorkshopName({workshopId})");
            if (WorkshopName != null)
                if (string.IsNullOrWhiteSpace(WorkshopName))
                    return null; // if we stored an empty string then return null
                else
                    return WorkshopName; // if we stored a value then return it

            try
            {
                using (var http = new HttpClient())
                {
                    var reqString = $"http://steamcommunity.com/sharedfiles/filedetails/?id={workshopId}";
                    var rawText = (await http.GetStringAsync(reqString).ConfigureAwait(false));

                    var matches = System.Text.RegularExpressions.Regex.Matches(rawText, "<\\s*div\\s+class\\s*=\\s*\"workshopItemTitle\"\\s*>(.*)<\\s*/\\s*div\\s*>");
                    string found = null;
                    if (matches.Count > 0)
                    {
                        if (matches[0].Groups.Count > 1)
                        {
                            found = matches[0].Groups[1].Value.Trim();
                        }
                    }

                    memCache.Set($"SteamInterface.GetSteamWorkshopName({workshopId})", found ?? string.Empty, TimeSpan.FromHours(24));
                }
            }
            catch { }

            return null;
        }
    }
}
