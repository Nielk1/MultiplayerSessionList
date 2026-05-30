using System.Net;
using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

namespace MultiplayerSessionList.Plugins.BattlezoneCombatCommander;

[GameListModule(GameID, "Battlezone: Combat Commander", true)]
public class GameListModule : IGameListModule
{
    private const string GameID = "bigboat:battlezone_combat_commander";
    private const string DefaultProxySource = "IonDriver"; // IonDriver is our source and it only lists source for games it proxied from other sources
    private const string DummyNatNegId = "XXXXXXX@XX"; // Dummy NatNegId used by IonDriver's placeholder game which should be ignored. It might not even be valid CustomBase64
    private const string SpamGamePlayerId = "S76561199685297391"; // This player ID is responsible for a lot of fake spam games, so don't show them unless there's at least one other player
    private const string DedicatedServerHostBotPlayerId = "S76561199232890248"; // This is the player ID of the host bot account used by the community dedicated server
    private const string StockModId = "0"; // The stock/default mod is 0, but the game explicitly includes it
    private const string VsrModId = "1325933293"; // The VSR mod has a well known ID which we use to trigger custom additional data
    private const int GameTimeMaxSentinelMinutes = 255; // This game time is the maximum the game supports, so when we see it the game time can't be determined from the data

    private readonly string queryUrl = null!;
    private readonly string mapUrl = null!;
    private readonly GogInterface gogInterface;
    private readonly SteamInterface steamInterface;
    private readonly CachedAdvancedWebClient cachedAdvancedWebClient;
    private readonly DataService dataService;

    public GameListModule(
        IConfiguration configuration,
        GogInterface gogInterface,
        SteamInterface steamInterface,
        CachedAdvancedWebClient cachedAdvancedWebClient,
        DataService dataService)
    {
        queryUrl = configuration[$"{GameID}:sessions"]!;
        mapUrl = configuration[$"{GameID}:maps"]!;
        if (string.IsNullOrWhiteSpace(queryUrl) || string.IsNullOrWhiteSpace(mapUrl))
            throw new InvalidOperationException($"Critical configuration value for '{GameID}' is missing or empty.");

        this.gogInterface = gogInterface;
        this.steamInterface = steamInterface;
        this.cachedAdvancedWebClient = cachedAdvancedWebClient;
        this.dataService = dataService;
    }

    public async IAsyncEnumerable<Datum> GetGameListChunksAsync(
        bool admin,
        bool mock,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var res = await cachedAdvancedWebClient.GetObject<BZCCRaknetData>(
            queryUrl,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(5),
            cancellationToken: cancellationToken);

        if (res == null) yield break;
        if (res.Data == null) yield break; // TODO determine what to do in this case

        var gamelist = res.Data;
        if (gamelist == null) yield break;

//#if DEBUG
//        // any games that aren't my placeholder
//        mock = mock || !gamelist.GET.Where(raw => raw.NATNegID != "XXXXXXX@XX").Any();
//#endif
        if (mock)
            gamelist = JsonSerializer.Deserialize<BZCCRaknetData>(
                System.IO.File.ReadAllText(@"mock\bigboat\battlezone_combat_commander.json"));

        if (gamelist == null)
            yield break;

        // Generate Source Datums
        DataCache rootLevelSources = new DataCache();
        foreach (var kv in BuildSources(gamelist))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rootLevelSources[kv.shortId] = kv.data.CreateDatumRef();
            yield return kv.data;
        }

        DynamicAsyncEnumerablePool<Datum> pendingWorkPool = new DynamicAsyncEnumerablePool<Datum>();

        DateTime curTime = res.LastModified ?? DateTime.UtcNow;

