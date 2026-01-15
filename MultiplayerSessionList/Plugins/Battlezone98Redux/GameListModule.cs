using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Plugins.BattlezoneCombatCommander;
using MultiplayerSessionList.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.Battlezone98Redux;

[GameListModule(GameID, "Battlezone 98 Redux", true)]
public class GameListModule : IGameListModule
{
    private const string GameID = "bigboat:battlezone_98_redux";

    private readonly string queryUrl = null!;
    private readonly string mapUrl = null!;
    private readonly GogInterface gogInterface;
    private readonly SteamInterface steamInterface;
    private readonly CachedAdvancedWebClient cachedAdvancedWebClient;

    public GameListModule(
        IConfiguration configuration,
        GogInterface gogInterface,
        SteamInterface steamInterface,
        CachedAdvancedWebClient cachedAdvancedWebClient)
    {
        queryUrl = configuration[$"{GameID}:sessions"]!;
        mapUrl = configuration[$"{GameID}:maps"]!;
        if (string.IsNullOrWhiteSpace(queryUrl) || string.IsNullOrWhiteSpace(mapUrl))
            throw new InvalidOperationException($"Critical configuration value for '{GameID}' is missing or empty.");
        
        this.gogInterface = gogInterface;
        this.steamInterface = steamInterface;
        this.cachedAdvancedWebClient = cachedAdvancedWebClient;
    }

    public async IAsyncEnumerable<Datum> GetGameListChunksAsync(
        bool admin,
        bool mock,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var fact_task = cachedAdvancedWebClient.GetObject<Dictionary<string, DataCache>>($"{mapUrl.TrimEnd('/')}/factions.json", TimeSpan.FromHours(24), TimeSpan.FromHours(1));

        // need protection on gamelist being null somehow
        var res = await cachedAdvancedWebClient.GetObject<Dictionary<string, Lobby>>(queryUrl, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
        if (res == null) yield break;
        if (res.Data == null) yield break; // TODO determine what to do in this case

        var gamelist = res.Data;
        if (gamelist == null) yield break;

#if DEBUG
        // any games that don't include CreepingDeath, because he loves to sit in games forever
        mock = mock || (gamelist != null && !gamelist.Where(dr => (dr.Value?.LobbyType ?? Lobby.ELobbyType.Unknown) == Lobby.ELobbyType.Game && (dr.Value?.users?.Values?.Any(dx => dx.id != "S76561199054029199") ?? false)).Any());
#endif
        if (mock)
            gamelist = JsonConvert.DeserializeObject<Dictionary<string, Lobby>>(
                System.IO.File.ReadAllText(@"mock\bigboat\battlezone_98_redux.json"));

        //TODO consider using memberlink for how many players can be in a game, as the text data above (before editing) has a 3/2 if its not used but 3/3 if it is.

        // Generate Source Datums
        DataCache rootLevelSources = new DataCache();
        foreach (var kv in BuildSources(res))
        {
            rootLevelSources[kv.shortId] = kv.data.CreateDatumRef();
            yield return kv.data;
        }

        DynamicAsyncEnumerablePool<Datum> pendingWorkPool = new DynamicAsyncEnumerablePool<Datum>();

        // Generate Session Datums
        DataCache rootLevelLobbies = new DataCache();
        DataCache rootLevelSessions = new DataCache();
        foreach (var datum in BuildSessionsAsync(gamelist, admin, pendingWorkPool, fact_task, cancellationToken))
        {
            switch (datum.Type)
            {
                case GAMELIST_TERMS.TYPE_LOBBY:
                    rootLevelLobbies[datum.ID] = datum.CreateDatumRef();
                    break;

                case GAMELIST_TERMS.TYPE_SESSION:
                    rootLevelSessions[datum.ID] = datum.CreateDatumRef();
                    break;
            }
            yield return datum;
        }

        // Generate Root Datum
        Datum root = new Datum(GAMELIST_TERMS.TYPE_ROOT, GameID, new DataCache() {
            { "sources", rootLevelSources },
            { "lobbies", rootLevelLobbies },
            { "sessions", rootLevelSessions },
        });
        yield return root;

        // Process pending work
        await foreach (var datum in pendingWorkPool.RunUntilEmptyAsync(cancellationToken))
        {
            yield return datum;
        }
    }

    private IEnumerable<Datum> BuildSessionsAsync(
        Dictionary<string, Lobby> gamelist,
        bool admin,
        DynamicAsyncEnumerablePool<Datum> pendingWorkPool,
        Task<CachedData<Dictionary<string, DataCache>>> fact_task,
        CancellationToken cancellationToken)
    {
        // ensure we don't waste time emitting datums we already did
        ConcurrentHashSet<DatumKey> datumsAlreadyQueued = new ConcurrentHashSet<DatumKey>();

        if (gamelist == null) yield break;

        foreach (Lobby raw in gamelist.Values)
        {
            Datum? session;
            switch (raw.LobbyType)
            {
                case Lobby.ELobbyType.Chat:
                    session = new Datum(GAMELIST_TERMS.TYPE_LOBBY, $"{GameID}:Rebellion:B{raw.id}");
                    break;
                case Lobby.ELobbyType.Game:
                    if (raw.isPrivate && !(raw.IsPassworded ?? false))
                        continue;
                    session = new Datum(GAMELIST_TERMS.TYPE_SESSION, $"{GameID}:Rebellion:B{raw.id}");
                    break;
                default:
                    continue;
            }

            // All servers are "listen" servers unless we override this later in a special situation
            // [session|lobby]/type
            session[GAMELIST_TERMS.SESSION_TYPE] = GAMELIST_TERMS.SESSION_TYPE_VALUE_LISTEN;

            // [session|lobby]/name
            session[GAMELIST_TERMS.SESSION_NAME] = raw.Name;

            // [session|lobby]/address/token
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_TOKEN}", $"B{raw.id}");

            // [session|lobby]/address/other/lobby_id
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_OTHER}:lobby_id", raw.id);

            // [session|lobby]/sources/[]
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_SOURCES}:Rebellion", new DatumRef(GAMELIST_TERMS.TYPE_SOURCE, $"{GameID}:Rebellion"));

