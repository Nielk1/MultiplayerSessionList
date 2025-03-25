using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.CompilerServices;

namespace MultiplayerSessionList.Plugins.Battlezone98Redux
{
    [GameListModule(GameID, "Battlezone 98 Redux", true)]
    public class GameListModule : IGameListModule
    {
        private const string GameID = "bigboat:battlezone_98_redux";
        //public string Title => "Battlezone 98 Redux";
        //public bool IsPublic => true;

        private string queryUrl;
        private string mapUrl;
        private GogInterface gogInterface;
        private SteamInterface steamInterface;
        private CachedAdvancedWebClient cachedAdvancedWebClient;

        public GameListModule(IConfiguration configuration, GogInterface gogInterface, SteamInterface steamInterface, CachedAdvancedWebClient cachedAdvancedWebClient)
        {
            queryUrl = configuration[$"{GameID}:sessions"];
            mapUrl = configuration[$"{GameID}:maps"];
            this.gogInterface = gogInterface;
            this.steamInterface = steamInterface;
            this.cachedAdvancedWebClient = cachedAdvancedWebClient;
        }

        public async IAsyncEnumerable<Datum> GetGameListChunksAsync(bool multiGame, bool admin, bool mock, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var fact_task = cachedAdvancedWebClient.GetObject<Dictionary<string, DataCache>>($"{mapUrl.TrimEnd('/')}/factions.json", TimeSpan.FromHours(24), TimeSpan.FromHours(1));

            // need protection on gamelist being null somehow
            var res_raw = await cachedAdvancedWebClient.GetObject<string>(queryUrl, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
            var res = res_raw.Data;
            var gamelist = JsonConvert.DeserializeObject<Dictionary<string, Lobby>>(res);

#if DEBUG
            // any games that don't include CreepingDeath, because he loves to sit in games forever
            mock = mock || !gamelist.Where(dr => (dr.Value?.LobbyType ?? Lobby.ELobbyType.Unknown) == Lobby.ELobbyType.Game && (dr.Value?.users?.Values?.Any(dx => dx.id != "S76561199054029199") ?? false)).Any();
#endif
            if (mock)
                gamelist = JsonConvert.DeserializeObject<Dictionary<string, Lobby>>(System.IO.File.ReadAllText(@"mock\bigboat\battlezone_98_redux.json"));

            //TODO consider using memberlink for how many players can be in a game, as the text data above (before editing) has a 3/2 if its not used but 3/3 if it is.

            TaskFactory taskFactory = new TaskFactory(cancellationToken);

            yield return new Datum(GAMELIST_TERMS.TYPE_SOURCE, $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion", new DataCache() {
                { GAMELIST_TERMS.SOURCE_NAME, "Rebellion" },
                { "timestamp", res_raw.LastModified },
            });

            if (!multiGame)
                yield return new Datum(GAMELIST_TERMS.TYPE_DEFAULT, GAMELIST_TERMS.TYPE_SESSION, new DataCache() {
                    { GAMELIST_TERMS.SESSION_TYPE, GAMELIST_TERMS.SESSION_TYPE_VALUE_LISTEN },
                    { GAMELIST_TERMS.SESSION_SOURCES, new DataCache() { {"Rebellion", new DatumRef(GAMELIST_TERMS.TYPE_SOURCE, $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion") } } },
                });

            HashSet<string> DontSendStub = new HashSet<string>();

            Dictionary<(string mod, string map), Task<MapData>> MapDataFetchTasks = new Dictionary<(string mod, string map), Task<MapData>>();
            List<Task<List<PendingDatum>>> DelayedDatumTasks = new List<Task<List<PendingDatum>>>();

            SemaphoreSlim heroesAlreadyReturnedLock = new SemaphoreSlim(1, 1);
            HashSet<string> heroesAlreadyReturnedFull = new HashSet<string>();

            // factions are locked inside of a hero lock so it's probably redundant and possibly a risk to have this
            SemaphoreSlim factionsAlreadyReturnedLock = new SemaphoreSlim(1, 1);
            HashSet<string> factionsAlreadyReturnedFull = new HashSet<string>();

            SemaphoreSlim modsAlreadyReturnedLock = new SemaphoreSlim(1, 1);
            HashSet<string> modsAlreadyReturnedFull = new HashSet<string>();

            SemaphoreSlim gametypeFullAlreadySentLock = new SemaphoreSlim(1, 1);
            HashSet<string> gametypeFullAlreadySent = new HashSet<string>();
            SemaphoreSlim gamemodeFullAlreadySentLock = new SemaphoreSlim(1, 1);
            HashSet<string> gamemodeFullAlreadySent = new HashSet<string>();
            SemaphoreSlim gamebalanceFullAlreadySentLock = new SemaphoreSlim(1, 1);
            HashSet<string> gamebalanceFullAlreadySent = new HashSet<string>();

            foreach (var raw in gamelist.Values)
            {
                if (raw.LobbyType != Lobby.ELobbyType.Game)
                    continue;

                if (raw.isPrivate && !(raw.IsPassworded ?? false))
                    continue;

                Datum session = new Datum(GAMELIST_TERMS.TYPE_SESSION, $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion:B{raw.id}");

                if (multiGame) {
                    session[GAMELIST_TERMS.SESSION_TYPE] = GAMELIST_TERMS.SESSION_TYPE_VALUE_LISTEN;
                    session[GAMELIST_TERMS.SESSION_SOURCES] = new DataCache() { { $"Rebellion", new DatumRef(GAMELIST_TERMS.TYPE_SOURCE, $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion") } };
                }

                session[GAMELIST_TERMS.SESSION_NAME] = raw.Name;

                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_TOKEN}", $"B{raw.id}");
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_OTHER}:lobby_id",raw.id);

                List<DataCache> PlayerTypes =
                [
                    new DataCache()
                    {
                        { GAMELIST_TERMS.PLAYERTYPE_TYPES, new List<string>() { GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER } },
                        { GAMELIST_TERMS.PLAYERTYPE_MAX, raw.PlayerLimit },
                    },
                ];
                session[GAMELIST_TERMS.SESSION_PLAYERTYPES] = PlayerTypes;

                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_PLAYERCOUNT}:{GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER}", raw.userCount);

                string modID = (raw.WorkshopID ?? @"0");

                if (modID != "0")
                {
                    if (DontSendStub.Add($"{GAMELIST_TERMS.TYPE_MOD}\t{modID}"))
                    {
                        yield return new Datum(GAMELIST_TERMS.TYPE_MOD, $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}");
                    }
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_MOD}", new DatumRef(GAMELIST_TERMS.TYPE_MOD, $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}"));
                }

                if (modID == "0")
                {
                    // we aren't concurrent yet so we're safe to just do this
                    if (DontSendStub.Add($"{GAMELIST_TERMS.TYPE_GAMEBALANCE}\tSTOCK"))
                        yield return new Datum(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{(multiGame ? $"{GameID}:" : string.Empty)}STOCK", new DataCache() { { GAMELIST_TERMS.GAMEBALANCE_NAME, "Stock" } });
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_GAMEBALANCE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{(multiGame ? $"{GameID}:" : string.Empty)}STOCK"));
                }

                string mapID = System.IO.Path.GetFileNameWithoutExtension(raw.MapFile).ToLowerInvariant();

                // TODO this map stub datum doesn't need to be emitted if another prior session already emitted it
                Datum mapData = new Datum(GAMELIST_TERMS.TYPE_MAP, $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}");
                mapData[GAMELIST_TERMS.MAP_MAPFILE] = raw.MapFile.ToLowerInvariant();
                yield return mapData;
                DontSendStub.Add($"{GAMELIST_TERMS.TYPE_MAP}\t{modID}:{mapID}"); // we already sent the a stub don't send another

                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_MAP}", new DatumRef(GAMELIST_TERMS.TYPE_MAP, $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}"));
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_OTHER}:crc32", raw.CRC32);

                if (raw.TimeLimit.HasValue && raw.TimeLimit > 0) session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:time_limit", raw.TimeLimit);
                if (raw.KillLimit.HasValue && raw.KillLimit > 0) session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:kill_limit", raw.KillLimit);
                if (raw.Lives.HasValue && raw.Lives.Value > 0) session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:lives", raw.Lives.Value);

                // ProducerClass removes build items of class CLASS_COMMTOWER
                if (raw.SatelliteEnabled.HasValue) session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:satellite", raw.SatelliteEnabled.Value);

                // ProducerClass removes build items of class CLASS_BARRACKS
                if (raw.BarracksEnabled.HasValue) session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:barracks", raw.BarracksEnabled.Value);

                // GameObjectClass removes weapons of signiture "SNIP"
                if (raw.SniperEnabled.HasValue) session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:sniper", raw.SniperEnabled.Value);

                // ArmoryClass removes mortar list entries of class CLASS_POWERUP_WEAPON with PrjID "apspln" and "spspln"
                // If not a DeathMatch (set by map script class internally) remove weapons with PrjID "gsplint"
                if (raw.SplinterEnabled.HasValue) session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:splinter", raw.SplinterEnabled.Value);

                // unlocked in progress games with SyncJoin will trap the user due to a bug, just list as locked
                if (!raw.isLocked && raw.SyncJoin.HasValue && raw.SyncJoin.Value && (!raw.IsEnded && raw.IsLaunched))
                {
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_LOCKED}", true);
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_OTHER}:sync_too_late", true);
                }
                else
                {
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_LOCKED}", raw.isLocked);
                }
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_PASSWORD}", raw.IsPassworded);
                
                string ServerState = raw.IsEnded ? SESSION_STATE.PostGame : raw.IsLaunched ? SESSION_STATE.InGame : SESSION_STATE.PreGame;
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_STATE}", ServerState);

                List<DatumRef> Players = new List<DatumRef>();
                foreach (var dr in raw.users.Values)
                {
                    Datum player = new Datum(GAMELIST_TERMS.TYPE_PLAYER, $"{(multiGame ? $"{GameID}:" : string.Empty)}{dr.id}");

                    player[GAMELIST_TERMS.PLAYER_NAME] = dr.name;
                    player[GAMELIST_TERMS.PLAYER_TYPE] = GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER;
                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:launched", dr.Launched);
                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:is_auth", dr.isAuth);
                    if (admin)
                    {
                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:wan_address", dr.wanAddress);
                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:lan_addresses", JArray.FromObject(dr.lanAddresses));
                    }
                    if (dr.CommunityPatch != null)
                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:community_patch", dr.CommunityPatch);
                    if (dr.CommunityPatchShim != null)
                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:community_patch_shim", dr.CommunityPatchShim);

                    if (dr.Team.HasValue)
                    {
                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_IDS}:slot:{GAMELIST_TERMS.PLAYER_IDS_X_ID}", dr.Team);
                        player.AddObjectPath(GAMELIST_TERMS.PLAYER_INDEX, dr.Team);
                    }

                    if (!string.IsNullOrWhiteSpace(dr.id))
                    {
                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_IDS}:bzr_net:{GAMELIST_TERMS.PLAYER_IDS_X_ID}", dr.id);
                        if (dr.id == raw.owner)
                            player[GAMELIST_TERMS.PLAYER_ISHOST] = true;
                        switch (dr.id[0])
                        {
                            case 'S': // dr.authType == "steam"
                                {
                                    ulong playerID = 0;
                                    if (ulong.TryParse(dr.id.Substring(1), out playerID))
                                    {
                                        yield return new Datum(GAMELIST_TERMS.TYPE_IDENTITYSTEAM, playerID.ToString(), new DataCache()
                                        {
                                            { GAMELIST_TERMS.PLAYER_IDS_X_TYPE, "steam" },
                                        });
                                        DontSendStub.Add($"{GAMELIST_TERMS.TYPE_IDENTITYSTEAM}\t{playerID.ToString()}"); // we already sent the a stub don't send another

                                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_IDS}:steam", new DataCache() {
                                            { GAMELIST_TERMS.PLAYER_IDS_X_ID, playerID.ToString() },
                                            { GAMELIST_TERMS.PLAYER_IDS_X_RAW, dr.id.Substring(1) },
                                            { GAMELIST_TERMS.PLAYER_IDS_X_IDENTITY, new DatumRef(GAMELIST_TERMS.TYPE_IDENTITYSTEAM, playerID.ToString()) },
                                        });

                                        DelayedDatumTasks.Add(steamInterface.GetPendingDataAsync(playerID));
                                    }
                                }
                                break;
                            case 'G':
                                {
                                    ulong playerID = 0;
                                    if (ulong.TryParse(dr.id.Substring(1), out playerID))
                                    {
                                        playerID = GogInterface.CleanGalaxyUserId(playerID);

                                        yield return new Datum(GAMELIST_TERMS.TYPE_IDENTITYGOG, playerID.ToString(), new DataCache()
                                        {
                                            { GAMELIST_TERMS.PLAYER_IDS_X_TYPE, "gog" },
                                        });
                                        DontSendStub.Add($"{GAMELIST_TERMS.TYPE_IDENTITYGOG}\t{playerID.ToString()}"); // we already sent the a stub don't send another

                                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_IDS}:gog", new DataCache() {
                                            { GAMELIST_TERMS.PLAYER_IDS_X_ID, playerID.ToString() },
                                            { GAMELIST_TERMS.PLAYER_IDS_X_RAW, dr.id.Substring(1) },
                                            { GAMELIST_TERMS.PLAYER_IDS_X_IDENTITY, new DatumRef(GAMELIST_TERMS.TYPE_IDENTITYGOG, playerID.ToString()) },
                                        });

                                        DelayedDatumTasks.Add(gogInterface.GetPendingDataAsync(playerID));
                                    }
                                }
                                break;
                        }
                    }

                    yield return player;

                    Players.Add(new DatumRef(GAMELIST_TERMS.TYPE_PLAYER, $"{(multiGame ? $"{GameID}:" : string.Empty)}{dr.id}"));
                }
                session[GAMELIST_TERMS.SESSION_PLAYERS] = Players;

                if (!MapDataFetchTasks.ContainsKey((modID, mapID)))
                    DelayedDatumTasks.Add(BuildDatumsForMapDataAsync(modID, mapID, raw, multiGame,
                        modsAlreadyReturnedLock, modsAlreadyReturnedFull,
                        gametypeFullAlreadySentLock, gametypeFullAlreadySent,
                        gamemodeFullAlreadySentLock, gamemodeFullAlreadySent,
                        heroesAlreadyReturnedLock, heroesAlreadyReturnedFull,
                        factionsAlreadyReturnedLock, factionsAlreadyReturnedFull, fact_task,
                        gamebalanceFullAlreadySentLock, gamebalanceFullAlreadySent));
                        //playerCacheLock, playerCache));

                if (!string.IsNullOrWhiteSpace(raw.clientVersion))
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_VERSION}", raw.clientVersion);
                else if (!string.IsNullOrWhiteSpace(raw.GameVersion))
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_VERSION}", raw.GameVersion);

                if (raw.SyncJoin.HasValue)
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:sync_join", raw.SyncJoin.Value);
                if (raw.MetaDataVersion.HasValue)
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:meta_data_version", raw.MetaDataVersion);

                yield return session;
            }

            while (DelayedDatumTasks.Any())
            {
                Task<List<PendingDatum>> doneTask = await Task.WhenAny(DelayedDatumTasks);
                List<PendingDatum> datums = doneTask.Result;
                if (datums != null)
                {
                    foreach (var datum in doneTask.Result)
                    {
                        // don't send datums if we already sent the big guy
                        if (datum.key != null)
                            if (datum.stub)
                                if (DontSendStub.Contains(datum.key))
                                    continue;
                        yield return datum.data;
                        DontSendStub.Add(datum.key);
                    }
                }
                DelayedDatumTasks.Remove(doneTask);
            }

            yield break;
        }

        private async Task<List<PendingDatum>> BuildDatumsForMapDataAsync(string modID, string mapID, Lobby session, bool multiGame,
            SemaphoreSlim modsAlreadyReturnedLock, HashSet<string> modsAlreadyReturnedFull,
            SemaphoreSlim gametypeFullAlreadySentLock, HashSet<string> gametypeFullAlreadySent,
            SemaphoreSlim gamemodeFullAlreadySentLock, HashSet<string> gamemodeFullAlreadySent,
            SemaphoreSlim heroesAlreadyReturnedLock, HashSet<string> heroesAlreadyReturnedFull,
            SemaphoreSlim factionsAlreadyReturnedLock, HashSet<string> factionsAlreadyReturnedFull, Task<CachedData<Dictionary<string, DataCache>>> fact_task,
            SemaphoreSlim gamebalanceFullAlreadySentLock, HashSet<string> gamebalanceFullAlreadySent)
            //SemaphoreSlim playerCacheLock, Dictionary<string, Tuple<int>> playerCache)
        {
            List<PendingDatum> retVal = new List<PendingDatum>();
            CachedData<MapData> mapDataC = await cachedAdvancedWebClient.GetObject<MapData>($"{mapUrl.TrimEnd('/')}/getdata2.php?map={mapID}&mods={modID}");
            MapData mapData = mapDataC?.Data;
            if (mapData != null)
            {
                Datum mapDatum = new Datum(GAMELIST_TERMS.TYPE_MAP, $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}", new DataCache() {
                    { GAMELIST_TERMS.MAP_NAME, mapData?.map?.title },
                });
                if (mapData.map?.image != null)
                    mapDatum[GAMELIST_TERMS.MAP_IMAGE] = $"{mapUrl.TrimEnd('/')}/{mapData.map.image}";

                //mapDatum.AddObjectPath($"{GAMELIST_TERMS.MAP_GAMETYPE}:id", mapData?.map?.type); // this might be broken here
                string mapType = mapData?.map?.bzcp_type_fix ?? mapData?.map?.bzcp_auto_type_fix ?? mapData?.map?.type;
                string mapMode = mapData?.map?.bzcp_type_override ?? mapData?.map?.bzcp_auto_type_override ?? mapType;
                if (!string.IsNullOrWhiteSpace(mapType))
                {
                    switch (mapType)
                    {
                        case "D": // Deathmatch
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                            retVal.Add(await BuildGameTypeDatumAsync("DM", "Deathmatch", $"{mapUrl.TrimEnd('/')}/resources/icon_d.png", "#B70505", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                            break;
                        case "S": // Strategy
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                            retVal.Add(await BuildGameTypeDatumAsync("STRAT", "Strategy", $"{mapUrl.TrimEnd('/')}/resources/icon_s.png", "#007FFF", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                            break;
                        case "K": // King of the Hill
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                            retVal.Add(await BuildGameTypeDatumAsync("DM", "Deathmatch", $"{mapUrl.TrimEnd('/')}/resources/icon_d.png", "#B70505", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                            break;
                        case "M": // Mission MPI
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                            retVal.Add(await BuildGameTypeDatumAsync("STRAT", "Strategy", $"{mapUrl.TrimEnd('/')}/resources/icon_s.png", "#007FFF", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                            break;
                        case "A": // Action MPI
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                            retVal.Add(await BuildGameTypeDatumAsync("DM", "Deathmatch", $"{mapUrl.TrimEnd('/')}/resources/icon_d.png", "#B70505", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                            break;
                        case "X": // Other
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{(multiGame ? $"{GameID}:" : string.Empty)}OTHER"));
                            retVal.Add(await BuildGameTypeDatumAsync("OTHER", "Other", $"{mapUrl.TrimEnd('/')}/resources/icon_x.png", "#666666", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                            break;
                    }
                }
                string MapModeIcon = null;
                string MapModeColorA = null;
                string MapModeColorB = null;
                if (!string.IsNullOrWhiteSpace(mapMode))
                {
                    switch (mapMode)
                    {
                        case "A": // Action MPI
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}A_MPI"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_a.png";
                            MapModeColorA = "#002C00";
                            MapModeColorB = "#007C03";
                            retVal.Add(await BuildGameModeDatumAsync("A_MPI", "Action MPI", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "C": // Custom
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}CUSTOM"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_c.png";
                            MapModeColorA = "#FFFF00";
                            MapModeColorB = "#FFFF00";
                            retVal.Add(await BuildGameModeDatumAsync("CUSTOM", "Custom", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "D": // Deathmatch
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_d.png";
                            MapModeColorA = "#B70505";
                            MapModeColorB = "#E90707";
                            retVal.Add(await BuildGameModeDatumAsync("DM", "Deathmatch", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "F": // Capture the Flag
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}CTF"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_f.png";
                            MapModeColorA = "#7F5422";
                            MapModeColorB = "#B0875E";
                            retVal.Add(await BuildGameModeDatumAsync("CTF", "Capture the Flag", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "G": // Race
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}RACE"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_g.png";
                            MapModeColorA = "#1A1A1A";
                            MapModeColorB = "#EEEEEE";
                            retVal.Add(await BuildGameModeDatumAsync("RACE", "Race", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "K": // King of the Hill
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}KOTH"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_k.png";
                            MapModeColorA = "#F0772D";
                            MapModeColorB = "#F0772D";
                            retVal.Add(await BuildGameModeDatumAsync("KOTH", "King of the Hill", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "L": // Loot
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}LOOT"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_l.png";
                            MapModeColorA = "#333333";
                            MapModeColorB = "#BFA88F";
                            retVal.Add(await BuildGameModeDatumAsync("LOOT", "Loot", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "M": // Mission MPI
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}M_MPI"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_m.png";
                            MapModeColorA = "#B932FF";
                            MapModeColorB = "#B932FF";
                            retVal.Add(await BuildGameModeDatumAsync("M_MPI", "Mission MPI", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "P": // Pilot/Sniper Deathmatch
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}PILOT"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_p.png";
                            MapModeColorA = "#7A0000";
                            MapModeColorB = "#B70606";
                            retVal.Add(await BuildGameModeDatumAsync("PILOT", "Pilot Deathmatch", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "Q": // Squad Deathmatch
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}SQUAD"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_q.png";
                            MapModeColorA = "#FF3F00";
                            MapModeColorB = "#FF3F00";
                            retVal.Add(await BuildGameModeDatumAsync("SQUAD", "Squad Deathmatch", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "R": // Capture the Relic
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}RELIC"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_r.png";
                            MapModeColorA = "#7D007D";
                            MapModeColorB = "#7D007D";
                            retVal.Add(await BuildGameModeDatumAsync("RELIC", "Capture the Relic", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "S": // Strategy
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_s.png";
                            MapModeColorA = "#007FFF";
                            MapModeColorB = "#007FFF";
                            retVal.Add(await BuildGameModeDatumAsync("STRAT", "Strategy", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "W": // Wingman
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}WINGMAN"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_w.png";
                            MapModeColorA = "#0047CF";
                            MapModeColorB = "#0047CF";
                            retVal.Add(await BuildGameModeDatumAsync("WINGMAN", "Wingman Strategy", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "X": // Other
                            mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}OTHER"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_x.png";
                            MapModeColorA = "#666666";
                            MapModeColorB = "#C3C3C3";
                            retVal.Add(await BuildGameModeDatumAsync("OTHER", "Other", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                    }
                }
                if (!string.IsNullOrWhiteSpace(mapData?.map?.custom_type))
                {
                    mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}CUST_{mapData.map.custom_type}"));
                    retVal.Add(await BuildGameModeDatumAsync($"CUST_{mapData.map.custom_type}", mapData.map.custom_type_name, MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                }

                if (mapData.map?.flags?.Contains("sbp") ?? false)
                {
                    mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEBALANCE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{(multiGame ? $"{GameID}:" : string.Empty)}CUST_SBP"));
                    retVal.Add(await BuildGameBalanceDatumAsync($"CUST_SBP", "Strat Balance Patch", "SBP", "This session uses a mod balance paradigm called \"Strat Balance Patch\" which significantly changes game balance.", multiGame, gamebalanceFullAlreadySentLock, gamebalanceFullAlreadySent));
                } else if (mapData.map?.flags?.Contains("balance_stock") ?? false)
                {
                    mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEBALANCE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{(multiGame ? $"{GameID}:" : string.Empty)}STOCK"));
                    retVal.Add(await BuildGameBalanceDatumAsync($"STOCK", "Stock", null, null, multiGame, gamebalanceFullAlreadySentLock, gamebalanceFullAlreadySent));
                }

                if (mapData.map?.flags?.Contains("sbp_auto_ally_teams") ?? false)
                {
                    mapDatum.AddObjectPath($"{GAMELIST_TERMS.MAP_TEAMS}:1:{GAMELIST_TERMS.MAP_TEAMS_X_NAME}", "Odds");
                    mapDatum.AddObjectPath($"{GAMELIST_TERMS.MAP_TEAMS}:2:{GAMELIST_TERMS.MAP_TEAMS_X_NAME}", "Evens");
                }

                // we don't bother linking these mods to the map since they came from the session.game, not the map, their data just came in piggybacking on the map data
                if (mapData?.mods != null && mapData.mods.Count > 0)
                {
                    foreach (var mod in mapData.mods)
                    {
                        // skip stock
                        if (mod.Key == "0")
                            continue;

                        await modsAlreadyReturnedLock.WaitAsync();
                        try
                        {
                            if (!modsAlreadyReturnedFull.Contains(mod.Key))
                            {
                                Datum modData = new Datum(GAMELIST_TERMS.TYPE_MOD, $"{(multiGame ? $"{GameID}:" : string.Empty)}{mod.Key}", new DataCache() {
                                    { GAMELIST_TERMS.MOD_NAME, mod.Value?.name ?? mod.Value?.workshop_name },
                                });

                                if (mod.Value?.image != null)
                                    modData.Data[GAMELIST_TERMS.MOD_IMAGE] = $"{mapUrl.TrimEnd('/')}/{mod.Value.image}";

                                if (UInt64.TryParse(mod.Key, out UInt64 modId) && modId > 0)
                                    modData.Data[GAMELIST_TERMS.MOD_URL] = $"http://steamcommunity.com/sharedfiles/filedetails/?id={mod.Key}";

                                if (mod.Value?.dependencies != null && mod.Value.dependencies.Count > 0)
                                {
                                    // just spam out stubs for dependencies, they're a mess anyway, the reducer at the end will reduce it
                                    foreach (var dep in mod.Value.dependencies)
                                        retVal.Add(new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_MOD, $"{(multiGame ? $"{GameID}:" : string.Empty)}{dep}"), $"{GAMELIST_TERMS.TYPE_MOD}\t{dep}", true));
                                    modData.AddObjectPath(GAMELIST_TERMS.MOD_DEPENDENCIES, mod.Value.dependencies.Select(dep => new DatumRef(GAMELIST_TERMS.TYPE_MOD, $"{(multiGame ? $"{GameID}:" : string.Empty)}{dep}")));
                                }

                                retVal.Add(new PendingDatum(modData, $"{GAMELIST_TERMS.TYPE_MOD}\t{mod.Key}", false));

                                modsAlreadyReturnedFull.Add(mod.Key);
                            }
                            else
                            {
                                // to deal with interlacing spit out some stubs too
                                retVal.Add(new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_MOD, $"{(multiGame ? $"{GameID}:" : string.Empty)}{mod.Key}"), $"{GAMELIST_TERMS.TYPE_MOD}\t{mod.Key}", true));
                            }
                        }
                        finally
                        {
                            modsAlreadyReturnedLock.Release();
                        }
                    }

                    //int ModsLen = (modDatumList?.Count ?? 0);
                    ////if (ModsLen > 0 && modDatumList.First() != "0")
                    //if (ModsLen > 0) // TODO missing 0 check for stock, but maybe we should always list stock?
                    //    mapDatum.AddObjectPath("mod", modDatumList[0]);
                    //if (ModsLen > 1)
                    //    mapDatum.AddObjectPath("mods", modDatumList.Skip(1));
                }

                //List<DatumRef> heroDatumList = new List<DatumRef>();
                if (mapData?.vehicles != null)
                {
                    foreach (var vehicle in mapData.vehicles)
                    {
                        // breakpoint here to make sure the vehicle has the mod prefix
                        //heroDatumList.Add(new DatumRef("hero", $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle.Key}"));

                        await heroesAlreadyReturnedLock.WaitAsync();
                        try
                        {
                            if (!heroesAlreadyReturnedFull.Contains(vehicle.Key))
                            {
                                Datum heroData = new Datum(GAMELIST_TERMS.TYPE_HERO, $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle.Key}", new DataCache() {
                                    { GAMELIST_TERMS.HERO_NAME, vehicle.Value.name },
                                });

                                {
                                    string faction = vehicle.Value.faction;
                                    if (faction != null && faction.Length > 0)
                                    {
                                        var factionDataX = await fact_task;
                                        var factionData = factionDataX?.Data;
                                        if (factionData != null && factionData.ContainsKey(faction))
                                        {
                                            heroData[GAMELIST_TERMS.HERO_FACTION] = new DatumRef(GAMELIST_TERMS.TYPE_FACTION, $"{(multiGame ? $"{GameID}:" : string.Empty)}{faction}");

                                            await factionsAlreadyReturnedLock.WaitAsync();
                                            try
                                            {
                                                if (factionsAlreadyReturnedFull.Add(faction))
                                                {
                                                    var fd = factionData[faction];
                                                    var fact = new DataCache();
                                                    fact[GAMELIST_TERMS.FACTION_NAME] = fd["name"];
                                                    if (fd.ContainsKey("abbr"))
                                                        fact[GAMELIST_TERMS.FACTION_ABBR] = fd["abbr"];
                                                    if (fd.ContainsKey("block"))
                                                        fact[GAMELIST_TERMS.FACTION_BLOCK] = $"{mapUrl.TrimEnd('/')}/resources/{fd["block"]}";
                                                    retVal.Add(new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_FACTION, $"{(multiGame ? $"{GameID}:" : string.Empty)}{faction}", fact), $"{GAMELIST_TERMS.TYPE_FACTION}\t{faction}", false));
                                                }
                                                else
                                                {
                                                    // trying to fix odd bug where sometimes the hero loads before the faction causing a bad ref
                                                    retVal.Add(new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_FACTION, $"{(multiGame ? $"{GameID}:" : string.Empty)}{faction}"), $"{GAMELIST_TERMS.TYPE_FACTION}\t{faction}", true));
                                                }
                                            }
                                            finally
                                            {
                                                factionsAlreadyReturnedLock.Release();
                                            }
                                        }
                                    }
                                }

                                // todo handle language logic
                                if (vehicle.Value.description != null)
                                {
                                    if (vehicle.Value.description.ContainsKey("en"))
                                    {
                                        heroData[GAMELIST_TERMS.HERO_DESCRIPTION] = vehicle.Value.description["en"];
                                    }
                                    else if (vehicle.Value.description.ContainsKey("default"))
                                    {
                                        heroData[GAMELIST_TERMS.HERO_DESCRIPTION] = vehicle.Value.description["default"];
                                    }
                                }

                                retVal.Add(new PendingDatum(heroData, $"{GAMELIST_TERMS.TYPE_HERO}\t{vehicle.Key}", false));

                                heroesAlreadyReturnedFull.Add(vehicle.Key);
                            }
                            else
                            {
                                // removed this since we're just sending stubs every time now instead via the actually allowed_heroes list
                                // to deal with interlacing spit out some stubs too
                                //retVal.Add(new PendingDatum(new Datum("hero", $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle.Key}"), $"hero\t{vehicle.Key}", true));
                            }
                        }
                        finally
                        {
                            heroesAlreadyReturnedLock.Release();
                        }
                    }
                    //mapDatum.AddObjectPath($"allowed_heroes", heroDatumList);

                    List<DatumRef> heroDatumList = new List<DatumRef>();
                    foreach (var vehicle in mapData.map.vehicles)
                    {
                        // dump a stub for each unit before we add it to our list, just in case, extras will get supressed on the output
                        retVal.Add(new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_HERO, $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle}"), $"{GAMELIST_TERMS.TYPE_HERO}\t{vehicle}", true));

                        heroDatumList.Add(new DatumRef(GAMELIST_TERMS.TYPE_HERO, $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle}"));
                    }

                    bool session_teamUpdate = session.PlayerLimit.HasValue && (mapData.map?.flags?.Contains("sbp_auto_ally_teams") ?? false);
                    bool session_syncUpdate = (mapData.map?.bzcp_type_fix ?? mapData.map?.bzcp_auto_type_fix ?? mapData.map?.type) == "S" &&
                                              !(session.SyncJoin ?? false) &&
                                              (mapData.map?.flags?.Contains("sbp") ?? false);
                    bool? session_is_deathmatch = mapData.map?.mission_dll switch
                    {
                        "MultSTMission" => false,
                        "MultDMMission" => true,
                        _ => null,
                    };
                    Datum sessionUpdate = null;
                    if (session_teamUpdate || session_syncUpdate || session_is_deathmatch.HasValue)
                    {
                        sessionUpdate = new Datum(GAMELIST_TERMS.TYPE_SESSION, $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion:B{session.id}");
                    }
                    if (session_teamUpdate)
                    {
                        // TODO account for when player slots are consumed by spectators
                        sessionUpdate.AddObjectPath($"{GAMELIST_TERMS.SESSION_TEAMS}:1:{GAMELIST_TERMS.SESSION_TEAMS_X_MAX}", (session.PlayerLimit + 1) / 2);
                        sessionUpdate.AddObjectPath($"{GAMELIST_TERMS.SESSION_TEAMS}:2:{GAMELIST_TERMS.SESSION_TEAMS_X_MAX}", (session.PlayerLimit + 0) / 2);
                    }
                    if (session_syncUpdate)
                    {
                        // script based sync
                        sessionUpdate.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:sync_script", true);
                    }

                    // if we know the mission script we can re-apply the rules that don't matter as null
                    if (session_is_deathmatch.HasValue)
                    {
                        if (session_is_deathmatch.Value)
                        {
                            if (session.Lives.HasValue && session.Lives.Value > 0) sessionUpdate.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:lives", null);

                            // I think this is always active but only relevant for STRAT based maps
                            // ProducerClass removes build items of class CLASS_COMMTOWER
                            if (session.SatelliteEnabled.HasValue) sessionUpdate.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:satellite", null);

                            // I think this is always active but only relevant for STRAT based maps
                            // ProducerClass removes build items of class CLASS_BARRACKS
                            if (session.BarracksEnabled.HasValue) sessionUpdate.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:barracks", null);

                            // ArmoryClass removes mortar list entries of class CLASS_POWERUP_WEAPON with PrjID "apspln" and "spspln"
                            // If not a DeathMatch (set by map script class internally) remove weapons with PrjID "gsplint"
                            if (session.SplinterEnabled.HasValue) sessionUpdate.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:splinter", null);
                        }
                        else
                        {
                            if (session.TimeLimit.HasValue && session.TimeLimit > 0) sessionUpdate.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:time_limit", null);
                            if (session.KillLimit.HasValue && session.KillLimit > 0) sessionUpdate.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:kill_limit", null);
                        }

                        // GameObjectClass removes weapons of signiture "SNIP"
                        //if (session.SniperEnabled.HasValue) sessionUpdate.AddObjectPath("level:rules:sniper", session.SniperEnabled.Value);
                    }

                    if (sessionUpdate != null)
                        retVal.Add(new PendingDatum(sessionUpdate, null, false));

                    foreach (var dr in session.users.Values)
                    {
                        string vehicle = mapData.map.vehicles.Where(v => v.EndsWith($":{dr.Vehicle}")).FirstOrDefault();
                        int playerTeam = dr.Team ?? -1;
                        Datum player = null;
                        if (vehicle != null || (playerTeam > 0 && (mapData.map?.flags?.Contains("sbp_auto_ally_teams") ?? false)))
                        {
                            player = new Datum(GAMELIST_TERMS.TYPE_PLAYER, $"{(multiGame ? $"{GameID}:" : string.Empty)}{dr.id}");
                        }

                        if (vehicle != null)
                        {
                            // stub the hero just in case, even though this stub should NEVER actually occur
                            retVal.Add(new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_HERO, $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle}"), $"{GAMELIST_TERMS.TYPE_HERO}\t{vehicle}", true));

                            // make the player data and shove in our hero
                            player[GAMELIST_TERMS.PLAYER_HERO] = new DatumRef(GAMELIST_TERMS.TYPE_HERO, $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle}");
                        }

                        if (mapData.map?.flags?.Contains("sbp_auto_ally_teams") ?? false)
                        {
                            if (playerTeam >= 1 & playerTeam <= 15)
                            {
                                if (playerTeam % 2 == 1)
                                {
                                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_ID}", "1");
                                    if (playerTeam == 1 && (mapData.map.flags?.Contains("sbp_wingman_game") ?? false))
                                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_LEADER}", true);
                                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_INDEX}", (playerTeam - 1) / 2);
                                }
                                else if (playerTeam % 2 == 0)
                                {
                                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_ID}", "2");
                                    if (playerTeam == 2 && (mapData.map.flags?.Contains("sbp_wingman_game") ?? false))
                                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_LEADER}", true);
                                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_INDEX}", (playerTeam - 1) / 2);
                                }
                            }
                        }

                        if (player != null)
                            retVal.Add(new PendingDatum(player, null, false));
                    }
                    mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_ALLOWEDHEROES, heroDatumList);
                }

                retVal.Add(new PendingDatum(mapDatum, null, false));
            }
            return retVal;
        }

        private async Task<PendingDatum> BuildGameBalanceDatumAsync(string code, string name, string name_short, string note, bool multiGame, SemaphoreSlim gamebalanceFullAlreadySentLock, HashSet<string> gamebalanceFullAlreadySent)
        {
            await gamebalanceFullAlreadySentLock.WaitAsync();
            try
            {
                if (gamebalanceFullAlreadySent.Add(code)) {
                    DataCache cache = new DataCache() { { GAMELIST_TERMS.GAMEBALANCE_NAME, name } };
                    if (!string.IsNullOrWhiteSpace(name_short))
                        cache[GAMELIST_TERMS.GAMEBALANCE_ABBR] = name_short;
                    if (!string.IsNullOrWhiteSpace(note))
                        cache[GAMELIST_TERMS.GAMEBALANCE_NOTE] = note;
                    return new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}", cache), $"{GAMELIST_TERMS.TYPE_GAMEBALANCE}\t{code}", false);
                }
                else
                    return new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}"), $"{GAMELIST_TERMS.TYPE_GAMEBALANCE}\t{code}", true);
            }
            finally
            {
                gamebalanceFullAlreadySentLock.Release();
            }
        }

        private async Task<PendingDatum> BuildGameTypeDatumAsync(string code, string name, string icon, string color, bool multiGame, SemaphoreSlim gametypeFullAlreadySentLock, HashSet<string> gametypeFullAlreadySent)
        {
            await gametypeFullAlreadySentLock.WaitAsync();
            try
            {
                if (gametypeFullAlreadySent.Add(code))
                {
                    DataCache DataCacheItem = new DataCache() { { GAMELIST_TERMS.GAMETYPE_NAME, name } };
                    if (icon != null) DataCacheItem[GAMELIST_TERMS.GAMETYPE_ICON] = icon;

                    // Main Color
                    if (color != null) DataCacheItem[GAMELIST_TERMS.GAMETYPE_COLOR] = color; // general color

                    // Color Pair
                    // color_f <Omitted> Forground
                    // color_b <Omitted> Background

                    // Dark Backgrounded Color Pair
                    // color_df <Omitted> Dark background Foreground (assume black if not present)
                    // color_db <Omitted> Dark background Background (assume black if not present)

                    // Light Backgrounded Color Pair
                    // color_lf <Omitted> Light background Forground
                    // color_lb <Omitted> Light background Background (assume white if not present)

                    return new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_GAMETYPE, $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}", DataCacheItem), $"{GAMELIST_TERMS.TYPE_GAMETYPE}\t{code}", false);
                }
                else
                    return new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_GAMETYPE, $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}"), $"{GAMELIST_TERMS.TYPE_GAMETYPE}\t{code}", true);
            }
            finally
            {
                gametypeFullAlreadySentLock.Release();
            }
        }
        private async Task<PendingDatum> BuildGameModeDatumAsync(string code, string name, string icon, string colorA, string colorB, bool multiGame, SemaphoreSlim gamemodeFullAlreadySentLock, HashSet<string> gamemodeFullAlreadySent)
        {
            await gamemodeFullAlreadySentLock.WaitAsync();
            try
            {
                if (gamemodeFullAlreadySent.Add(code))
                {
                    DataCache DataCacheItem = new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, name } };
                    if (icon != null) DataCacheItem[GAMELIST_TERMS.GAMEMODE_ICON] = icon;

                    // Main Color
                    if (colorA != null) DataCacheItem[GAMELIST_TERMS.GAMEMODE_COLOR] = colorA; // general color

                    // Color Pair
                    // color_f <Omitted> Forground
                    // color_b <Omitted> Background

                    // Dark Backgrounded Color Pair
                    if (colorB != null) DataCacheItem[GAMELIST_TERMS.GAMEMODE_COLORDF] = colorB; // Dark background Foreground
                    // color_db <Omitted> Dark background Background (assume black if not present)

                    // Light Backgrounded Color Pair
                    // color_lf <Omitted> Light background Forground
                    // color_lb <Omitted> Light background Background (assume white if not present)
                    return new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}", DataCacheItem), $"{GAMELIST_TERMS.TYPE_GAMEMODE}\t{code}", false);
                }
                else
                    return new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}"), $"{GAMELIST_TERMS.TYPE_GAMEMODE}\t{code}", true);
            }
            finally
            {
                gamemodeFullAlreadySentLock.Release();
            }
        }
    }
}
