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

        // Note that null responses from Steam are also cached so hiccups might result in no data for an hour (consider hit-count and retry on null cache items)
        public async Task<WrappedPlayerSummaryModel> Users(ulong playerID)
        {
            WrappedPlayerSummaryModel data = memCache.Get<WrappedPlayerSummaryModel>($"SteamInterface.GetPlayerSummary({playerID})");
            if (data != null)
                if (data.HasNoData)
                    return null;
                else
                    return data;

            try
            {
                ISteamWebResponse<PlayerSummaryModel> wrappedData = await steamInterface.GetPlayerSummaryAsync(playerID);
                if (wrappedData == null)
                {
                    // we only run this ID check logic if nothing came back, so we can only detect known pirates if they have no SteamID data (which I guess if it's a known pirate they won't, but make a note here if issues arrise)
                    switch (playerID)
                    {
                        case 76561197960267366:
                            {
                                var pirate = WrappedPlayerSummaryModel.MakePirate(playerID);
                                memCache.Set($"SteamInterface.GetPlayerSummary({playerID})", data, TimeSpan.FromHours(1));
                                return pirate;
                            }
                    }
                    memCache.Set($"SteamInterface.GetPlayerSummary({playerID})", WrappedPlayerSummaryModel.NoData, TimeSpan.FromHours(1));
                    return null;
                }
                if (wrappedData.Data == null)
                {
                    memCache.Set($"SteamInterface.GetPlayerSummary({playerID})", WrappedPlayerSummaryModel.NoData, TimeSpan.FromHours(1));
                    return null;
                }
                data = new WrappedPlayerSummaryModel(wrappedData.Data);
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
    
        public class WrappedPlayerSummaryModel
        {
            public PlayerSummaryModel Model { get; set; }
            public bool IsPirate { get; set; }
            public bool HasNoData { get; set; }
            public WrappedPlayerSummaryModel() { }
            public WrappedPlayerSummaryModel(PlayerSummaryModel model)
            {
                this.Model = model;
            }

            public static WrappedPlayerSummaryModel MakePirate(ulong playerID)
            {
                return new WrappedPlayerSummaryModel(new PlayerSummaryModel()
                {
                    SteamId = playerID,
                    ProfileVisibility = ProfileVisibility.Unknown,
                    ProfileState = 0,
                    UserStatus = UserStatus.Unknown,
                })
                { IsPirate = true };
            }

            public static readonly WrappedPlayerSummaryModel NoData = new() { HasNoData = true };
        }
    }
}