            // [session|lobby]/player_types/[]
            List<DataCache> PlayerTypes = new List<DataCache>();
            if (raw.LobbyType == Lobby.ELobbyType.Game)
            {
                var playerType = new DataCache()
                {
                    { GAMELIST_TERMS.PLAYERTYPE_TYPES, new List<string>() { GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER } },
                };
                if (raw.PlayerLimit != null)
                    playerType[GAMELIST_TERMS.PLAYERTYPE_MAX] = raw.PlayerLimit;
                PlayerTypes.Add(playerType);
            }
            if (raw.LobbyType == Lobby.ELobbyType.Chat)
            {
                var playerType = new DataCache()
                {
                    { GAMELIST_TERMS.PLAYERTYPE_TYPES, new List<string>() {
                        GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER,
                        GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_BOT,
                    } },
                };
                if (raw.PlayerLimit != null)
                    playerType[GAMELIST_TERMS.PLAYERTYPE_MAX] = raw.PlayerLimit;
                PlayerTypes.Add(playerType);
            }
            session[GAMELIST_TERMS.SESSION_PLAYERTYPES] = PlayerTypes;

            int countBots = 0;
            if (raw.LobbyType == Lobby.ELobbyType.Chat)
                foreach (var dr in raw.users.Values)
                    if (dr.id[0] == 'B')
                        countBots++;

