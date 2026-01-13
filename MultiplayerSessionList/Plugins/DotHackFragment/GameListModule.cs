using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Extensions;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Plugins.Battlezone98Redux;
using MultiplayerSessionList.Plugins.RetroArchNetplay;
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
    // {W: '#fff', Y: '#c7c700', B: '#0000ad', G: '#02ab10', R: '#ef0606'}
    // /#([WRGBY])([^#]*?)(?=#|$)/g

    [GameListModule(GameID, ".hack//frägment", true)]
    public class GameListModule : IGameListModule
    {
        public const string GameID = "cyberconnect2:dothack_fragment";

        readonly List<DataCache> LobbyPlayerTypes =
        [
            new DataCache()
                {
                    { GAMELIST_TERMS.PLAYERTYPE_TYPES, new List<string>() { GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER } },
                },
            ];

        readonly List<DataCache> AreaServerPlayerTypes =
        [
            new DataCache()
                {
                    { GAMELIST_TERMS.PLAYERTYPE_TYPES, new List<string>() { GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER } },
                    { GAMELIST_TERMS.PLAYERTYPE_MAX, 3 },
                },
            ];

        private string queryUrl_lobbies;
        private string queryUrl_areaservers;
        private CachedAdvancedWebClient cachedAdvancedWebClient;

        public GameListModule(IConfiguration configuration, CachedAdvancedWebClient cachedAdvancedWebClient)
        {
            string? queryUrl_lobbies = configuration[$"{GameID}:lobbies"];
            if (string.IsNullOrWhiteSpace(queryUrl_lobbies))
                throw new InvalidOperationException($"Critical configuration value for '{GameID}:lobbies' is missing or empty.");
            this.queryUrl_lobbies = queryUrl_lobbies;

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
            Datum root = new Datum(GAMELIST_TERMS.TYPE_ROOT, GameID, new DataCache() {
                { "sessions", new HashSet<DatumRef>() },
            });

            var lobbies = ApplyLobbiesAsync(admin, root);
            var areaServers = ApplyAreaServerListAsync(admin, root);

            await foreach (var datum in new List<IAsyncEnumerable<Datum>>(){ lobbies, areaServers }.SelectManyAsync(cancellationToken: cancellationToken))
            {
                yield return datum;
            }
        }

        private async IAsyncEnumerable<Datum> ApplyLobbiesAsync(bool admin, Datum root)
        {
            CachedData<string>? res = await cachedAdvancedWebClient.GetObject<string>(queryUrl_lobbies, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
            if (res?.Data == null)
                yield break;
            if (admin)
                yield return new Datum("debug", $"{GameID}:dothackers:lobbies", new DataCache() { { "raw", res.Data } });
            Lobby[]? areaServerList = JsonConvert.DeserializeObject<Lobby[]>(res.Data);
            if (areaServerList != null)
            {
                HashSet<DatumRef>? sessions = root["sessions"] as HashSet<DatumRef>;
                if (sessions != null)
                {
                    foreach (var server in areaServerList)
                    {
                        lock (sessions)
                        {
                            sessions.Add(new DatumRef(GAMELIST_TERMS.TYPE_SESSION, $"{GameID}:dothackers:lobby:{server.id}"));
                        }
                    }
                    yield return root;
                }

                foreach (var server in areaServerList)
                {
                    Datum session = new Datum(GAMELIST_TERMS.TYPE_SESSION, $"{GameID}:dothackers:lobby:{server.id}");

                    session[GAMELIST_TERMS.SESSION_TYPE] = GAMELIST_TERMS.SESSION_TYPE_VALUE_DEDICATED;
                    session[GAMELIST_TERMS.SESSION_PLAYERTYPES] = LobbyPlayerTypes;

                    session[GAMELIST_TERMS.SESSION_NAME] = server.name;

                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:Type", server.type);
                    //session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:XXXX", server.players);

                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_PLAYERCOUNT}:{GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER}", server.playerCount);

                    yield return session;
                }
            }
        }

        private async IAsyncEnumerable<Datum> ApplyAreaServerListAsync(bool admin, Datum root)
        {
            CachedData<string>? res = await cachedAdvancedWebClient.GetObject<string>(queryUrl_areaservers, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
            if (res?.Data == null)
                yield break;
            if (admin)
                yield return new Datum("debug", $"{GameID}:dothackers:areaservers", new DataCache() { { "raw", res.Data } });
            AreaServer[]? areaServerList = JsonConvert.DeserializeObject<AreaServer[]>(res.Data);
            if (areaServerList != null)
            {
                HashSet<DatumRef>? sessions = root["sessions"] as HashSet<DatumRef>;
                if (sessions != null)
                {
                    foreach (var server in areaServerList)
                    {
                        lock (sessions)
                        {
                            sessions.Add(new DatumRef(GAMELIST_TERMS.TYPE_SESSION, $"{GameID}:dothackers:area:{server.Name}"));
                        }
                    }
                    yield return root;
                }

                foreach (var server in areaServerList)
                {
                    Datum session = new Datum(GAMELIST_TERMS.TYPE_SESSION, $"{GameID}:dothackers:area:{server.Name}");

                    session[GAMELIST_TERMS.SESSION_TYPE] = GAMELIST_TERMS.SESSION_TYPE_VALUE_DEDICATED;
                    session[GAMELIST_TERMS.SESSION_PLAYERTYPES] = AreaServerPlayerTypes;

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