        // Generate Session Datums
        DataCache rootLevelSessions = new DataCache();
        foreach (var datum in BuildSessionsAsync(gamelist, curTime, admin, pendingWorkPool, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (datum.Type == GAMELIST_TERMS.TYPE_SESSION)
                rootLevelSessions[datum.ID] = datum.CreateDatumRef();
            yield return datum;
        }

        // Generate Root Datum
        Datum root = new Datum(GAMELIST_TERMS.TYPE_ROOT, GameID, new DataCache() {
            { "sources", rootLevelSources },
            { "sessions", rootLevelSessions },
        });
        yield return root;

        // Process pending work
        await foreach (var datum in pendingWorkPool.RunUntilEmptyAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return datum;
        }
    }
    private IEnumerable<Datum> BuildSessionsAsync(
        BZCCRaknetData gamelist,
        DateTime curTime,
        bool admin,
        DynamicAsyncEnumerablePool<Datum> pendingWorkPool,
        CancellationToken cancellationToken)
    {
        // ensure we don't waste time emitting datums we already did
        ConcurrentHashSet<DatumKey> datumsAlreadyQueued = new ConcurrentHashSet<DatumKey>();

        if (gamelist == null) yield break;
        if (gamelist.GET == null) yield break;

        foreach (var raw in gamelist.GET)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (raw == null) continue;
            if (raw.NATNegID == DummyNatNegId) continue;
            if (raw.NATNegID == null) continue;

            ProxyStatus? stat = null;
            gamelist.proxyStatus?.TryGetValue(raw.proxySource ?? DefaultProxySource, out stat);
            dataService.Decorate(raw, stat?.updated);

            // if the game's only player is the spam game account and it is locked, ignore it unless we're in admin mode
            if (!admin && raw.Locked && (raw.pl?.All(player => player?.PlayerID == SpamGamePlayerId) ?? false))
                continue;

            // Session ID
            Datum session = new Datum(GAMELIST_TERMS.TYPE_SESSION, $"{GameID}:{raw.proxySource ?? DefaultProxySource}:{raw.NATNegGuid:x16}");

            // All servers are "listen" servers unless we override this later in a special situation
            // session/type
            session[GAMELIST_TERMS.SESSION_TYPE] = GAMELIST_TERMS.SESSION_TYPE_VALUE_LISTEN;

            // session/address/other/nat
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_OTHER}:nat", raw.NATNegID);

            // session/name
            session[GAMELIST_TERMS.SESSION_NAME] = raw.SessionName;

            // session/message
            if (!string.IsNullOrWhiteSpace(raw.MOTD))
                session[GAMELIST_TERMS.SESSION_MESSAGE] = raw.MOTD;

            string modID = raw.Mods?.FirstOrDefault() ?? StockModId;
            string mapID = raw.MapFile?.ToLowerInvariant() ?? string.Empty;