            // [session|lobby]/player_count/player
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_PLAYERCOUNT}:{GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER}", raw.userCount - countBots);

            // lobby/player_count/bot
            if (raw.LobbyType == Lobby.ELobbyType.Chat)
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_PLAYERCOUNT}:{GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_BOT}", countBots);

            if (raw.LobbyType == Lobby.ELobbyType.Game)
            {
                string modID = (raw.WorkshopID ?? @"0");

                if (modID != "0")
                {
                    if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_MOD, modID)))
                    {
                        yield return new Datum(GAMELIST_TERMS.TYPE_MOD, $"{GameID}:{modID}");
                    }

                    DataCache modwrap = new DataCache();

                    // session/game/mods/major/[]/role = main
                    modwrap[GAMELIST_TERMS.MODWRAP_ROLE] = GAMELIST_TERMS.MODWRAP_ROLES_MAIN;

                    // session/game/mods/major/[]/mod
                    modwrap[GAMELIST_TERMS.MODWRAP_MOD] = new DatumRef(GAMELIST_TERMS.TYPE_MOD, $"{GameID}:{modID}");

                    // session/game/mods/major/[]
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_MODS}:{GAMELIST_TERMS.SESSION_GAME_MODS_MAJOR}", new[] { modwrap });
                }

                if (modID == "0")
                {
                    // we aren't concurrent yet so we're safe to just do this
                    if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEBALANCE, "STOCK")))
                        yield return new Datum(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:STOCK", new DataCache() { { GAMELIST_TERMS.GAMEBALANCE_NAME, "Stock" } });

                    // session/game/gamebalance
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_GAMEBALANCE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:STOCK"));
                }

                string mapID = System.IO.Path.GetFileNameWithoutExtension(raw.MapFile).ToLowerInvariant();

                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_MAP, $"{modID}:{mapID}")))
                {
                    Datum mapData = new Datum(GAMELIST_TERMS.TYPE_MAP, $"{GameID}:{modID}:{mapID}");
                    mapData[GAMELIST_TERMS.MAP_MAPFILE] = raw.MapFile.ToLowerInvariant();
                    yield return mapData;

                    if (!string.IsNullOrWhiteSpace(raw.MapFile))
                        pendingWorkPool.Add(BuildDatumsForModAndMapDataAsync(modID, mapID, raw, datumsAlreadyQueued, fact_task));
                }
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_MAP}", new DatumRef(GAMELIST_TERMS.TYPE_MAP, $"{GameID}:{modID}:{mapID}"));
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

                string ServerState = raw.IsEnded ? SESSION_STATE.PostGame : raw.IsLaunched ? SESSION_STATE.InGame : SESSION_STATE.PreGame;
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_STATE}", ServerState);
            }

            // I think only games can have passwords but lets just not filter this anyway
            if (raw.IsPassworded.HasValue)
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_PASSWORD}", raw.IsPassworded);

            List<DatumRef> Players = new List<DatumRef>();
            foreach (var dr in raw.users.Values)
            {
                Datum player = new Datum(GAMELIST_TERMS.TYPE_PLAYER, $"{GameID}:{dr.id}");

                player[GAMELIST_TERMS.PLAYER_NAME] = dr.name;
                if (raw.LobbyType == Lobby.ELobbyType.Chat && dr.id[0] == 'B')
                {
                    player[GAMELIST_TERMS.PLAYER_TYPE] = GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_BOT;
                }
                else
                {
                    player[GAMELIST_TERMS.PLAYER_TYPE] = GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER;
                }
                if (raw.LobbyType == Lobby.ELobbyType.Game)
                {
                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:launched", dr.Launched);
                }
                player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:is_auth", dr.isAuth);
                if (admin)
                {
                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:wan_address", dr.wanAddress);
                    if (dr.lanAddresses.Length > 0)
                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:lan_addresses", dr.lanAddresses);
                }
                if (dr.CommunityPatch != null)
                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:community_patch", dr.CommunityPatch);
                if (dr.CommunityPatchShim != null)
                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_OTHER}:community_patch_shim", dr.CommunityPatchShim);

                if (raw.LobbyType == Lobby.ELobbyType.Game)
                {
                    if (dr.Team.HasValue)
                    {
                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_IDS}:slot:{GAMELIST_TERMS.PLAYER_IDS_X_ID}", dr.Team);
                        player.AddObjectPath(GAMELIST_TERMS.PLAYER_INDEX, dr.Team);
                    }
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
                                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_IDS}:steam", new DataCache() {
                                        { GAMELIST_TERMS.PLAYER_IDS_X_ID, playerID.ToString() },
                                        { GAMELIST_TERMS.PLAYER_IDS_X_RAW, dr.id.Substring(1) },
                                        { GAMELIST_TERMS.PLAYER_IDS_X_IDENTITY, new DatumRef(GAMELIST_TERMS.TYPE_IDENTITYSTEAM, playerID.ToString()) },
                                    });

                                    pendingWorkPool.Add(steamInterface.GetPendingDataAsync(playerID));
                                }
                            }
                            break;
                        case 'G':
                            {
                                ulong playerID = 0;
                                if (ulong.TryParse(dr.id.Substring(1), out playerID))
                                {
                                    playerID = GogInterface.CleanGalaxyUserId(playerID);
                                    player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_IDS}:gog", new DataCache() {
                                        { GAMELIST_TERMS.PLAYER_IDS_X_ID, playerID.ToString() },
                                        { GAMELIST_TERMS.PLAYER_IDS_X_RAW, dr.id.Substring(1) },
                                        { GAMELIST_TERMS.PLAYER_IDS_X_IDENTITY, new DatumRef(GAMELIST_TERMS.TYPE_IDENTITYGOG, playerID.ToString()) },
                                    });

                                    pendingWorkPool.Add(gogInterface.GetPendingDataAsync(playerID));
                                }
                            }
                            break;
                    }
                }

                yield return player;

                Players.Add(new DatumRef(GAMELIST_TERMS.TYPE_PLAYER, $"{GameID}:{dr.id}"));
            }
            session[GAMELIST_TERMS.SESSION_PLAYERS] = Players;

            if (!string.IsNullOrWhiteSpace(raw.clientVersion))
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_VERSION}", raw.clientVersion);
            else if (!string.IsNullOrWhiteSpace(raw.GameVersion))
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_VERSION}", raw.GameVersion);

            if (raw.LobbyType == Lobby.ELobbyType.Game)
            {
                if (raw.SyncJoin.HasValue)
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:sync_join", raw.SyncJoin.Value);
                if (raw.MetaDataVersion.HasValue)
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:meta_data_version", raw.MetaDataVersion);
            }

            yield return session;
        }
    }

    private IEnumerable<(string shortId, Datum data)> BuildSources(CachedData<Dictionary<string, Lobby>> RebellionResult)
    {
        Datum sourceDatum = new Datum(GAMELIST_TERMS.TYPE_SOURCE, $"{GameID}:Rebellion", new DataCache() {
            { GAMELIST_TERMS.SOURCE_NAME, "Rebellion" },
        });
        if (RebellionResult.LastModified != null)
            sourceDatum["timestamp"] = RebellionResult.LastModified;
        yield return ("Rebellion", sourceDatum);
    }

    private async IAsyncEnumerable<Datum> BuildDatumsForModAndMapDataAsync(string modID, string mapID,
        Lobby session,
        ConcurrentHashSet<DatumKey> datumsAlreadyQueued,
        Task<CachedData<Dictionary<string, DataCache>>> fact_task)
    {
        CachedData<MapData>? mapDataC = await cachedAdvancedWebClient.GetObject<MapData>($"{mapUrl.TrimEnd('/')}/getdata2.php?map={mapID}&mods={modID}");
        MapData? mapData = mapDataC?.Data;
        if (mapData != null)
        {
            Datum mapDatum = new Datum(GAMELIST_TERMS.TYPE_MAP, $"{GameID}:{modID}:{mapID}", new DataCache() { });
            if (mapData.map?.title != null)
                mapDatum[GAMELIST_TERMS.MAP_NAME] = mapData.map.title;
            if (mapData.map?.image != null)
                mapDatum[GAMELIST_TERMS.MAP_IMAGE] = $"{mapUrl.TrimEnd('/')}/{mapData.map.image}";

            //mapDatum.AddObjectPath($"{GAMELIST_TERMS.MAP_GAMETYPE}:id", mapData?.map?.type); // this might be broken here
            string? mapType = mapData?.map?.bzcp_type_fix ?? mapData?.map?.bzcp_auto_type_fix ?? mapData?.map?.type;
            string? mapMode = mapData?.map?.bzcp_type_override ?? mapData?.map?.bzcp_auto_type_override ?? mapType;
            if (!string.IsNullOrWhiteSpace(mapType))
            {
                Datum? rv;
                switch (mapType)
                {
                    case "D": // Deathmatch
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:DM"));
                        rv = BuildGameTypeDatum("DM", "Deathmatch", $"{mapUrl.TrimEnd('/')}/resources/icon_d.png", "#B70505", datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "S": // Strategy
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:STRAT"));
                        rv = BuildGameTypeDatum("STRAT", "Strategy", $"{mapUrl.TrimEnd('/')}/resources/icon_s.png", "#007FFF", datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "K": // King of the Hill
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:DM"));
                        rv = BuildGameTypeDatum("DM", "Deathmatch", $"{mapUrl.TrimEnd('/')}/resources/icon_d.png", "#B70505", datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "M": // Mission MPI
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:STRAT"));
                        rv = BuildGameTypeDatum("STRAT", "Strategy", $"{mapUrl.TrimEnd('/')}/resources/icon_s.png", "#007FFF", datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "A": // Action MPI
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:DM"));
                        rv = BuildGameTypeDatum("DM", "Deathmatch", $"{mapUrl.TrimEnd('/')}/resources/icon_d.png", "#B70505", datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "X": // Other
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMETYPE, new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:OTHER"));
                        rv = BuildGameTypeDatum("OTHER", "Other", $"{mapUrl.TrimEnd('/')}/resources/icon_x.png", "#666666", datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                }
            }
            string? MapModeIcon = null;
            string? MapModeColorA = null;
            string? MapModeColorB = null;
            if (!string.IsNullOrWhiteSpace(mapMode))
            {
                Datum? rv;
                switch (mapMode)
                {
                    case "A": // Action MPI
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:A_MPI"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_a.png";
                        MapModeColorA = "#002C00";
                        MapModeColorB = "#007C03";
                        rv = BuildGameModeDatum("A_MPI", "Action MPI", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "C": // Custom
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:CUSTOM"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_c.png";
                        MapModeColorA = "#FFFF00";
                        MapModeColorB = "#FFFF00";
                        rv = BuildGameModeDatum("CUSTOM", "Custom", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "D": // Deathmatch
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:DM"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_d.png";
                        MapModeColorA = "#B70505";
                        MapModeColorB = "#E90707";
                        rv = BuildGameModeDatum("DM", "Deathmatch", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "F": // Capture the Flag
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:CTF"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_f.png";
                        MapModeColorA = "#7F5422";
                        MapModeColorB = "#B0875E";
                        rv = BuildGameModeDatum("CTF", "Capture the Flag", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "G": // Race
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:RACE"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_g.png";
                        MapModeColorA = "#1A1A1A";
                        MapModeColorB = "#EEEEEE";
                        rv = BuildGameModeDatum("RACE", "Race", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "K": // King of the Hill
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:KOTH"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_k.png";
                        MapModeColorA = "#F0772D";
                        MapModeColorB = "#F0772D";
                        rv = BuildGameModeDatum("KOTH", "King of the Hill", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "L": // Loot
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:LOOT"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_l.png";
                        MapModeColorA = "#333333";
                        MapModeColorB = "#BFA88F";
                        rv = BuildGameModeDatum("LOOT", "Loot", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "M": // Mission MPI
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:M_MPI"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_m.png";
                        MapModeColorA = "#B932FF";
                        MapModeColorB = "#B932FF";
                        rv = BuildGameModeDatum("M_MPI", "Mission MPI", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "P": // Pilot/Sniper Deathmatch
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:PILOT"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_p.png";
                        MapModeColorA = "#7A0000";
                        MapModeColorB = "#B70606";
                        rv = BuildGameModeDatum("PILOT", "Pilot Deathmatch", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "Q": // Squad Deathmatch
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:SQUAD"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_q.png";
                        MapModeColorA = "#FF3F00";
                        MapModeColorB = "#FF3F00";
                        rv = BuildGameModeDatum("SQUAD", "Squad Deathmatch", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "R": // Capture the Relic
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:RELIC"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_r.png";
                        MapModeColorA = "#7D007D";
                        MapModeColorB = "#7D007D";
                        rv = BuildGameModeDatum("RELIC", "Capture the Relic", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "S": // Strategy
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:STRAT"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_s.png";
                        MapModeColorA = "#007FFF";
                        MapModeColorB = "#007FFF";
                        rv = BuildGameModeDatum("STRAT", "Strategy", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "W": // Wingman
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:WINGMAN"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_w.png";
                        MapModeColorA = "#0047CF";
                        MapModeColorB = "#0047CF";
                        rv = BuildGameModeDatum("WINGMAN", "Wingman Strategy", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                    case "X": // Other
                        mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:OTHER"));
                        MapModeIcon = $"{mapUrl.TrimEnd('/')}/resources/icon_x.png";
                        MapModeColorA = "#666666";
                        MapModeColorB = "#C3C3C3";
                        rv = BuildGameModeDatum("OTHER", "Other", MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                        if (rv != null)
                            yield return rv;
                        break;
                }
            }
            if (!string.IsNullOrWhiteSpace(mapData?.map?.custom_type))
            {
                mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEMODE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:CUST_{mapData.map.custom_type}"));
                Datum? rv = BuildGameModeDatum($"CUST_{mapData.map.custom_type}", mapData.map.custom_type_name, MapModeIcon, MapModeColorA, MapModeColorB, datumsAlreadyQueued);
                if (rv != null)
                    yield return rv;
            }

            if (mapData?.map?.flags?.Contains("sbp") ?? false)
            {
                mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEBALANCE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:CUST_SBP"));
                Datum? rv = BuildGameBalanceDatum($"CUST_SBP", "Strat Balance Patch", "SBP", "This session uses a mod balance paradigm called \"Strat Balance Patch\" which significantly changes game balance.", datumsAlreadyQueued);
                if (rv != null)
                    yield return rv;
            } else if (mapData?.map?.flags?.Contains("balance_stock") ?? false)
            {
                mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_GAMEBALANCE, new DatumRef(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:STOCK"));
                Datum? rv = BuildGameBalanceDatum($"STOCK", "Stock", null, null, datumsAlreadyQueued);
                if (rv != null)
                    yield return rv;
            }

            if (mapData?.map?.flags?.Contains("sbp_auto_ally_teams") ?? false)
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
                    if (mod.Key == "0") continue;
                    
                    if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_MOD, $"{GameID}:{mod.Key}")))
                    {
                        Datum modData = new Datum(GAMELIST_TERMS.TYPE_MOD, $"{GameID}:{mod.Key}", new DataCache() { });

                        var modName = mod.Value?.name ?? mod.Value?.workshop_name;
                        if (modName != null)
                            modData.Data[GAMELIST_TERMS.MOD_NAME] = modName;

                        if (mod.Value?.image != null)
                            modData.Data[GAMELIST_TERMS.MOD_IMAGE] = $"{mapUrl.TrimEnd('/')}/{mod.Value.image}";

                        if (UInt64.TryParse(mod.Key, out UInt64 modId) && modId > 0)
                            modData.Data[GAMELIST_TERMS.MOD_URL] = $"http://steamcommunity.com/sharedfiles/filedetails/?id={mod.Key}";

                        if (mod.Value?.dependencies != null && mod.Value.dependencies.Count > 0)
                        {
                            // just spam out stubs for dependencies, they're a mess anyway, the reducer at the end will reduce it
                            //foreach (var dep in mod.Value.dependencies)
                            //    retVal.Add(new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_MOD, $"{GameID}:{dep}"), $"{GAMELIST_TERMS.TYPE_MOD}\t{dep}", true));
                            modData.AddObjectPath(GAMELIST_TERMS.MOD_DEPENDENCIES, mod.Value.dependencies.Select(dep => new DatumRef(GAMELIST_TERMS.TYPE_MOD, $"{GameID}:{dep}")));
                        }

                        yield return modData;
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
                    //heroDatumList.Add(new DatumRef("hero", $"{GameID}:{vehicle.Key}"));

                    if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_HERO, $"{GameID}:{vehicle.Key}")))
                    {
                        Datum heroData = new Datum(GAMELIST_TERMS.TYPE_HERO, $"{GameID}:{vehicle.Key}", new DataCache() {
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
                                    heroData[GAMELIST_TERMS.HERO_FACTION] = new DatumRef(GAMELIST_TERMS.TYPE_FACTION, $"{GameID}:{faction}");

                                    if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_FACTION, $"{GameID}:{faction}")))
                                    {
                                        var fd = factionData[faction];
                                        var fact = new DataCache();
                                        fact[GAMELIST_TERMS.FACTION_NAME] = fd["name"];
                                        if (fd.ContainsKey("abbr"))
                                            fact[GAMELIST_TERMS.FACTION_ABBR] = fd["abbr"];
                                        if (fd.ContainsKey("block"))
                                            fact[GAMELIST_TERMS.FACTION_BLOCK] = $"{mapUrl.TrimEnd('/')}/resources/{fd["block"]}";
                                        yield return new Datum(GAMELIST_TERMS.TYPE_FACTION, $"{GameID}:{faction}", fact);
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

                        yield return heroData;
                    }
                    else
                    {
                        // removed this since we're just sending stubs every time now instead via the actually allowed_heroes list
                        // to deal with interlacing spit out some stubs too
                        //retVal.Add(new PendingDatum(new Datum("hero", $"{GameID}:{vehicle.Key}"), $"hero\t{vehicle.Key}", true));
                    }
                }
                //mapDatum.AddObjectPath($"allowed_heroes", heroDatumList);

                List<DatumRef> heroDatumList = new List<DatumRef>();
                if (mapData?.map?.vehicles != null)
                {
                    foreach (var vehicle in mapData.map.vehicles)
                    {
                        // dump a stub for each unit before we add it to our list, just in case, extras will get supressed on the output
                        //retVal.Add(new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_HERO, $"{GameID}:{vehicle}"), $"{GAMELIST_TERMS.TYPE_HERO}\t{vehicle}", true));

                        heroDatumList.Add(new DatumRef(GAMELIST_TERMS.TYPE_HERO, $"{GameID}:{vehicle}"));
                    }
                }

                bool session_teamUpdate = session.PlayerLimit.HasValue && (mapData?.map?.flags?.Contains("sbp_auto_ally_teams") ?? false);
                bool session_syncUpdate = (mapData?.map?.bzcp_type_fix ?? mapData?.map?.bzcp_auto_type_fix ?? mapData?.map?.type) == "S" &&
                                          !(session.SyncJoin ?? false) &&
                                          (mapData?.map?.flags?.Contains("sbp") ?? false);
                bool? session_is_deathmatch = mapData?.map?.mission_dll switch
                {
                    "MultSTMission" => false,
                    "MultDMMission" => true,
                    _ => null,
                };
                Datum? sessionUpdate = null;
                if (session_teamUpdate || session_syncUpdate || session_is_deathmatch.HasValue)
                {
                    sessionUpdate = new Datum(GAMELIST_TERMS.TYPE_SESSION, $"{GameID}:Rebellion:B{session.id}");
                    if (session_teamUpdate && session.PlayerLimit.HasValue)
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
                }

                if (sessionUpdate != null)
                    yield return sessionUpdate;

                foreach (var dr in session.users.Values)
                {
                    string? vehicle = mapData?.map?.vehicles.Where(v => v.EndsWith($":{dr.Vehicle}")).FirstOrDefault();
                    int playerTeam = dr.Team ?? -1;
                    Datum? player = null;
                    if (vehicle != null || (playerTeam > 0 && (mapData?.map?.flags?.Contains("sbp_auto_ally_teams") ?? false)))
                    {
                        player = new Datum(GAMELIST_TERMS.TYPE_PLAYER, $"{GameID}:{dr.id}");

                        if (vehicle != null)
                        {
                            // stub the hero just in case, even though this stub should NEVER actually occur
                            //retVal.Add(new PendingDatum(new Datum(GAMELIST_TERMS.TYPE_HERO, $"{GameID}:{vehicle}"), $"{GAMELIST_TERMS.TYPE_HERO}\t{vehicle}", true));

                            // make the player data and shove in our hero
                            player[GAMELIST_TERMS.PLAYER_HERO] = new DatumRef(GAMELIST_TERMS.TYPE_HERO, $"{GameID}:{vehicle}");
                        }

                        if (mapData?.map?.flags?.Contains("sbp_auto_ally_teams") ?? false)
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

                        yield return player;
                    }
                }
                mapDatum.AddObjectPath(GAMELIST_TERMS.MAP_ALLOWEDHEROES, heroDatumList);
            }

            yield return mapDatum;
        }
    }

    private Datum? BuildGameBalanceDatum(string code, string name, string? name_short, string? note, ConcurrentHashSet<DatumKey> datumsAlreadyQueued)
    {
        if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:{code}")))
        {
            DataCache cache = new DataCache() { { GAMELIST_TERMS.GAMEBALANCE_NAME, name } };
            if (!string.IsNullOrWhiteSpace(name_short))
                cache[GAMELIST_TERMS.GAMEBALANCE_ABBR] = name_short;
            if (!string.IsNullOrWhiteSpace(note))
                cache[GAMELIST_TERMS.GAMEBALANCE_NOTE] = note;
            return new Datum(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:{code}", cache);
        }
        return null;
    }

    private Datum? BuildGameTypeDatum(string code, string name, string icon, string color, ConcurrentHashSet<DatumKey> datumsAlreadyQueued)
    {
        if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:{code}")))
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

            return new Datum(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:{code}", DataCacheItem);
        }
        return null;
    }
    private Datum? BuildGameModeDatum(string code, string name, string? icon, string? colorA, string? colorB, ConcurrentHashSet<DatumKey> datumsAlreadyQueued)
    {
        if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{code}")))
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
            return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{code}", DataCacheItem);
        }
        return null;
    }
}
