using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Steam.Models.SteamCommunity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.DotHackFragment
{
    [GameListModule(GameID, ".hack//frägment", true)]
    public class GameListModule : IGameListModule
    {
        public const string GameID = "cyberconnect2:dothack_fragment";

        private string queryUrl_areaservers;
        private CachedAdvancedWebClient cachedAdvancedWebClient;

        public GameListModule(IConfiguration configuration, CachedAdvancedWebClient cachedAdvancedWebClient)
        {
            string? queryUrl_areaservers = configuration[$"{GameID}:areaservers"];
            if (string.IsNullOrWhiteSpace(queryUrl_areaservers))
                throw new InvalidOperationException($"Critical configuration value for '{GameID}:areaservers' is missing or empty.");
            this.queryUrl_areaservers = queryUrl_areaservers;

            //IConfigurationSection myArraySection = configuration.GetSection("MyArray");
            //var itemArray = myArraySection.AsEnumerable();

            //"Clients": [ {..}, {..} ]
            //configuration.GetSection("Clients").GetChildren();

            //"Clients": [ "", "", "" ]
            //configuration.GetSection("Clients").GetChildren().ToArray().Select(c => c.Value).ToArray();

            this.cachedAdvancedWebClient = cachedAdvancedWebClient;
        }

        public async IAsyncEnumerable<Datum> GetGameListChunksAsync(bool admin, bool mock, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CachedData<string> res = await cachedAdvancedWebClient.GetObject<string>(queryUrl_areaservers, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
            AreaServer[]? areaServerList = JsonConvert.DeserializeObject<AreaServer[]>(res.Data);

            List<DataCache> PlayerTypes =
            [
                new DataCache()
                {
                    { GAMELIST_TERMS.PLAYERTYPE_TYPES, new List<string>() { GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER } },
                    { GAMELIST_TERMS.PLAYERTYPE_MAX, 3 },
                },
            ];

            if (admin)
                yield return new Datum("debug", "raw", new DataCache() { { "areaservers", res.Data } });

            if (areaServerList != null)
            {
                foreach (var server in areaServerList)
                {
                    Datum session = new Datum(GAMELIST_TERMS.TYPE_SESSION, $"{GameID}:dothackers:{server.Name}");

                    session[GAMELIST_TERMS.SESSION_TYPE] = GAMELIST_TERMS.SESSION_TYPE_VALUE_DEDICATED;
                    session[GAMELIST_TERMS.SESSION_PLAYERTYPES] = PlayerTypes;

                    session[GAMELIST_TERMS.SESSION_NAME] = server.Name;

                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:Level", server.Level);
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:Status", server.Status);

                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_PLAYERCOUNT}:{GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER}", server.CurrentPlayerCount);

                    yield return session;
                }
            }
        }
    }
}
