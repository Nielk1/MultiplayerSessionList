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
using System.Threading.Tasks;
using System.Threading;
using static MultiplayerSessionList.Services.GogInterface;
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
        private CachedJsonWebClient mapDataInterface;

        public GameListModule(IConfiguration configuration, GogInterface gogInterface, SteamInterface steamInterface, CachedJsonWebClient mapDataInterface)
        {
            queryUrl = configuration["bigboat:battlezone_98_redux:sessions"];
            mapUrl = configuration["bigboat:battlezone_98_redux:maps"];
            this.gogInterface = gogInterface;
            this.steamInterface = steamInterface;
            this.mapDataInterface = mapDataInterface;
        }

        public async IAsyncEnumerable<Datum> GetGameListChunksAsync(bool multiGame, bool admin, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using (var http = new HttpClient())
            {
                var res_raw = await http.GetAsync(queryUrl).ConfigureAwait(false);
                var res = await res_raw.Content.ReadAsStringAsync();
                var gamelist = JsonConvert.DeserializeObject<Dictionary<string, Lobby>>(res);

                yield return new Datum("source", $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion", new DataCache() {
                    { "name", "Rebellion" },
                    //{ "status", proxyStatus.Value.status },
                    //{ "success", proxyStatus.Value.success },
                    { "timestamp", res_raw.Content.Headers.LastModified.Value.ToUniversalTime().UtcDateTime },
                });

                if (!multiGame)
                    yield return new Datum("default", "session", new DataCache() {
                        { "type", GAMELIST_TERMS.TYPE_LISTEN },
                        { "sources", new DataCache() { {"Rebellion", new DatumRef("source", $"{(multiGame ? $"{GameID}:" : string.Empty)}Rebellion") } } },
                    }, true);

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

                //yield return new Datum("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}0", new DataCache() { { "name", "Stock" } });
                //modsAlreadyReturnedFull.Add("0"); // full data for stock already returned as there's so little data for it, remove this if stock gets more data
                //DontSendStub.Add("mod\t0"); // we already sent the full data for stock, don't send stubs

                /*
                Tasks.Add(Task.Run(async () =>
                {
                    await DataCacheLock.WaitAsync();
                    try
                    {
                        DataCache.AddObjectPath($"Level:GameType:DM:Name", "Deathmatch");
                        DataCache.AddObjectPath($"Level:GameType:STRAT:Name", "Strategy");

                        DataCache.AddObjectPath($"Level:GameMode:DM:Name", "Deathmatch");
                        DataCache.AddObjectPath($"Level:GameMode:STRAT:Name", "Strategy");
                        DataCache.AddObjectPath($"Level:GameMode:KOTH:Name", "King of the Hill");
                        DataCache.AddObjectPath($"Level:GameMode:M_MPI:Name", "Mission MPI");
                        DataCache.AddObjectPath($"Level:GameMode:A_MPI:Name", "Action MPI");
                    }
                    finally
                    {
                        DataCacheLock.Release();
                    }
                }));
                */

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


                    string mapID = System.IO.Path.GetFileNameWithoutExtension(raw.MapFile).ToLowerInvariant();

                    // TODO this map stub datum doesn't need to be emitted if another prior session already emitted it
                    Datum mapData = new Datum("map", $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}");
                    mapData["map_file"] = raw.MapFile.ToLowerInvariant();
                    yield return mapData;
                    DontSendStub.Add($"map\t{modID}:{mapID}"); // we already sent the a stub don't send another

                    session.AddObjectPath("level:map", new DatumRef("map", $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}"));
                    session.AddObjectPath("level:other:crc32", raw.CRC32);

                    if (!MapDataFetchTasks.ContainsKey((modID, mapID)))
                    {
                        DelayedDatumTasks.Add(Task.Run(async () => {
                            var session = raw; // TODO this is just a race condition isn't it? what should we do, stagger with a semaphore?
                            List<PendingDatum> retVal = new List<PendingDatum>();
                            MapData mapData = await mapDataInterface.GetObject<MapData>($"{mapUrl.TrimEnd('/')}/getdata.php?map={mapID}&mods=0,{modID}");
                            if (mapData != null)
                            {
                                Datum mapDatum = new Datum("map", $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}", new DataCache() {
                                    { "name", mapData?.map?.title },
                                });
                                if (mapData.image != null)
                                    mapDatum["image"] = $"{mapUrl.TrimEnd('/')}/{mapData.image}";

                                mapDatum.AddObjectPath("game_type:id", mapData?.map?.type);
                                //game.Level["GameMode"] = "Unknown";
                                if (!string.IsNullOrWhiteSpace(mapData?.map?.type))
                                {
                                    switch (mapData?.map?.type)
                                    {
                                        case "D": // Deathmatch
                                            mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));

                                            await gametypeFullAlreadySentLock.WaitAsync();
                                            try
                                            {
                                                if (gametypeFullAlreadySent.Add($"DM"))
                                                    retVal.Add(new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM", new DataCache() { { "name", "Deathmatch" } }), $"game_type\tDM", false));
                                                else
                                                    retVal.Add(new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"), $"game_type\tDM", true));
                                            }
                                            finally
                                            {
                                                gametypeFullAlreadySentLock.Release();
                                            }
                                            
                                            await gamemodeFullAlreadySentLock.WaitAsync();
                                            try
                                            {
                                                if (gamemodeFullAlreadySent.Add($"DM"))
                                                    retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM", new DataCache() { { "name", "Deathmatch" } }), $"game_mode\tDM", false));
                                                else
                                                    retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"), $"game_mode\tDM", true));
                                            }
                                            finally
                                            {
                                                gamemodeFullAlreadySentLock.Release();
                                            }

                                            break;
                                        case "S": // Strategy
                                            mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));

                                            await gametypeFullAlreadySentLock.WaitAsync();
                                            try
                                            {
                                                if (gametypeFullAlreadySent.Add($"STRAT"))
                                                    retVal.Add(new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT", new DataCache() { { "name", "Strategy" } }), $"game_type\tSTRAT", false));
                                                else
                                                    retVal.Add(new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"), $"game_type\tSTRAT", true));
                                            }
                                            finally
                                            {
                                                gametypeFullAlreadySentLock.Release();
                                            }
                                            
                                            await gamemodeFullAlreadySentLock.WaitAsync();
                                            try
                                            {
                                                if (gamemodeFullAlreadySent.Add($"STRAT"))
                                                    retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT", new DataCache() { { "name", "Strategy" } }), $"game_mode\tSTRAT", false));
                                                else
                                                    retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"), $"game_mode\tSTRAT", true));
                                            }
                                            finally
                                            {
                                                gamemodeFullAlreadySentLock.Release();
                                            }

                                            break;
                                        case "K": // King of the Hill
                                            mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}KOTH"));

                                            await gametypeFullAlreadySentLock.WaitAsync();
                                            try
                                            {
                                                if (gametypeFullAlreadySent.Add($"DM"))
                                                    retVal.Add(new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM", new DataCache() { { "name", "Deathmatch" } }), $"game_type\tDM", false));
                                                else
                                                    retVal.Add(new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"), $"game_type\tDM", true));
                                            }
                                            finally
                                            {
                                                gametypeFullAlreadySentLock.Release();
                                            }
                                            
                                            await gamemodeFullAlreadySentLock.WaitAsync();
                                            try
                                            {
                                                if (gamemodeFullAlreadySent.Add($"KOTH"))
                                                    retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}KOTH", new DataCache() { { "name", "King of the Hill" } }), $"game_mode\tKOTH", false));
                                                else
                                                    retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}KOTH"), $"game_mode\tKOTH", true));
                                            }
                                            finally
                                            {
                                                gamemodeFullAlreadySentLock.Release();
                                            }

                                            break;
                                        case "M": // Mission MPI
                                            mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}M_MPI"));

                                            await gametypeFullAlreadySentLock.WaitAsync();
                                            try
                                            {
                                                if (gametypeFullAlreadySent.Add($"STRAT"))
                                                    retVal.Add(new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT", new DataCache() { { "name", "Strategy" } }), $"game_type\tSTRAT", false));
                                                else
                                                    retVal.Add(new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"), $"game_type\tSTRAT", true));
                                            }
                                            finally
                                            {
                                                gametypeFullAlreadySentLock.Release();
                                            }
                                            
                                            await gamemodeFullAlreadySentLock.WaitAsync();
                                            try
                                            {
                                                if (gamemodeFullAlreadySent.Add($"M_MPI"))
                                                    retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}M_MPI", new DataCache() { { "name", "Mission MPI" } }), $"game_mode\tM_MPI", false));
                                                else
                                                    retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}M_MPI"), $"game_mode\tM_MPI", true));
                                            }
                                            finally
                                            {
                                                gamemodeFullAlreadySentLock.Release();
                                            }

                                            break;
                                        case "A": // Action MPI
                                            mapDatum.AddObjectPath("game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                                            mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}A_MPI"));

                                            await gametypeFullAlreadySentLock.WaitAsync();
                                            try
                                            {
                                                if (gametypeFullAlreadySent.Add($"STRAT"))
                                                    retVal.Add(new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT", new DataCache() { { "name", "Strategy" } }), $"game_type\tSTRAT", false));
                                                else
                                                    retVal.Add(new PendingDatum(new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"), $"game_type\tSTRAT", true));
                                            }
                                            finally
                                            {
                                                gametypeFullAlreadySentLock.Release();
                                            }
                                            
                                            await gamemodeFullAlreadySentLock.WaitAsync();
                                            try
                                            {
                                                if (gamemodeFullAlreadySent.Add($"A_MPI"))
                                                    retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}A_MPI", new DataCache() { { "name", "Action MPI" } }), $"game_mode\tA_MPI", false));
                                                else
                                                    retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}A_MPI"), $"game_mode\tA_MPI", true));
                                            }
                                            finally
                                            {
                                                gamemodeFullAlreadySentLock.Release();
                                            }

                                            break;
                                    }
                                }
                                if (!string.IsNullOrWhiteSpace(mapData?.map?.custom_type))
                                {
                                    mapDatum.AddObjectPath("game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{mapData.map.custom_type}"));

                                    await gamemodeFullAlreadySentLock.WaitAsync();
                                    try
                                    {
                                        if (gamemodeFullAlreadySent.Add(mapData.map.custom_type))
                                            retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{mapData.map.custom_type}", new DataCache() { { "name", mapData.map.custom_type_name } }), $"game_mode\t{mapData.map.custom_type}", false));
                                        else
                                            retVal.Add(new PendingDatum(new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{mapData.map.custom_type}"), $"game_mode\t{mapData.map.custom_type}", true));
                                    }
                                    finally
                                    {
                                        gamemodeFullAlreadySentLock.Release();
                                    }
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
                                                if (vehicle.Value.description.ContainsKey("en"))
                                                {
                                                    heroData["description"] = vehicle.Value.description["en"].content;
                                                }
                                                else if (vehicle.Value.description.ContainsKey("default"))
                                                {
                                                    heroData["description"] = vehicle.Value.description["default"].content;
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

                                    foreach (var dr in session.users.Values)
                                    {
                                        string vehicle = mapData.map.vehicles.Where(v => v.EndsWith($":{dr.Vehicle}")).FirstOrDefault();
                                        if (vehicle != null)
                                        {
                                            // stub the hero just in case, even though this stub should NEVER actually occur
                                            retVal.Add(new PendingDatum(new Datum("hero", $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle}"), $"hero\t{vehicle}", true));

                                            // make the player data and shove in our hero
                                            Datum player = new Datum("player", $"{(multiGame ? $"{GameID}:" : string.Empty)}{dr.id}");
                                            player["hero"] = new DatumRef("hero", $"{(multiGame ? $"{GameID}:" : string.Empty)}{vehicle}");
                                            retVal.Add(new PendingDatum(player, null, false));
                                        }
                                    }
                                    mapDatum.AddObjectPath($"allowed_heroes", heroDatumList);
                                }

                                retVal.Add(new PendingDatum(mapDatum, null, false));
                            }
                            return retVal;
                        }));

                        //DelayedDatumTasks
                    }

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
                    if (raw.SatelliteEnabled.HasValue) session.AddObjectPath("level:rules:satellite", raw.SatelliteEnabled.Value);
                    if (raw.BarracksEnabled.HasValue) session.AddObjectPath("level:rules:barracks", raw.BarracksEnabled.Value);
                    if (raw.SniperEnabled.HasValue) session.AddObjectPath("level:rules:sniper", raw.SniperEnabled.Value);
                    if (raw.SplinterEnabled.HasValue) session.AddObjectPath("level:rules:splinter", raw.SplinterEnabled.Value);

                    // unlocked in progress games with SyncJoin will trap the user due to a bug, just list as locked
                    if (raw.SyncJoin.HasValue && raw.SyncJoin.Value && (!raw.IsEnded && raw.IsLaunched))
                    {
                        session.AddObjectPath($"status:{GAMELIST_TERMS.STATUS_LOCKED}", true);
                    }
                    else
                    {
                        session.AddObjectPath($"status:{GAMELIST_TERMS.STATUS_LOCKED}", raw.isLocked);
                    }
                    session.AddObjectPath($"status:{GAMELIST_TERMS.STATUS_PASSWORD}", raw.IsPassworded);
                    
                    string ServerState = Enum.GetName(typeof(ESessionState), raw.IsEnded ? ESessionState.PostGame : raw.IsLaunched ? ESessionState.InGame : ESessionState.PreGame);
                    session.AddObjectPath("status:state", ServerState); // TODO limit this state to our state enumeration
                    session.AddObjectPath("status:other:state", ServerState);

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

                        if (dr.Team.HasValue)
                        {
                            //player.AddObjectPath("team:id", dr.Team.Value.ToString());
                            player.AddObjectPath("ids:slot:id", dr.Team);
                            player.AddObjectPath("index", dr.Team);
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

                                            DelayedDatumTasks.Add(Task.Run(async () =>
                                            {
                                                PlayerSummaryModel playerData = await steamInterface.Users(playerID);
                                                Datum accountDataSteam = new Datum("identity/steam", playerID.ToString(), new DataCache()
                                                {
                                                    { "type", "steam" },
                                                    { "avatar_url", playerData.AvatarFullUrl },
                                                    { "nickname", playerData.Nickname },
                                                    { "profile_url", playerData.ProfileUrl },
                                                });
                                                return new List<PendingDatum> () { new PendingDatum(accountDataSteam, $"identity/steam/{playerID.ToString()}", false) };
                                            }));
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

                                            player.AddObjectPath("ids:steam", new DataCache() {
                                                { "id", playerID.ToString() },
                                                { "raw", dr.id.Substring(1) },
                                                { "identity", new DatumRef("identity/gog", playerID.ToString()) },
                                            });

                                            player.AddObjectPath("ids:gog", new DatumRef("identity/gog", playerID.ToString()));

                                            DelayedDatumTasks.Add(Task.Run(async () =>
                                            {
                                                GogUserData playerData = await gogInterface.Users(playerID);
                                                Datum accountDataGog = new Datum("identity/gog", playerID.ToString(), new DataCache()
                                                {
                                                    { "type", "gog" },
                                                    { "avatar_url", playerData.Avatar.sdk_img_184 ?? playerData.Avatar.large_2x ?? playerData.Avatar.large },
                                                    { "username", playerData.username },
                                                    { "profile_url", $"https://www.gog.com/u/{playerData.username}" },
                                                });
                                                return new List<PendingDatum> () { new PendingDatum(accountDataGog, $"identity/gog/{playerID.ToString()}", false) };
                                            }));
                                        }
                                    }
                                    break;
                            }
                        }

                        yield return player;

                        Players.Add(new DatumRef("player", $"{(multiGame ? $"{GameID}:" : string.Empty)}{dr.id}"));
                    }
                    session["players"] = Players;

                    if (!string.IsNullOrWhiteSpace(raw.clientVersion))
                        session.AddObjectPath("game:version", raw.clientVersion);
                    else if (!string.IsNullOrWhiteSpace(raw.GameVersion))
                        session.AddObjectPath("game:version", raw.GameVersion);

                    if (raw.SyncJoin.HasValue)
                        session.AddObjectPath("attributes:sync_join", raw.SyncJoin.Value);
                    if (raw.MetaDataVersion.HasValue)
                        session.AddObjectPath("attributes:meta_data_version", raw.MetaDataVersion);

                    yield return session;
                }

                while (DelayedDatumTasks.Any())
                {
                    Task<List<PendingDatum>> doneTask = await Task.WhenAny(DelayedDatumTasks);
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
                    DelayedDatumTasks.Remove(doneTask);
                }
            }
        }
    }
}