            // session/level/map
            if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_MAP, $"{modID}:{mapID}")))
            {
                Datum mapData = new Datum(GAMELIST_TERMS.TYPE_MAP, $"{GameID}:{modID}:{mapID}");
                if (!string.IsNullOrWhiteSpace(raw.MapFile))
                    mapData[GAMELIST_TERMS.MAP_MAPFILE] = raw.MapFile + @".bzn";
                yield return mapData; // emit a stub that has only the map/map_file

                if (!string.IsNullOrWhiteSpace(raw.MapFile))
                    pendingWorkPool.Add(BuildDatumsForMapDataAsync(modID, mapID, datumsAlreadyQueued, cancellationToken));
            }

            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_MAP}", new DatumRef(GAMELIST_TERMS.TYPE_MAP, $"{GameID}:{modID}:{mapID}"));

            // session/status/locked
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_LOCKED}", raw.Locked);

            // session/status/password
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_PASSWORD}", raw.Passworded);

            var (serverState, includeStateTime) = DetermineServerState(raw);
            ApplyServerState(session, serverState);

            if (raw.Mods != null)
            {
                int modsLen = raw.Mods.Length;

                if (modsLen > 0)
                {
                    if (raw.Mods[0] != StockModId)
                    {
                        DataCache modwrap = new DataCache();

                        // session/game/mods/major/[]/role = main
                        modwrap[GAMELIST_TERMS.MODWRAP_ROLE] = GAMELIST_TERMS.MODWRAP_ROLES_MAIN;

                        // session/game/mods/major/[]/mod
                        modwrap[GAMELIST_TERMS.MODWRAP_MOD] = new DatumRef(GAMELIST_TERMS.TYPE_MOD, $"{GameID}:{raw.Mods[0]}");

                        // session/game/mods/major/[]
                        session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_MODS}:{GAMELIST_TERMS.SESSION_GAME_MODS_MAJOR}", new[] { modwrap });

                        if (raw.Mods[0] == VsrModId)
                        {
                            if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:VSR")))
                                yield return new Datum(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:VSR", new DataCache() {
                                    { GAMELIST_TERMS.GAMEBALANCE_NAME, "Vet Strategy Recycler Variant" },
                                    { GAMELIST_TERMS.GAMEBALANCE_ABBR, "VSR" },
                                    { GAMELIST_TERMS.GAMEBALANCE_NOTE, "This session uses a mod balance paradigm which emphasizes players over AI units and enables flight through the exploitation of physics quirks." }
                                });

                            // session/game/game_balance
                            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_GAMEBALANCE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:VSR"));
                        }
                    }
                    else
                    {
                        // we aren't concurrent yet so we're safe to just do this
                        if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:STOCK")))
                            yield return new Datum(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:STOCK", new DataCache() {
                                { GAMELIST_TERMS.GAMEBALANCE_NAME, "Stock" }
                            });

                        // session/game/game_balance
                        session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_GAMEBALANCE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEBALANCE, $"{GameID}:STOCK"));
                    }
                }
                if (modsLen > 1)
                {
                    // this is the legacy path where dependencies get spun out as minor mods, only pre-community-patch does this
                    var dependencyMods = raw?.Mods.Skip(1).Select(m =>
                    {
                        DataCache modwrap = new DataCache();

                        // session/game/mods/minor/[]/role = dependency
                        modwrap[GAMELIST_TERMS.MODWRAP_ROLE] = GAMELIST_TERMS.MODWRAP_ROLES_DEPENDENCY;

                        // session/game/mods/minor/[]/mod
                        modwrap[GAMELIST_TERMS.MODWRAP_MOD] = new DatumRef(GAMELIST_TERMS.TYPE_MOD, $"{GameID}:{m}");

                        return modwrap;
                    }).ToArray();

                    // session/game/mods/minor/[]
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_MODS}:{GAMELIST_TERMS.SESSION_GAME_MODS_MINOR}", dependencyMods);
                }

                // session/game/version
                if (!string.IsNullOrWhiteSpace(raw.v))
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_VERSION}", raw.v);

                // session/other/tps
                if (raw.TPS.HasValue && raw.TPS > 0)
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:tps", raw.TPS);

                // session/other/max_ping
                if (raw.MaxPing.HasValue && raw.MaxPing > 0)
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:max_ping", raw.MaxPing);

                // session/other/worst_ping
                if (raw.MaxPingSeen.HasValue && raw.MaxPingSeen > 0)
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:worst_ping", raw.MaxPingSeen);

                // session/level/rules/time_minutes
                if (raw.TimeLimit.HasValue && raw.TimeLimit > 0)
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:time_limit", raw.TimeLimit);

                // session/level/rules/kill_limit
                if (raw.KillLimit.HasValue && raw.KillLimit > 0)
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:kill_limit", raw.KillLimit);

                // session/address/other/nat_type
                if (raw.NATType.HasValue)
                {
                    if (Enum.IsDefined(raw.NATType.Value))
                        session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_OTHER}:nat_type", raw.NATType.Value.ToString().Replace('_', ' '));
                    else
                        session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_OTHER}:nat_type", $"[" + raw.NATType + "]");
                }
            }

            ApplySessionSource(session, raw);

            bool teamsOn = false;
            bool onlyOneTeam = false;

            switch (raw.GameType)
            {
                case 0:
                    // removed this as it's invalid, will probably need to use maps to override it via manual metadata
                    //session.AddObjectPath($"level:game_type", "All"); // TODO we saw this on a retaliation MPI, WTF?
                    break;
                case 1:
                    if (raw.GameSubType != null)
                    {
                        int GetGameModeOutput = raw.GameSubType.Value % (int)EGameMode.GAMEMODE_MAX; // extract if we are team or not
                        int detailed = raw.GameSubType.Value / (int)EGameMode.GAMEMODE_MAX; // ivar7
                        bool RespawnSameRace = (detailed & 256) == 256;
                        bool RespawnAnyRace = (detailed & 512) == 512;
                        session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:respawn", RespawnSameRace ? "Race" : RespawnAnyRace ? "Any" : "One");
                        detailed = (detailed & 0xff);

                        switch ((EGameMode)GetGameModeOutput)
                        {
                            case EGameMode.GAMEMODE_TEAM_DM:
                            case EGameMode.GAMEMODE_TEAM_KOTH:
                            case EGameMode.GAMEMODE_TEAM_CTF:
                            case EGameMode.GAMEMODE_TEAM_LOOT:
                            case EGameMode.GAMEMODE_TEAM_RACE:
                                teamsOn = true;
                                break;
                            case EGameMode.GAMEMODE_DM:
                            case EGameMode.GAMEMODE_KOTH:
                            case EGameMode.GAMEMODE_CTF:
                            case EGameMode.GAMEMODE_LOOT:
                            case EGameMode.GAMEMODE_RACE:
                            default:
                                teamsOn = false;
                                break;
                        }

                        session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMETYPE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:DM"));
                        if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMETYPE, $"DM")))
                            yield return new Datum(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:DM", new DataCache() { { GAMELIST_TERMS.GAMETYPE_NAME, "Deathmatch" } });

                        switch (detailed) // first byte of ivar7?  might be all of ivar7 // Deathmatch subtype (0 = normal; 1 = KOH; 2 = CTF; add 256 for random respawn on same race, or add 512 for random respawn w/o regard to race)
                        {
                            case 0: // Deathmatch
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMEMODE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}DM"));
                                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(teamsOn ? "TEAM_" : string.Empty)}DM")))
                                    yield return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}DM", new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, $"{(teamsOn ? "Team " : string.Empty)}Deathmatch" } });
                                break;
                            case 1: // King of the Hill
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMEMODE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}KOTH"));
                                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(teamsOn ? "TEAM_" : string.Empty)}KOTH")))
                                    yield return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}KOTH", new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, $"{(teamsOn ? "Team " : string.Empty)}King of the Hill" } });
                                break;
                            case 2: // Capture the Flag
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMEMODE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}CTF"));
                                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(teamsOn ? "TEAM_" : string.Empty)}CTF")))
                                    yield return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}CTF", new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, $"{(teamsOn ? "Team " : string.Empty)}Capture the Flag" } });
                                break;
                            case 3: // Loot
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMEMODE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}LOOT"));
                                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(teamsOn ? "TEAM_" : string.Empty)}LOOT")))
                                    yield return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}LOOT", new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, $"{(teamsOn ? "Team " : string.Empty)}Loot" } });
                                break;
                            case 4: // DM [RESERVED]
                                break;
                            case 5: // Race
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMEMODE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}RACE"));
                                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(teamsOn ? "TEAM_" : string.Empty)}RACE")))
                                    yield return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}RACE", new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, $"{(teamsOn ? "Team " : string.Empty)}Race" } });
                                break;
                            case 6: // Race (Vehicle Only)
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMEMODE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}RACE"));
                                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(teamsOn ? "TEAM_" : string.Empty)}RACE")))
                                    yield return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}RACE", new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, $"{(teamsOn ? "Team " : string.Empty)}Race" } });
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:vehicle_only", true);
                                break;
                            case 7: // DM (Vehicle Only)
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMEMODE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}DM"));
                                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, $"{(teamsOn ? "TEAM_" : string.Empty)}DM")))
                                    yield return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:{(teamsOn ? "TEAM_" : string.Empty)}DM", new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, $"{(teamsOn ? "Team " : string.Empty)}Deathmatch" } });
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_RULES}:vehicle_only", true);
                                break;
                            default:
                                //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : string.Empty) + "DM [UNKNOWN {raw.GameSubType}]";
                                break;
                        }
                    }
                    break;
                case 2:
                    if (raw.GameSubType != null)
                    {
                        int GetGameModeOutput = raw.GameSubType.Value % (int)EGameMode.GAMEMODE_MAX; // extract if we are team or not

                        session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMETYPE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:STRAT"));
                        if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMETYPE, $"STRAT")))
                            yield return new Datum(GAMELIST_TERMS.TYPE_GAMETYPE, $"{GameID}:STRAT", new DataCache() { { GAMELIST_TERMS.GAMETYPE_NAME, "Strategy" } });

                        switch ((EGameMode)GetGameModeOutput)
                        {
                            case EGameMode.GAMEMODE_TEAM_STRAT:
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMEMODE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:STRAT"));
                                teamsOn = true;
                                onlyOneTeam = false;
                                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, "STRAT")))
                                    yield return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:STRAT", new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, "Team Strategy" } });
                                break;
                            case EGameMode.GAMEMODE_STRAT:
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMEMODE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:FFA"));
                                teamsOn = false;
                                onlyOneTeam = false;
                                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, "FFA")))
                                    yield return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:FFA", new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, "Free for All" } });
                                break;
                            case EGameMode.GAMEMODE_MPI:
                                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMEMODE}", new DatumRef(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:MPI"));
                                teamsOn = true;
                                onlyOneTeam = true;
                                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, "MPI")))
                                    yield return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:MPI", new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, "Multiplayer Instant Action" } });
                                break;
                            default:
                                //game.Level["GameType"] = $"STRAT [UNKNOWN {GetGameModeOutput}]";
                                if (datumsAlreadyQueued.Add(new DatumKey(GAMELIST_TERMS.TYPE_GAMEMODE, $"UNK{GetGameModeOutput}")))
                                    yield return new Datum(GAMELIST_TERMS.TYPE_GAMEMODE, $"{GameID}:UNK{GetGameModeOutput}", new DataCache() { { GAMELIST_TERMS.GAMEMODE_NAME, $"{GetGameModeOutput}" } });
                                break;
                        }
                    }
                    break;
                case 3: // impossible, BZCC limits to 0-2
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_GAMETYPE}", $"{GameID}:MPI"); //  "MPI [Invalid]";
                    break;
            }

            if (!string.IsNullOrWhiteSpace(raw?.d))
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_OTHER}:mod_hash", raw.d); // base64 encoded CRC32

            bool specialDedicatedServer = ApplyPlayers(raw, session, teamsOn, onlyOneTeam, pendingWorkPool, cancellationToken);
            ApplyPlayerTypeSummary(raw, session, specialDedicatedServer);
            ApplySessionTime(session, raw, curTime, includeStateTime, serverState);
            ApplyTeamData(session, raw, teamsOn, onlyOneTeam);

            yield return session;
        }

        yield break;
    }

    private IEnumerable<(string shortId, Datum data)> BuildSources(BZCCRaknetData gamelist)
    {
        if (gamelist?.proxyStatus == null) yield break;

        foreach (var proxyStatus in gamelist.proxyStatus)
        {
            if (proxyStatus.Value == null) continue;

            var datum = new Datum(GAMELIST_TERMS.TYPE_SOURCE, $"{GameID}:{proxyStatus.Key}", new DataCache
        {
            { GAMELIST_TERMS.SOURCE_NAME, proxyStatus.Key },
            { "status", proxyStatus.Value.status }
        });
            if (proxyStatus.Value.success != null)
                datum["success"] = proxyStatus.Value.success;
            if (proxyStatus.Value.updated != null)
                datum["timestamp"] = proxyStatus.Value.updated;
            yield return (proxyStatus.Key, datum);
        }
    }

    private async IAsyncEnumerable<Datum> BuildDatumsForMapDataAsync(
        string modID,
        string mapID,
        ConcurrentHashSet<DatumKey> datumsAlreadyQueued,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var encodedMapId = WebUtility.UrlEncode(mapID);
        var encodedModId = WebUtility.UrlEncode(modID);

        CachedData<MapData>? mapDataC = await cachedAdvancedWebClient.GetObject<MapData>(
            $"{mapUrl.TrimEnd('/')}/getdata.php?map={encodedMapId}&mod={encodedModId}",
            cancellationToken: cancellationToken);

        MapData? mapData = mapDataC?.Data;
        if (mapData == null)
            yield break;

        Datum mapDatum = new Datum(GAMELIST_TERMS.TYPE_MAP, $"{GameID}:{modID}:{mapID}", new DataCache() {
            { GAMELIST_TERMS.MAP_NAME, mapData?.title },
            { GAMELIST_TERMS.MAP_DESCRIPTION, mapData?.description },
            { GAMELIST_TERMS.MAP_MAPFILE, mapID + @".bzn" },
        });
        if (mapData?.image != null)
            mapDatum[GAMELIST_TERMS.MAP_IMAGE] = $"{mapUrl.TrimEnd('/')}/{mapData.image}";
        if (mapData?.netVars != null && (mapData?.netVars?.Count ?? 0) > 0)
        {
            if (mapData != null && mapData.netVars.ContainsKey("ivar3") && mapData.netVars["ivar3"] == "1")
            {
                if (mapData.netVars.ContainsKey("svar1")) mapDatum.AddObjectPath($"{GAMELIST_TERMS.MAP_TEAMS}:1:{GAMELIST_TERMS.MAP_TEAMS_X_NAME}", mapData.netVars["svar1"]);
                if (mapData.netVars.ContainsKey("svar2")) mapDatum.AddObjectPath($"{GAMELIST_TERMS.MAP_TEAMS}:2:{GAMELIST_TERMS.MAP_TEAMS_X_NAME}", mapData.netVars["svar2"]);
            }
        }
        yield return mapDatum;

        if (mapData?.mods != null)
        {
            foreach (var mod in mapData.mods)
            {
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
                        modData.AddObjectPath(GAMELIST_TERMS.MOD_DEPENDENCIES, mod.Value.dependencies.Select(dep => new DatumRef(GAMELIST_TERMS.TYPE_MOD, $"{GameID}:{dep}")));
                    }

                    yield return modData;
                }
            }
        }
    }





















    private static (string? serverState, bool includeStateTime) DetermineServerState(BZCCGame raw)
    {
        string? serverState = null;
        bool includeStateTime = false;

        if (raw.ServerInfoMode.HasValue)
        {
            switch (raw.ServerInfoMode)
            {
                case EServerInfoMode.Unknown:
                    serverState = SESSION_STATE.Unknown;
                    break;
                case EServerInfoMode.OpenWaiting:
                case EServerInfoMode.ClosedWaiting:
                    includeStateTime = true;
                    if (raw.pl?.Any(dr => dr != null && ((dr.Score ?? 0) != 0 || (dr.Deaths ?? 0) != 0 || (dr.Kills ?? 0) != 0)) ?? false)
                        // PreGame status applied in error, players have in-game sourced data
                        serverState = SESSION_STATE.InGame;
                    else
                        serverState = SESSION_STATE.PreGame;
                    break;
                case EServerInfoMode.OpenPlaying:
                case EServerInfoMode.ClosedPlaying:
                    includeStateTime = true;
                    serverState = SESSION_STATE.InGame;
                    break;
                case EServerInfoMode.Exiting:
                    serverState = SESSION_STATE.PostGame;
                    break;
            }
        }

        return (serverState, includeStateTime);
    }

    private static void ApplyServerState(Datum session, string? serverState)
    {
        if (!string.IsNullOrWhiteSpace(serverState))
        {
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_STATE}", serverState);
        }
    }

    private static void ApplySessionSource(Datum session, BZCCGame raw)
    {
        // session/sources/[source(short)]
        session.AddObjectPath(
            $"{GAMELIST_TERMS.SESSION_SOURCES}:{(raw.proxySource ?? DefaultProxySource)}",
            new DatumRef(GAMELIST_TERMS.TYPE_SOURCE, $"{GameID}:{(raw.proxySource ?? DefaultProxySource)}"));
    }

    private bool ApplyPlayers(
        BZCCGame raw,
        Datum session,
        bool teamsOn,
        bool onlyOneTeam,
        DynamicAsyncEnumerablePool<Datum> pendingWorkPool,
        CancellationToken cancellationToken)
    {
        bool specialDedicatedServer = false;
        if (raw.pl == null)
            return specialDedicatedServer;

        List<DataCache> players = new List<DataCache>();
        for (int playerIndex = 0; playerIndex < raw.pl.Length; playerIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dr = raw.pl[playerIndex];
            if (dr == null) continue; // If slot 0 is invalid, host is unknown by design.

            DataCache player = new DataCache();
            player[GAMELIST_TERMS.PLAYER_NAME] = dr.Name;
            player[GAMELIST_TERMS.PLAYER_TYPE] = GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER;

            if (playerIndex == 0)
            {
                player[GAMELIST_TERMS.PLAYER_ISHOST] = true;

                // dedicated bot detection constant
                if (!string.IsNullOrWhiteSpace(dr.PlayerID) && dr.PlayerID == DedicatedServerHostBotPlayerId)
                {
                    player[GAMELIST_TERMS.PLAYER_TYPE] = GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_BOT;
                    specialDedicatedServer = true;
                }
            }

            if ((dr.Team ?? 255) != 255) // 255 means not on a team yet, could be understood as -1
            {
                if (teamsOn)
                {
                    if (!onlyOneTeam)
                    {
                        if (dr.Team >= 1 && dr.Team <= 5) player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_ID}", "1");
                        if (dr.Team >= 6 && dr.Team <= 10) player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_ID}", "2");
                        if (dr.Team == 1 || dr.Team == 6) player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_LEADER}", true);
                        if (dr.Team >= 1 && dr.Team <= 10) player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_INDEX}", (dr.Team - 1) % 5);
                    }
                    else // MPI, only teams 1-5 should be valid but let's assume all are valid
                    {
                        // TODO confirm if map data might need to influence this
                        player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_ID}", "1");
                        if (dr.Team == 1) player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_LEADER}", true);
                        if (dr.Team >= 1) player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_TEAM}:{GAMELIST_TERMS.PLAYER_TEAM_INDEX}", dr.Team - 1);
                    }
                }

                player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_IDS}:slot:{GAMELIST_TERMS.PLAYER_IDS_X_ID}", dr.Team);
                player.AddObjectPath(GAMELIST_TERMS.PLAYER_INDEX, dr.Team);
            }

            if (dr.Kills.HasValue) player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_STATS}:kills", dr.Kills);
            if (dr.Deaths.HasValue) player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_STATS}:deaths", dr.Deaths);
            if (dr.Score.HasValue) player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_STATS}:score", dr.Score);

            if (!string.IsNullOrWhiteSpace(dr.PlayerID))
            {
                player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_IDS}:bzr_net:{GAMELIST_TERMS.PLAYER_IDS_X_ID}", dr.PlayerID);

                if (dr.PlayerID.Length > 1)
                {
                    switch (dr.PlayerID[0])
                    {
                        case 'S':
                            if (ulong.TryParse(dr.PlayerID.Substring(1), out var steamId))
                            {
                                player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_IDS}:steam", new DataCache() {
                                    { GAMELIST_TERMS.PLAYER_IDS_X_ID, steamId.ToString() },
                                    { GAMELIST_TERMS.PLAYER_IDS_X_RAW, dr.PlayerID.Substring(1) },
                                    { GAMELIST_TERMS.PLAYER_IDS_X_IDENTITY, new DatumRef(GAMELIST_TERMS.TYPE_IDENTITYSTEAM, steamId.ToString()) },
                                });

                                pendingWorkPool.Add(steamInterface.GetPendingDataAsync(steamId));
                            }
                            break;

                        case 'G':
                            if (ulong.TryParse(dr.PlayerID.Substring(1), out var gogIdRaw))
                            {
                                var gogId = GogInterface.CleanGalaxyUserId(gogIdRaw);
                                player.AddObjectPath($"{GAMELIST_TERMS.PLAYER_IDS}:gog", new DataCache() {
                                    { GAMELIST_TERMS.PLAYER_IDS_X_ID, gogId.ToString() },
                                    { GAMELIST_TERMS.PLAYER_IDS_X_RAW, dr.PlayerID.Substring(1) },
                                    { GAMELIST_TERMS.PLAYER_IDS_X_IDENTITY, new DatumRef(GAMELIST_TERMS.TYPE_IDENTITYGOG, gogId.ToString()) },
                                });

                                pendingWorkPool.Add(gogInterface.GetPendingDataAsync(gogId));
                            }
                            break;
                    }
                }
            }

            players.Add(player);
        }

        session[GAMELIST_TERMS.SESSION_PLAYERS] = players;
        return specialDedicatedServer;
    }

    private static void ApplyPlayerTypeSummary(BZCCGame raw, Datum session, bool specialDedicatedServer)
    {
        // TODO spectators

        if (specialDedicatedServer)
        {
            // dedicated server
            var dedicatedMaxPlayers = raw.MaxPlayers.HasValue ? Math.Max(0, raw.MaxPlayers.Value - 1) : 0;
            var dedicatedCurPlayers = Math.Max(0, raw.CurPlayers - 1);

            session[GAMELIST_TERMS.SESSION_PLAYERTYPES] = new List<DataCache>
            {
                new DataCache
                {
                    { GAMELIST_TERMS.PLAYERTYPE_TYPES, new List<string>() { GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER } },
                    { GAMELIST_TERMS.PLAYERTYPE_MAX, dedicatedMaxPlayers },
                }
            };

            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_PLAYERCOUNT}:{GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER}", dedicatedCurPlayers);
            session[GAMELIST_TERMS.SESSION_TYPE] = GAMELIST_TERMS.SESSION_TYPE_VALUE_DEDICATED;
        }
        else
        {
            // normal player data
            session[GAMELIST_TERMS.SESSION_PLAYERTYPES] = new List<DataCache>
            {
                new DataCache
                {
                    { GAMELIST_TERMS.PLAYERTYPE_TYPES, new List<string>() { GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER } },
                    { GAMELIST_TERMS.PLAYERTYPE_MAX, raw.MaxPlayers },
                }
            };

            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_PLAYERCOUNT}:{GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_PLAYER}", raw.CurPlayers);
        }
    }

    private static void ApplySessionTime(Datum session, BZCCGame raw, DateTime curTime, bool includeStateTime, string? serverState)
    {
        if (!includeStateTime)
            return;

        if (raw.GameTimeMinutes.HasValue && (raw.GameTimeMinutes.Value != GameTimeMaxSentinelMinutes || !raw.GameStateStarted.HasValue))
        {
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TIME}:{GAMELIST_TERMS.SESSION_TIME_SECONDS}", raw.GameTimeMinutes * 60);
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TIME}:{GAMELIST_TERMS.SESSION_TIME_RESOLUTION}", 60);
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TIME}:{GAMELIST_TERMS.SESSION_TIME_MAX}", raw.GameTimeMinutes.Value == GameTimeMaxSentinelMinutes);
            if (!string.IsNullOrWhiteSpace(serverState))
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TIME}:{GAMELIST_TERMS.SESSION_TIME_CONTEXT}", serverState);
        }
        else if (raw.GameStateStarted.HasValue)
        {
            int min = (int)(curTime - raw.GameStateStarted.Value).TotalMinutes;
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TIME}:{GAMELIST_TERMS.SESSION_TIME_SECONDS}", min * 60);
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TIME}:{GAMELIST_TERMS.SESSION_TIME_RESOLUTION}", 60);
            if (!string.IsNullOrWhiteSpace(serverState))
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TIME}:{GAMELIST_TERMS.SESSION_TIME_CONTEXT}", serverState);
        }
    }

    private static void ApplyTeamData(Datum session, BZCCGame raw, bool teamsOn, bool onlyOneTeam)
    {
        if (!teamsOn)
            return;

        session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TEAMS}:1:{GAMELIST_TERMS.SESSION_TEAMS_X_HUMAN}", true);
        session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TEAMS}:1:{GAMELIST_TERMS.SESSION_TEAMS_X_COMPUTER}", false);
        //if ((mapData?.netVars?.Count ?? 0) > 0)
        //{
        //    if (mapData.netVars.ContainsKey("svar1")) game.Teams.AddObjectPath("1:Name", mapData.netVars["svar1"]);
        //    if (mapData.netVars.ContainsKey("svar2")) game.Teams.AddObjectPath("2:Name", mapData.netVars["svar2"]);
        //}

        if (!onlyOneTeam)
        {
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TEAMS}:2:{GAMELIST_TERMS.SESSION_TEAMS_X_HUMAN}", true);
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TEAMS}:2:{GAMELIST_TERMS.SESSION_TEAMS_X_COMPUTER}", false);
            if (raw.MaxPlayers.HasValue)
            {
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TEAMS}:1:{GAMELIST_TERMS.SESSION_TEAMS_X_MAX}", Math.Min(5, raw.MaxPlayers.Value - 1));
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TEAMS}:2:{GAMELIST_TERMS.SESSION_TEAMS_X_MAX}", Math.Min(5, raw.MaxPlayers.Value - 1));
            }
        }
        else
        {
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TEAMS}:2:{GAMELIST_TERMS.SESSION_TEAMS_X_HUMAN}", false);
            session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TEAMS}:2:{GAMELIST_TERMS.SESSION_TEAMS_X_COMPUTER}", true);
            if (raw.MaxPlayers.HasValue)
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TEAMS}:1:{GAMELIST_TERMS.SESSION_TEAMS_X_MAX}", Math.Min(5, raw.MaxPlayers.Value));
        }
    }















}
