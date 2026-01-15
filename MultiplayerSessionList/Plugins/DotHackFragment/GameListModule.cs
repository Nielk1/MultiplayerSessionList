using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
using MultiplayerSessionList.Extensions;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Plugins.Battlezone98Redux;
using MultiplayerSessionList.Plugins.RetroArchNetplay;
using MultiplayerSessionList.Services;
using Newtonsoft.Json.Linq;
using Steam.Models.SteamCommunity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        readonly Regex name_color = new Regex(@"#([WRGBY])([^#]*?)(?=#|$)");

        readonly Dictionary<char, string> ansi16pColors = new Dictionary<char, string>
        {
            { 'W', "\u001b[37m" }, // White
            { 'Y', "\u001b[33m" }, // Yellow
            { 'B', "\u001b[34m" }, // Blue
            { 'G', "\u001b[32m" }, // Green
            { 'R', "\u001b[31m" }, // Red
        };
        readonly Dictionary<char, string> ansi24bColors = new Dictionary<char, string>
        {
            { 'W', "\u001b[38;2;255;255;255m" },   // #fff
            { 'Y', "\u001b[38;2;199;199;0m" },     // #c7c700
            { 'B', "\u001b[38;2;0;0;173m" },       // #0000ad
            { 'G', "\u001b[38;2;2;171;16m" },      // #02ab10
            { 'R', "\u001b[38;2;239;6;6m" },       // #ef0606
        };
        const string ansiReset = "\u001b[0m";

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
            var lobbies = ApplyLobbiesAsync(admin);
            var areaServers = ApplyAreaServerListAsync(admin);

            await foreach (var datum in new List<IAsyncEnumerable<Datum>>(){ lobbies, areaServers }.SelectManyAsync(cancellationToken: cancellationToken))
            {
                yield return datum;
            }
        }

        private async IAsyncEnumerable<Datum> ApplyLobbiesAsync(bool admin)
        {
            CachedData<string>? res = await cachedAdvancedWebClient.GetObject<string>(queryUrl_lobbies, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
            if (res?.Data == null)
                yield break;
            //if (admin)
            //    yield return new Datum("debug", $"{GameID}:dothackers:lobbies", new DataCache() { { "raw", res.Data } });
            Lobby[]? lobbyList = JsonSerializer.Deserialize<Lobby[]>(res.Data);
            if (lobbyList != null)
            {
                {
                    List<DatumRef> lobbies = new List<DatumRef>();
                    foreach (var server in lobbyList)
                    {
                        lobbies.Add(new DatumRef(GAMELIST_TERMS.TYPE_LOBBY, $"{GameID}:dothackers:lobby:{server.Id}"));
                    }
                    Datum root = new Datum(GAMELIST_TERMS.TYPE_ROOT, GameID, new DataCache() {
                        { "lobbies", lobbies },
                    });
                    yield return root;
                }

                foreach (var server in lobbyList)
                {
                    Datum lobby = new Datum(GAMELIST_TERMS.TYPE_LOBBY, $"{GameID}:dothackers:lobby:{server.Id}");

                    lobby[GAMELIST_TERMS.SESSION_TYPE] = GAMELIST_TERMS.SESSION_TYPE_VALUE_DEDICATED;
                    lobby[GAMELIST_TERMS.SESSION_PLAYERTYPES] = LobbyPlayerTypes;

                    lobby[GAMELIST_TERMS.SESSION_NAME] = server.Name;

                    lobby.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:Type", server.Type.ToString());
                    //session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:XXXX", server.players);

                    lobby.AddObjectPath($"{GAMELIST_TERMS.SESSION_PLAYERCOUNT}:{GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER}", server.PlayerCount);

                    yield return lobby;
                }
            }
        }

        private async IAsyncEnumerable<Datum> ApplyAreaServerListAsync(bool admin)
        {
            CachedData<string>? res = await cachedAdvancedWebClient.GetObject<string>(queryUrl_areaservers, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
            if (res?.Data == null)
                yield break;
            if (admin)
                yield return new Datum("debug", $"{GameID}:dothackers:areaservers", new DataCache() { { "raw", res.Data } });
            AreaServer[]? areaServerList = JsonSerializer.Deserialize<AreaServer[]>(res.Data);
            if (areaServerList != null)
            {
                List<DatumRef> sessions = new List<DatumRef>();
                if (sessions != null)
                {
                    foreach (var server in areaServerList)
                    {
                        sessions.Add(new DatumRef(GAMELIST_TERMS.TYPE_SESSION, $"{GameID}:dothackers:area:{server.Name}"));
                    }
                    Datum root = new Datum(GAMELIST_TERMS.TYPE_ROOT, GameID, new DataCache() {
                        { "sessions", sessions },
                    });
                    yield return root;
                }

                foreach (var server in areaServerList)
                {
                    Datum session = new Datum(GAMELIST_TERMS.TYPE_SESSION, $"{GameID}:dothackers:area:{server.Name}");

                    session[GAMELIST_TERMS.SESSION_TYPE] = GAMELIST_TERMS.SESSION_TYPE_VALUE_DEDICATED;
                    session[GAMELIST_TERMS.SESSION_PLAYERTYPES] = AreaServerPlayerTypes;

                    if (server.Name != null)
                    {
                        var matches = name_color.Matches(server.Name);
                        if (matches.Count > 0)
                        {
                            string clean = "";
                            string ansi16p = "";
                            string ansi24b = "";

                            foreach (Match match in matches)
                            {
                                char color = match.Groups[1].Value[0];
                                string text = match.Groups[2].Value;

                                clean += text;
                                if (ansi16pColors.TryGetValue(color, out var ansi16pCode))
                                {
                                    ansi16p += ansi16pCode + text;
                                }
                                else
                                {
                                    ansi16p += text;
                                }
                                if (ansi24bColors.TryGetValue(color, out var ansi24bCode))
                                {
                                    ansi24b += ansi24bCode + text;
                                }
                                else
                                {
                                    ansi24b += text;
                                }
                            }
                            ansi16p += ansiReset;
                            ansi24b += ansiReset;

                            session[GAMELIST_TERMS.SESSION_NAME] = clean;
                            session[$"{GAMELIST_TERMS.SESSION_NAME}.ansi16p"] = ansi16p;
                            session[$"{GAMELIST_TERMS.SESSION_NAME}.ansi24b"] = ansi24b;
                            session[$"{GAMELIST_TERMS.SESSION_NAME}.raw"] = server.Name;
                        }
                        else
                        {
                            session[GAMELIST_TERMS.SESSION_NAME] = server.Name;
                        }
                    }

                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:level", server.Level);

                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:Status", server.Status.ToString());
                    //switch (server.Status)
                    //{
                    //    case AreaServerStatus.Published:
                    //        break;
                    //    default:
                    //        break;
                    //}

                    switch (server.State)
                    {
                        case AreaServerState.Normal:
                            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_PASSWORD}", false);
                            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_LOCKED}", null);
                            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_STATE}", SESSION_STATE.PreMatch); // in root town
                            break;
                        case AreaServerState.Password:
                            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_PASSWORD}", true);
                            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_LOCKED}", null);
                            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_STATE}", SESSION_STATE.PreMatch); // in root town
                            break;
                        case AreaServerState.Playing:
                            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_PASSWORD}", null);
                            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_LOCKED}", true);
                            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_STATE}", SESSION_STATE.InGame); // in area (field or dungeon) (all players are forced to area)
                            break;
                    }

                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_PLAYERCOUNT}:{GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER}", server.CurrentPlayerCount);

                    yield return session;
                }
            }
        }
    }
}
