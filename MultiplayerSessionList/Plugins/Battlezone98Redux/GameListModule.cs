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
    public class GameListModule : IGameListModule
    {
        public string GameID => "bigboat:battlezone_98_redux";
        public string Title => "Battlezone 98 Redux";
        public bool IsPublic => true;


        private string queryUrl;
        private string mapUrl;
        private GogInterface gogInterface;
        private SteamInterface steamInterface;
        private CachedAdvancedWebClient cachedAdvancedWebClient;

        public GameListModule(IConfiguration configuration, GogInterface gogInterface, SteamInterface steamInterface, CachedAdvancedWebClient cachedAdvancedWebClient)
        {
            queryUrl = configuration["bigboat:battlezone_98_redux:sessions"];
            mapUrl = configuration["bigboat:battlezone_98_redux:maps"];
            this.gogInterface = gogInterface;
            this.steamInterface = steamInterface;
            this.cachedAdvancedWebClient = cachedAdvancedWebClient;
        }

        public async IAsyncEnumerable<Datum> GetGameListChunksAsync(bool multiGame, bool admin, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var res_raw = await cachedAdvancedWebClient.GetObject<string>(queryUrl, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
            var res = res_raw.Data;
            var gamelist = JsonConvert.DeserializeObject<Dictionary<string, Lobby>>(res);

            TaskFactory taskFactory = new TaskFactory(cancellationToken);

            yield return new Datum("source", $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion", new DataCache() {
                { "name", "Rebellion" },
                { "timestamp", res_raw.LastModified },
            });

            if (!multiGame)
                yield return new Datum("default", "session", new DataCache() {
                    { "type", GAMELIST_TERMS.TYPE_LISTEN },
                    { "sources", new DataCache() { {"Rebellion", new DatumRef("source", $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion") } } },
                });

            HashSet<string> DontSendStub = new HashSet<string>();

            Dictionary<(string mod, string map), Task<MapData>> MapDataFetchTasks = new Dictionary<(string mod, string map), Task<MapData>>();
            List<Task<List<PendingDatum>>> DelayedDatumTasks = new List<Task<List<PendingDatum>>>();

            SemaphoreSlim heroesAlreadyReturnedLock = new SemaphoreSlim(1, 1);
            HashSet<string> heroesAlreadyReturnedFull = new HashSet<string>();

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

                Datum session = new Datum("session", $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion:B{raw.id}");

                if (multiGame) {
                    session["type"] = GAMELIST_TERMS.TYPE_LISTEN;
                    session["sources"] = new DataCache() { { $"Rebellion", new DatumRef("source", $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion") } };
                }

                session["name"] = raw.Name;

                session.AddObjectPath("address:token", $"B{raw.id}");
                session.AddObjectPath("address:other:lobby_id",raw.id);

                List<DataCache> PlayerTypes = new List<DataCache>();
                PlayerTypes.Add(new DataCache()
                {
                    { "types", new List<string>() { GAMELIST_TERMS.PLAYERTYPE_PLAYER } },
                    { "max", raw.PlayerLimit },
                });
                session["player_types"] = PlayerTypes;

                session.AddObjectPath($"player_count:{GAMELIST_TERMS.PLAYERTYPE_PLAYER}", raw.userCount);

                string modID = (raw.WorkshopID ?? @"0");

                if (modID != "0")
                {
                    if (DontSendStub.Add($"mod\t{modID}"))
                    {
                        yield return new Datum("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}");
                    }
                    session.AddObjectPath("game:mod", new DatumRef("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}"));
                }

                if (modID == "0")
                {
                    // we aren't concurrent yet so we're safe to just do this
                    if (DontSendStub.Add(@"game_balance\tSTOCK"))
                        yield return new Datum("game_balance", $"{(multiGame ? $"{GameID}:" : string.Empty)}STOCK", new DataCache() { { "name", "Stock" } });
                    session.AddObjectPath("game:game_balance", new DatumRef("game_balance", $"{(multiGame ? $"{GameID}:" : string.Empty)}STOCK"));
                }

                string mapID = System.IO.Path.GetFileNameWithoutExtension(raw.MapFile).ToLowerInvariant();

                // TODO this map stub datum doesn't need to be emitted if another prior session already emitted it
                Datum mapData = new Datum("map", $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}");
                mapData["map_file"] = raw.MapFile.ToLowerInvariant();
                yield return mapData;
                DontSendStub.Add($"map\t{modID}:{mapID}"); // we already sent the a stub don't send another

                session.AddObjectPath("level:map", new DatumRef("map", $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}"));
                session.AddObjectPath("level:other:crc32", raw.CRC32);

                //if (!string.IsNullOrWhiteSpace(raw.WorkshopID) && raw.WorkshopID != "0")
                //{
                //    /*if (!modsAlreadyReturnedStub.Contains(raw.WorkshopID))
                //    {
                //        yield return new Datum("mod", raw.WorkshopID);
                //        modsAlreadyReturnedStub.Add(raw.WorkshopID);
                //    }*/
                //    session.AddObjectPath("level:mod", new DatumRef("mod", raw.WorkshopID));
                //}

                if (raw.TimeLimit.HasValue && raw.TimeLimit > 0) session.AddObjectPath("level:rules:time_limit", raw.TimeLimit);
                if (raw.KillLimit.HasValue && raw.KillLimit > 0) session.AddObjectPath("level:rules:kill_limit", raw.KillLimit);
                if (raw.Lives.HasValue && raw.Lives.Value > 0) session.AddObjectPath("level:rules:lives", raw.Lives.Value);

                // ProducerClass removes build items of class CLASS_COMMTOWER
                if (raw.SatelliteEnabled.HasValue) session.AddObjectPath("level:rules:satellite", raw.SatelliteEnabled.Value);

                // ProducerClass removes build items of class CLASS_BARRACKS
                if (raw.BarracksEnabled.HasValue) session.AddObjectPath("level:rules:barracks", raw.BarracksEnabled.Value);

                // GameObjectClass removes weapons of signiture "SNIP"
                if (raw.SniperEnabled.HasValue) session.AddObjectPath("level:rules:sniper", raw.SniperEnabled.Value);

                // ArmoryClass removes mortar list entries of class CLASS_POWERUP_WEAPON with PrjID "apspln" and "spspln"
                // If not a DeathMatch (set by map script class internally) remove weapons with PrjID "gsplint"
                if (raw.SplinterEnabled.HasValue) session.AddObjectPath("level:rules:splinter", raw.SplinterEnabled.Value);

                // unlocked in progress games with SyncJoin will trap the user due to a bug, just list as locked
                if (!raw.isLocked && raw.SyncJoin.HasValue && raw.SyncJoin.Value && (!raw.IsEnded && raw.IsLaunched))
                {
                    session.AddObjectPath($"status:{GAMELIST_TERMS.STATUS_LOCKED}", true);
                    session.AddObjectPath("status:other:sync_bug", true);
                }
                else
                {
                    session.AddObjectPath($"status:{GAMELIST_TERMS.STATUS_LOCKED}", raw.isLocked);
                }
                session.AddObjectPath($"status:{GAMELIST_TERMS.STATUS_PASSWORD}", raw.IsPassworded);
                    
                string ServerState = Enum.GetName(typeof(ESessionState), raw.IsEnded ? ESessionState.PostGame : raw.IsLaunched ? ESessionState.InGame : ESessionState.PreGame);
                session.AddObjectPath("status:state", ServerState); // TODO limit this state to our state enumeration
                session.AddObjectPath("status:other:state", ServerState);

                //SemaphoreSlim playerCacheLock = new SemaphoreSlim(1, 1);
                //Dictionary<string, Tuple<int>> playerCache = new Dictionary<string, Tuple<int>>();

                List<DatumRef> Players = new List<DatumRef>();
                foreach (var dr in raw.users.Values)
                {
                    Datum player = new Datum("player", $"{(multiGame ? $"{GameID}:" : string.Empty)}{dr.id}");

                    player["name"] = dr.name;
                    player["type"] = GAMELIST_TERMS.PLAYERTYPE_PLAYER;
                    player.AddObjectPath("other:launched", dr.Launched);
                    player.AddObjectPath("other:is_auth", dr.isAuth);
                    if (admin)
                    {
                        player.AddObjectPath("other:wan_address", dr.wanAddress);
                        player.AddObjectPath("other:lan_addresses", JArray.FromObject(dr.lanAddresses));
                    }
                    if (dr.CommunityPatch != null)
                        player.AddObjectPath("other:community_patch", dr.CommunityPatch);
                    if (dr.CommunityPatchShim != null)
                        player.AddObjectPath("other:community_patch_shim", dr.CommunityPatchShim);

                    if (dr.Team.HasValue)
                    {
                        //player.AddObjectPath("team:id", dr.Team.Value.ToString());
                        player.AddObjectPath("ids:slot:id", dr.Team);
                        player.AddObjectPath("index", dr.Team);

                        //playerCache[dr.id] = new Tuple<int>(dr.Team.Value);
                    }

                    //player.Attributes.Add("Vehicle", dr.Vehicle);
                    //if (dr.Vehicle != null)
                    //{
                    //    // the issue here is that the vehicle ID can easily be wrong, due to the workshop prefix
                    //    // we need to find a way to correct this
                    //    string heroId = (raw.WorkshopID ?? @"0") + @":" + dr.Vehicle.ToLowerInvariant();
                    //    yield return new Datum("hero", $"{(multiGame ? $"{GameID}:" : string.Empty)}{heroId}", new DataCache()
                    //    {
                    //        //{ "other", new DataCache2() { { "odf", dr.Vehicle } } },
                    //    });
                    //    DontSendStub.Add($"hero\t{heroId}"); // we already sent the a stub don't send another
                    //    player["hero"] = new DatumRef("hero", $"{(multiGame ? $"{GameID}:" : string.Empty)}{heroId}");
                    //}

                    if (!string.IsNullOrWhiteSpace(dr.id))
                    {
                        player.AddObjectPath("ids:bzr_net:id", dr.id);
                        if (dr.id == raw.owner)
                            player["is_host"] = true;
                        switch (dr.id[0])
                        {
                            case 'S': // dr.authType == "steam"
                                {
                                    ulong playerID = 0;
                                    if (ulong.TryParse(dr.id.Substring(1), out playerID))
                                    {
                                        yield return new Datum("identity/steam", playerID.ToString(), new DataCache()
                                        {
                                            { "type", "steam" },
                                        });
                                        DontSendStub.Add($"identity/steam\t{playerID.ToString()}"); // we already sent the a stub don't send another

                                        player.AddObjectPath("ids:steam", new DataCache() {
                                            { "id", playerID.ToString() },
                                            { "raw", dr.id.Substring(1) },
                                            { "identity", new DatumRef("identity/steam", playerID.ToString()) },
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

                                        yield return new Datum("identity/gog", playerID.ToString(), new DataCache()
                                        {
                                            { "type", "gog" },
                                        });
                                        DontSendStub.Add($"identity/gog\t{playerID.ToString()}"); // we already sent the a stub don't send another

                                        player.AddObjectPath("ids:gog", new DataCache() {
                                            { "id", playerID.ToString() },
                                            { "raw", dr.id.Substring(1) },
                                            { "identity", new DatumRef("identity/gog", playerID.ToString()) },
                                        });

                                        DelayedDatumTasks.Add(gogInterface.GetPendingDataAsync(playerID));
                                    }
                                }
                                break;
                        }
                    }

                    yield return player;

                    Players.Add(new DatumRef("player", $"{(multiGame ? $"{GameID}:" : string.Empty)}{dr.id}"));
                }
                session["players"] = Players;

                if (!MapDataFetchTasks.ContainsKey((modID, mapID)))
                    DelayedDatumTasks.Add(BuildDatumsForMapDataAsync(modID, mapID, raw, multiGame,
                        modsAlreadyReturnedLock, modsAlreadyReturnedFull,
                        gametypeFullAlreadySentLock, gametypeFullAlreadySent,
                        gamemodeFullAlreadySentLock, gamemodeFullAlreadySent,
                        heroesAlreadyReturnedLock, heroesAlreadyReturnedFull,
                        gamebalanceFullAlreadySentLock, gamebalanceFullAlreadySent));
                        //playerCacheLock, playerCache));

                if (!string.IsNullOrWhiteSpace(raw.clientVersion))
                    session.AddObjectPath("game:version", raw.clientVersion);
                else if (!string.IsNullOrWhiteSpace(raw.GameVersion))
                    session.AddObjectPath("game:version", raw.GameVersion);

                if (raw.SyncJoin.HasValue)
                    session.AddObjectPath("other:sync_join", raw.SyncJoin.Value);
                if (raw.MetaDataVersion.HasValue)
                    session.AddObjectPath("other:meta_data_version", raw.MetaDataVersion);

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
            SemaphoreSlim gamebalanceFullAlreadySentLock, HashSet<string> gamebalanceFullAlreadySent)
            //SemaphoreSlim playerCacheLock, Dictionary<string, Tuple<int>> playerCache)
        {
            List<PendingDatum> retVal = new List<PendingDatum>();
            CachedData<MapData> mapDataC = await cachedAdvancedWebClient.GetObject<MapData>($"{mapUrl.TrimEnd('/')}/getdata2.php?map={mapID}&mods={modID}");
            MapData mapData = mapDataC?.Data;
            if (mapData != null)
            {
                Datum mapDatum = new Datum("map", $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}", new DataCache() {
                    { "name", mapData?.map?.title },
                });
                if (mapData.map?.image != null)
                    mapDatum["image"] = $"{mapUrl.TrimEnd('/')}/{mapData.map.image}";

                mapDatum.AddObjectPath("game_type:id", mapData?.map?.type);
                //game.Level["GameMode"] = "Unknown";
                string mapType = mapData?.map?.bzcp_type_fix ?? mapData?.map?.bzcp_auto_type_fix ?? mapData?.map?.type;
                string mapMode = mapData?.map?.bzcp_type_override ?? mapData?.map?.bzcp_auto_type_override ?? mapType;
                if (!string.IsNullOrWhiteSpace(mapType))
                {
                    switch (mapType)
                    {
                        case "D": // Deathmatch
                            mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                            retVal.Add(await BuildGameTypeDatumAsync("DM", "Deathmatch", $"{mapUrl.TrimEnd('/')}/icons/icon_d.png", "#B70505", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                            break;
                        case "S": // Strategy
                            mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                            retVal.Add(await BuildGameTypeDatumAsync("STRAT", "Strategy", $"{mapUrl.TrimEnd('/')}/icons/icon_s.png", "#007FFF", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                            break;
                        case "K": // King of the Hill
                            mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                            retVal.Add(await BuildGameTypeDatumAsync("DM", "Deathmatch", $"{mapUrl.TrimEnd('/')}/icons/icon_d.png", "#B70505", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                            break;
                        case "M": // Mission MPI
                            mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                            retVal.Add(await BuildGameTypeDatumAsync("STRAT", "Strategy", $"{mapUrl.TrimEnd('/')}/icons/icon_s.png", "#007FFF", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                            break;
                        case "A": // Action MPI
                            mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                            retVal.Add(await BuildGameTypeDatumAsync("DM", "Deathmatch", $"{mapUrl.TrimEnd('/')}/icons/icon_d.png", "#B70505", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                            break;
                        case "X": // Other
                            mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}OTHER"));
                            retVal.Add(await BuildGameTypeDatumAsync("OTHER", "Other", $"{mapUrl.TrimEnd('/')}/icons/icon_x.png", "#666666", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
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
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}A_MPI"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_a.png";
                            MapModeColorA = "#002C00";
                            MapModeColorB = "#007C03";
                            retVal.Add(await BuildGameModeDatumAsync("A_MPI", "Action MPI", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "C": // Custom
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}CUSTOM"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_c.png";
                            MapModeColorA = "#FFFF00";
                            MapModeColorB = "#FFFF00";
                            retVal.Add(await BuildGameModeDatumAsync("CUSTOM", "Custom", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "D": // Deathmatch
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_d.png";
                            MapModeColorA = "#B70505";
                            MapModeColorB = "#E90707";
                            retVal.Add(await BuildGameModeDatumAsync("DM", "Deathmatch", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "F": // Capture the Flag
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}CTF"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_f.png";
                            MapModeColorA = "#7F5422";
                            MapModeColorB = "#B0875E";
                            retVal.Add(await BuildGameModeDatumAsync("CTF", "Capture the Flag", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "G": // Race
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}RACE"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_g.png";
                            MapModeColorA = "#1A1A1A";
                            MapModeColorB = "#EEEEEE";
                            retVal.Add(await BuildGameModeDatumAsync("RACE", "Race", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "K": // King of the Hill
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}KOTH"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_k.png";
                            MapModeColorA = "#F0772D";
                            MapModeColorB = "#F0772D";
                            retVal.Add(await BuildGameModeDatumAsync("KOTH", "King of the Hill", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "L": // Loot
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}LOOT"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_l.png";
                            MapModeColorA = "#333333";
                            MapModeColorB = "#BFA88F";
                            retVal.Add(await BuildGameModeDatumAsync("LOOT", "Loot", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "M": // Mission MPI
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}M_MPI"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_m.png";
                            MapModeColorA = "#B932FF";
                            MapModeColorB = "#B932FF";
                            retVal.Add(await BuildGameModeDatumAsync("M_MPI", "Mission MPI", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "P": // Pilot/Sniper Deathmatch
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}PILOT"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_p.png";
                            MapModeColorA = "#7A0000";
                            MapModeColorB = "#B70606";
                            retVal.Add(await BuildGameModeDatumAsync("PILOT", "Pilot Deathmatch", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "Q": // Squad Deathmatch
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}SQUAD"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_q.png";
                            MapModeColorA = "#FF3F00";
                            MapModeColorB = "#FF3F00";
                            retVal.Add(await BuildGameModeDatumAsync("SQUAD", "Squad Deathmatch", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "R": // Capture the Relic
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}RELIC"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_r.png";
                            MapModeColorA = "#7D007D";
                            MapModeColorB = "#7D007D";
                            retVal.Add(await BuildGameModeDatumAsync("RELIC", "Capture the Relic", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "S": // Strategy
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_s.png";
                            MapModeColorA = "#007FFF";
                            MapModeColorB = "#007FFF";
                            retVal.Add(await BuildGameModeDatumAsync("STRAT", "Strategy", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "W": // Wingman
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}WINGMAN"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_w.png";
                            MapModeColorA = "#0047CF";
                            MapModeColorB = "#0047CF";
                            retVal.Add(await BuildGameModeDatumAsync("WINGMAN", "Wingman Strategy", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                        case "X": // Other
                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}OTHER"));
                            MapModeIcon = $"{mapUrl.TrimEnd('/')}/icons/icon_x.png";
                            MapModeColorA = "#666666";
                            MapModeColorB = "#C3C3C3";
                            retVal.Add(await BuildGameModeDatumAsync("OTHER", "Other", MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                            break;
                    }
                }
                if (!string.IsNullOrWhiteSpace(mapData?.map?.custom_type))
                {
                    //if (mapData.map.custom_type == "S" && (mapData.map.flags?.Contains("bad_type") ?? false))
                    //{
                    //    // Special case of SBP being knobs and use 'K' for their STRAT maps
                    //    mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                    //    retVal.Add(await BuildGameTypeDatumAsync("STRAT", "Strategy", $"{mapUrl.TrimEnd('/')}/icons/icon_s.png", multiGame, gametypeFullAlreadySentLock, gametypeFullAlreadySent));
                    //
                    //    mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                    //    retVal.Add(await BuildGameModeDatumAsync("STRAT", "Strategy", $"{mapUrl.TrimEnd('/')}/icons/icon_s.png", multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                    //}
                    //else
                    //{
                        // Custom type
                        mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}CUST_{mapData.map.custom_type}"));
                        retVal.Add(await BuildGameModeDatumAsync($"CUST_{mapData.map.custom_type}", mapData.map.custom_type_name, MapModeIcon, MapModeColorA, MapModeColorB, multiGame, gamemodeFullAlreadySentLock, gamemodeFullAlreadySent));
                        //if (mapData.map.custom_type == "TOOL")
                        //{
                        //    mapDatum.AddObjectPath("game_type", null);
                        //}
                    //}
                }

                if (mapData.map?.flags?.Contains("sbp") ?? false)
                {
                    mapDatum.AddObjectPath("game_balance", new DatumRef("game_balance", $"{(multiGame ? $"{GameID}:" : string.Empty)}CUST_SBP"));
                    retVal.Add(await BuildGameBalanceDatumAsync($"CUST_SBP", "Strat Balance Patch", "SBP", "This session uses a mod balance paradigm called \"Strat Balance Patch\" which significantly changes game balance.", multiGame, gamebalanceFullAlreadySentLock, gamebalanceFullAlreadySent));
                } else if (mapData.map?.flags?.Contains("balance_stock") ?? false)
                {
                    mapDatum.AddObjectPath("game_balance", new DatumRef("game_balance", $"{(multiGame ? $"{GameID}:" : string.Empty)}STOCK"));
                    retVal.Add(await BuildGameBalanceDatumAsync($"STOCK", "Stock", null, null, multiGame, gamebalanceFullAlreadySentLock, gamebalanceFullAlreadySent));
                }

                if (mapData.map?.flags?.Contains("sbp_auto_ally_teams") ?? false)
                {
                    //mapDatum.AddObjectPath("teams:1:member_player_indexes", new int[] { 1, 3, 5, 7, 9, 11, 13, 15 });
                    //mapDatum.AddObjectPath("teams:2:member_player_indexes", new int[] { 2, 4, 6, 8, 10, 12, 14 });
                    mapDatum.AddObjectPath("teams:1:name", "Odds");
                    mapDatum.AddObjectPath("teams:2:name", "Evens");

                    //if (mapData.map.flags?.Contains("sbp_wingman_game") ?? false)
                    //{
                    //    mapDatum.AddObjectPath("teams:1:leader_player_indexes", new int[] { 1 });
                    //    mapDatum.AddObjectPath("teams:2:leader_player_indexes", new int[] { 2 });
                    //}
                }

                // we don't bother linking these mods to the map since they came from the session.game, not the map, their data just came in piggybacking on the map data
                //List<DatumRef> modDatumList = new List<DatumRef>();
                if (mapData?.mods != null && mapData.mods.Count > 0)
                {
                    foreach (var mod in mapData.mods)
                    {
                        // skip stock
                        if (mod.Key == "0")
                            continue;

                        //modDatumList.Add(new DatumRef("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}{mod.Key}"));

                        await modsAlreadyReturnedLock.WaitAsync();
                        try
                        {
                            if (!modsAlreadyReturnedFull.Contains(mod.Key))
                            {
                                Datum modData = new Datum("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}{mod.Key}", new DataCache() {
                                    { "name", mod.Value?.name ?? mod.Value?.workshop_name },
                                });

                                if (mod.Value?.image != null)
                                    modData.Data["image"] = $"{mapUrl.TrimEnd('/')}/{mod.Value.image}";

                                if (UInt64.TryParse(mod.Key, out UInt64 modId) && modId > 0)
                                    modData.Data["url"] = $"http://steamcommunity.com/sharedfiles/filedetails/?id={mod.Key}";

                                if (mod.Value?.dependencies != null && mod.Value.dependencies.Count > 0)
                                {
                                    // just spam out stubs for dependencies, they're a mess anyway, the reducer at the end will reduce it
                                    foreach (var dep in mod.Value.dependencies)
                                        retVal.Add(new PendingDatum(new Datum("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}{dep}"), $"mod\t{dep}", true));
                                    modData.AddObjectPath("dependencies", mod.Value.dependencies.Select(dep => new DatumRef("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}{dep}")));
                                }

                                retVal.Add(new PendingDatum(modData, $"mod\t{mod.Key}", false));

                                modsAlreadyReturnedFull.Add(mod.Key);
                            }
                            else
                            {
                                // to deal with interlacing spit out some stubs too
                                retVal.Add(new PendingDatum(new Datum("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}{mod.Key}"), $"mod\t{mod.Key}", true));
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
                                Datum heroData = new Datum("hero", $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle.Key}", new DataCache() {
                                    { "name", vehicle.Value.name },
                                });

                                // todo handle language logic
                                if (vehicle.Value.description != null)
                                {
                                    if (vehicle.Value.description.ContainsKey("en"))
                                    {
                                        //heroData["description"] = vehicle.Value.description["en"].content;
                                        heroData["description"] = vehicle.Value.description["en"];
                                    }
                                    else if (vehicle.Value.description.ContainsKey("default"))
                                    {
                                        //heroData["description"] = vehicle.Value.description["default"].content;
                                        heroData["description"] = vehicle.Value.description["default"];
                                    }
                                }

                                retVal.Add(new PendingDatum(heroData, $"hero\t{vehicle.Key}", false));

                                heroesAlreadyReturnedFull.Add(vehicle.Key);
                            }
                            else
                            {
                                // removed this since we're just sending stubs every time now instead via the actualy allowed_heroes list
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
                        retVal.Add(new PendingDatum(new Datum("hero", $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle}"), $"hero\t{vehicle}", true));

                        heroDatumList.Add(new DatumRef("hero", $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle}"));
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
                        sessionUpdate = new Datum("session", $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion:B{session.id}");
                    }
                    if (session_teamUpdate)
                    {
                        // TODO account for when player slots are consumed by spectators
                        sessionUpdate.AddObjectPath("teams:1:max", (session.PlayerLimit + 1) / 2);
                        sessionUpdate.AddObjectPath("teams:2:max", (session.PlayerLimit + 0) / 2);
                    }
                    if (session_syncUpdate)
                    {
                        // script based sync
                        sessionUpdate.AddObjectPath("other:sync_script", true);
                    }

                    // if we know the mission script we can re-apply the rules that don't matter as null
                    if (session_is_deathmatch.HasValue)
                    {
                        if (session_is_deathmatch.Value)
                        {
                            if (session.Lives.HasValue && session.Lives.Value > 0) sessionUpdate.AddObjectPath("level:rules:lives", null);

                            // I think this is always active but only relevant for STRAT based maps
                            // ProducerClass removes build items of class CLASS_COMMTOWER
                            if (session.SatelliteEnabled.HasValue) sessionUpdate.AddObjectPath("level:rules:satellite", null);

                            // I think this is always active but only relevant for STRAT based maps
                            // ProducerClass removes build items of class CLASS_BARRACKS
                            if (session.BarracksEnabled.HasValue) sessionUpdate.AddObjectPath("level:rules:barracks", null);

                            // ArmoryClass removes mortar list entries of class CLASS_POWERUP_WEAPON with PrjID "apspln" and "spspln"
                            // If not a DeathMatch (set by map script class internally) remove weapons with PrjID "gsplint"
                            if (session.SplinterEnabled.HasValue) sessionUpdate.AddObjectPath("level:rules:splinter", null);
                    }
                        else
                        {
                            if (session.TimeLimit.HasValue && session.TimeLimit > 0) sessionUpdate.AddObjectPath("level:rules:time_limit", null);
                            if (session.KillLimit.HasValue && session.KillLimit > 0) sessionUpdate.AddObjectPath("level:rules:kill_limit", null);
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
                            player = new Datum("player", $"{(multiGame ? $"{GameID}:" : string.Empty)}{dr.id}");
                        }

                        if (vehicle != null)
                        {
                            // stub the hero just in case, even though this stub should NEVER actually occur
                            retVal.Add(new PendingDatum(new Datum("hero", $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle}"), $"hero\t{vehicle}", true));

                            // make the player data and shove in our hero
                            player["hero"] = new DatumRef("hero", $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle}");
                        }

                        if (mapData.map?.flags?.Contains("sbp_auto_ally_teams") ?? false)
                        {
                            if (playerTeam >= 1 & playerTeam <= 15)
                            {
                                if (playerTeam % 2 == 1)
                                {
                                    player.AddObjectPath("team:id", "1");
                                    if (playerTeam == 1 && (mapData.map.flags?.Contains("sbp_wingman_game") ?? false))
                                        player.AddObjectPath("team:leader", true);
                                    player.AddObjectPath("team:index", (playerTeam - 1) / 2);
                                }
                                else if (playerTeam % 2 == 0)
                                {
                                    player.AddObjectPath("team:id", "2");
                                    if (playerTeam == 2 && (mapData.map.flags?.Contains("sbp_wingman_game") ?? false))
                                        player.AddObjectPath("team:leader", true);
                                    player.AddObjectPath("team:index", (playerTeam - 1) / 2);
                                }
                            }
                        }

                        if (player != null)
                            retVal.Add(new PendingDatum(player, null, false));
                    }
                    mapDatum.AddObjectPath($"allowed_heroes", heroDatumList);
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
                    DataCache cache = new DataCache() { { "name", name } };
                    if (!string.IsNullOrWhiteSpace(name_short))
                        cache["abbr"] = name_short;
                    if (!string.IsNullOrWhiteSpace(note))
                        cache["note"] = note;
                    return new PendingDatum(new Datum("game_balance", $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}", cache), $"game_balance\t{code}", false);
                }
                else
                    return new PendingDatum(new Datum("game_balance", $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}"), $"game_balance\t{code}", true);
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
                    DataCache DataCacheItem = new DataCache() { { "name", name } };
                    if (icon != null) DataCacheItem["icon"] = icon;

                    // Main Color
                    if (color != null) DataCacheItem["color"] = color; // general color

                    // Color Pair
                    // color_f <Omitted> Forground
                    // color_b <Omitted> Background

                    // Dark Backgrounded Color Pair
                    // color_df <Omitted> Dark background Foreground (assume black if not present)
                    // color_db <Omitted> Dark background Background (assume black if not present)

                    // Light Backgrounded Color Pair
                    // color_lf <Omitted> Light background Forground
                    // color_lb <Omitted> Light background Background (assume white if not present)

                    return new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}", DataCacheItem), $"game_type\t{code}", false);
                }
                else
                    return new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}"), $"game_type\t{code}", true);
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
                    DataCache DataCacheItem = new DataCache() { { "name", name } };
                    if (icon != null) DataCacheItem["icon"] = icon;

                    // Main Color
                    if (colorA != null) DataCacheItem["color"] = colorA; // general color

                    // Color Pair
                    // color_f <Omitted> Forground
                    // color_b <Omitted> Background

                    // Dark Backgrounded Color Pair
                    if (colorB != null) DataCacheItem["color_df"] = colorB; // Dark background Foreground
                    // color_db <Omitted> Dark background Background (assume black if not present)

                    // Light Backgrounded Color Pair
                    // color_lf <Omitted> Light background Forground
                    // color_lb <Omitted> Light background Background (assume white if not present)
                    return new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}", DataCacheItem), $"game_mode\t{code}", false);
                }
                else
                    return new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{code}"), $"game_mode\t{code}", true);
            }
            finally
            {
                gamemodeFullAlreadySentLock.Release();
            }
        }
    }
}
