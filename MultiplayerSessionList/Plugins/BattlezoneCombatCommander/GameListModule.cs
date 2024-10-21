using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Plugins.Battlezone98Redux;
using MultiplayerSessionList.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.BattlezoneCombatCommander
{
    public class GameListModule : IGameListModule
    {
        public string GameID => "bigboat:battlezone_combat_commander";
        public string Title => "Battlezone: Combat Commander";
        public bool IsPublic => true;

        enum GameMode : int
        {
            GAMEMODE_UNKNOWN,
            GAMEMODE_DM,
            GAMEMODE_TEAM_DM,
            GAMEMODE_KOTH,
            GAMEMODE_TEAM_KOTH,
            GAMEMODE_CTF,
            GAMEMODE_TEAM_CTF,
            GAMEMODE_LOOT,
            GAMEMODE_TEAM_LOOT = 8,
            GAMEMODE_RACE = 9,
            GAMEMODE_TEAM_RACE = 10,
            GAMEMODE_STRAT = 11,
            GAMEMODE_TEAM_STRAT = 12,
            GAMEMODE_MPI = 13,

            GAMEMODE_MAX // Must be last, thank you.
        };

        private string queryUrl;
        private string mapUrl;
        private GogInterface gogInterface;
        private SteamInterface steamInterface;
        private CachedAdvancedWebClient cachedAdvancedWebClient;

        public GameListModule(IConfiguration configuration, GogInterface gogInterface, SteamInterface steamInterface, CachedAdvancedWebClient cachedAdvancedWebClient)
        {
            queryUrl = configuration["bigboat:battlezone_combat_commander:sessions"];
            mapUrl = configuration["bigboat:battlezone_combat_commander:maps"];
            this.gogInterface = gogInterface;
            this.steamInterface = steamInterface;
            this.cachedAdvancedWebClient = cachedAdvancedWebClient;
        }

        const string base64Chars    = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        const string altBase64Chars = "@123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_";

        private byte[] Base64DecodeBinary(string base64)
        {
            string newBase64 = new string(base64.Select(c => { int idx = altBase64Chars.IndexOf(c); return idx < 0 ? c : base64Chars[altBase64Chars.IndexOf(c)]; }).ToArray());
            newBase64 = newBase64.PadRight((newBase64.Length + 3) & ~3, '=');
            return Convert.FromBase64String(newBase64);
        }

        public async IAsyncEnumerable<Datum> GetGameListChunksAsync(bool multiGame, bool admin, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            TaskFactory taskFactory = new TaskFactory(cancellationToken);

            if (!multiGame)
                yield return new Datum("default", "session", new DataCache() { { "type", GAMELIST_TERMS.TYPE_LISTEN } });//, true);

            var res = await cachedAdvancedWebClient.GetObject<string>(queryUrl, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
            if (admin) yield return new Datum("debug", "raw", new DataCache () { { "raw", res.Data } });

            var gamelist = JsonConvert.DeserializeObject<BZCCRaknetData>(res.Data);


            foreach (var proxyStatus in gamelist.proxyStatus)
                yield return new Datum("source", $"{(multiGame ? $"{GameID}:" : string.Empty)}{proxyStatus.Key}", new DataCache() {
                    { "name", proxyStatus.Key },
                    { "status", proxyStatus.Value.status },
                    { "success", proxyStatus.Value.success },
                    { "timestamp", proxyStatus.Value.updated },
                });

            HashSet<string> DontSendStub = new HashSet<string>();

            Dictionary<(string mod, string map), Task<MapData>> MapDataFetchTasks = new Dictionary<(string mod, string map), Task<MapData>>();
            List<Task<List<PendingDatum>>> DelayedDatumTasks = new List<Task<List<PendingDatum>>>();

            SemaphoreSlim modsAlreadyReturnedLock = new SemaphoreSlim(1, 1);
            HashSet<string> modsAlreadyReturnedFull = new HashSet<string>();

            HashSet<string> modStubAlreadySent = new HashSet<string>();
            HashSet<string> mapStubAlreadySent = new HashSet<string>();
            HashSet<string> gametypeFullAlreadySent = new HashSet<string>();
            HashSet<string> gametypeStubAlreadySent = new HashSet<string>();
            HashSet<string> gamemodeFullAlreadySent = new HashSet<string>();
            HashSet<string> gamemodeStubAlreadySent = new HashSet<string>();

            //yield return new Datum("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}0", new DataCache() { { "name", "Stock" } });
            //modsAlreadyReturnedFull.Add("0"); // full data for stock already returned as there's so little data for it, remove this if stock gets more data
            //DontSendStub.Add("mod\t0"); // we already sent the full data for stock, don't send stubs

            foreach (var raw in gamelist.GET)
            {
                // ignore dummy games
                if (raw.g == "XXXXXXX@XX")
                    continue;

                //byte[] natNegId = Base64DecodeBinary(raw.g);
                //var natNegIdPad = new byte[8];
                //var startAt = natNegIdPad.Length - natNegId.Length;
                //Array.Copy(natNegId, 0, natNegIdPad, startAt, natNegId.Length);
                //Datum game = new Datum("session", $"{raw.proxySource ?? "IonDriver"}:{natNegIdPad[7]:x2}{natNegIdPad[6]:x2}{natNegIdPad[5]:x2}{natNegIdPad[4]:x2}{natNegIdPad[3]:x2}{natNegIdPad[2]:x2}{natNegIdPad[1]:x2}{natNegIdPad[0]:x2}");
                Datum session = new Datum("session", $"{(multiGame ? $"{GameID}:" : string.Empty)}{raw.proxySource ?? "IonDriver"}:{raw.g}");

                if (multiGame)
                    session["type"] = GAMELIST_TERMS.TYPE_LISTEN;

                session.AddObjectPath("address:other:nat", raw.g);

                session["name"] = raw.Name;
                if (!string.IsNullOrWhiteSpace(raw.MOTD))
                    session["message"] = raw.MOTD;

                List<DataCache> PlayerTypes = new List<DataCache>();
                PlayerTypes.Add(new DataCache()
                {
                    { "types", new List<string>() { GAMELIST_TERMS.PLAYERTYPE_PLAYER } },
                    { "max", raw.MaxPlayers },
                });
                session["player_types"] = PlayerTypes;

                session.AddObjectPath($"player_count:{GAMELIST_TERMS.PLAYERTYPE_PLAYER}", raw.CurPlayers);

                string modID = (raw.Mods?.FirstOrDefault() ?? @"0");
                string mapID = raw.MapFile?.ToLowerInvariant();

                // map stub
                if (mapStubAlreadySent.Add($"{modID}:{mapID}"))
                {
                    Datum mapData = new Datum("map", $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}");
                    if (!string.IsNullOrWhiteSpace(raw.MapFile))
                        mapData["map_file"] = raw.MapFile + @".bzn";
                    yield return mapData;
                    DontSendStub.Add($"map\t{modID}:{mapID}"); // we already sent the a stub don't send another
                }

                //game.AddObjectPath("level:id", $"{modID}:{mapID}");
                session.AddObjectPath("level:map", new DatumRef("map", $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}"));

                if (!string.IsNullOrWhiteSpace(raw.MapFile))
                    if (!MapDataFetchTasks.ContainsKey((modID, mapID)))
                        DelayedDatumTasks.Add(BuildDatumsForMapDataAsync(modID, mapID, multiGame, modsAlreadyReturnedLock, modsAlreadyReturnedFull));

                session.AddObjectPath($"status:{GAMELIST_TERMS.STATUS_LOCKED}", raw.Locked);
                session.AddObjectPath($"status:{GAMELIST_TERMS.STATUS_PASSWORD}", raw.Passworded);

                string ServerState = null;
                if (raw.ServerInfoMode.HasValue)
                {
                    switch (raw.ServerInfoMode)
                    {
                        case 0: // ServerInfoMode_Unknown
                            ServerState = Enum.GetName(typeof(ESessionState), ESessionState.Unknown);
                            break;
                        case 1: // ServerInfoMode_OpenWaiting
                        case 2: // ServerInfoMode_ClosedWaiting (full)
                            if (raw.pl.Any(dr => dr != null && ((dr.Score ?? 0) != 0 || (dr.Deaths ?? 0) != 0 || (dr.Kills ?? 0) != 0)))
                                // PreGame status applied in error, players have in-game sourced data
                                ServerState = Enum.GetName(typeof(ESessionState), ESessionState.InGame);
                            else
                                ServerState = Enum.GetName(typeof(ESessionState), ESessionState.PreGame);
                            break;
                        case 3: // ServerInfoMode_OpenPlaying
                        case 4: // ServerInfoMode_ClosedPlaying (full)
                            ServerState = Enum.GetName(typeof(ESessionState), ESessionState.InGame);
                            break;
                        case 5: // ServerInfoMode_Exiting
                            ServerState = Enum.GetName(typeof(ESessionState), ESessionState.PostGame);
                            break;
                    }
                }
                if (!string.IsNullOrWhiteSpace(ServerState))
                {
                    session.AddObjectPath("status:state", ServerState); // TODO limit this state to our state enumeration
                    session.AddObjectPath("status:other:state", ServerState);
                }

                // send mod stubs only once for each mod
                int ModsLen = (raw.Mods?.Length ?? 0);
                if (ModsLen > 0)
                    foreach (string mod in raw.Mods)
                        if (modStubAlreadySent.Add(mod))
                        {
                            yield return new Datum("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}{mod}");
                            DontSendStub.Add($"mod\t{mod}"); // we already sent the a stub don't send another
                        }

                if (ModsLen > 0 && raw.Mods[0] != "0")
                    //game.AddObjectPath("game:mod", raw.Mods[0]);
                    session.AddObjectPath("game:mod", new DatumRef("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}{raw.Mods[0]}"));
                if (ModsLen > 1)
                    //game.AddObjectPath("game:mods", raw.Mods.Skip(1));
                    session.AddObjectPath("game:mods", raw.Mods.Skip(1).Select(m => new DatumRef("mod", $"{(multiGame ? $"{GameID}:" : string.Empty)}{m}")));

                if (!string.IsNullOrWhiteSpace(raw.v))
                    session.AddObjectPath("game:version", raw.v);

                if (raw.TPS.HasValue && raw.TPS > 0)
                    session.AddObjectPath("other:tps", raw.TPS);

                if (raw.MaxPing.HasValue && raw.MaxPing > 0)
                    session.AddObjectPath("other:max_ping", raw.MaxPing);
                if (raw.MaxPingSeen.HasValue && raw.MaxPingSeen > 0)
                    session.AddObjectPath("other:worst_ping", raw.MaxPingSeen);


                if (raw.TimeLimit.HasValue && raw.TimeLimit > 0)
                    session.AddObjectPath("level:rules:time_limit", raw.TimeLimit);
                if (raw.KillLimit.HasValue && raw.KillLimit > 0)
                    session.AddObjectPath("level:rules:kill_limit", raw.KillLimit);

                if (!string.IsNullOrWhiteSpace(raw.t))
                    switch (raw.t)
                    {
                        case "0":
                            session.AddObjectPath("address:other:nat_type", $"NONE"); /// Works with anyone
                            break;
                        case "1":
                            session.AddObjectPath("address:other:nat_type", $"FULL CONE"); /// Accepts any datagrams to a port that has been previously used. Will accept the first datagram from the remote peer.
                            break;
                        case "2":
                            session.AddObjectPath("address:other:nat_type", $"ADDRESS RESTRICTED"); /// Accepts datagrams to a port as long as the datagram source IP address is a system we have already sent to. Will accept the first datagram if both systems send simultaneously. Otherwise, will accept the first datagram after we have sent one datagram.
                            break;
                        case "3":
                            session.AddObjectPath("address:other:nat_type", $"PORT RESTRICTED"); /// Same as address-restricted cone NAT, but we had to send to both the correct remote IP address and correct remote port. The same source address and port to a different destination uses the same mapping.
                            break;
                        case "4":
                            session.AddObjectPath("address:other:nat_type", $"SYMMETRIC"); /// A different port is chosen for every remote destination. The same source address and port to a different destination uses a different mapping. Since the port will be different, the first external punchthrough attempt will fail. For this to work it requires port-prediction (MAX_PREDICTIVE_PORT_RANGE>1) and that the router chooses ports sequentially.
                            break;
                        case "5":
                            session.AddObjectPath("address:other:nat_type", $"UNKNOWN"); /// Hasn't been determined. NATTypeDetectionClient does not use this, but other plugins might
                            break;
                        case "6":
                            session.AddObjectPath("address:other:nat_type", $"DETECTION IN PROGRESS"); /// In progress. NATTypeDetectionClient does not use this, but other plugins might
                            break;
                        case "7":
                            session.AddObjectPath("address:other:nat_type", $"SUPPORTS UPNP"); /// Didn't bother figuring it out, as we support UPNP, so it is equivalent to NAT_TYPE_NONE. NATTypeDetectionClient does not use this, but other plugins might
                            break;
                        default:
                            session.AddObjectPath("address:other:nat_type", $"[" + raw.t + "]");
                            break;
                    }

                //session.AddObjectPath($"sources:{(raw.proxySource ?? "IonDriver")}", true);
                session.AddObjectPath($"sources:{(raw.proxySource ?? "IonDriver")}", new DatumRef("source", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(raw.proxySource ?? "IonDriver")}"));

                bool m_TeamsOn = false;
                bool m_OnlyOneTeam = false;
                switch (raw.GameType)
                {
                    case 0:
                        // removed this as it's invalid, will probably need to use maps to override it via manual metadata
                        //session.AddObjectPath($"level:game_type", "All"); // TODO we saw this on a retaliation MPI, WTF?
                        break;
                    case 1:
                        {
                            int GetGameModeOutput = raw.GameSubType.Value % (int)GameMode.GAMEMODE_MAX; // extract if we are team or not
                            int detailed = raw.GameSubType.Value / (int)GameMode.GAMEMODE_MAX; // ivar7
                            bool RespawnSameRace = (detailed & 256) == 256;
                            bool RespawnAnyRace = (detailed & 512) == 512;
                            session.AddObjectPath("level:rules:respawn", RespawnSameRace ? "Race" : RespawnAnyRace ? "Any" : "One");
                            detailed = (detailed & 0xff);
                            switch ((GameMode)GetGameModeOutput)
                            {
                                case GameMode.GAMEMODE_TEAM_DM:
                                case GameMode.GAMEMODE_TEAM_KOTH:
                                case GameMode.GAMEMODE_TEAM_CTF:
                                case GameMode.GAMEMODE_TEAM_LOOT:
                                case GameMode.GAMEMODE_TEAM_RACE:
                                    m_TeamsOn = true;
                                    break;
                                case GameMode.GAMEMODE_DM:
                                case GameMode.GAMEMODE_KOTH:
                                case GameMode.GAMEMODE_CTF:
                                case GameMode.GAMEMODE_LOOT:
                                case GameMode.GAMEMODE_RACE:
                                default:
                                    m_TeamsOn = false;
                                    break;
                            }

                            session.AddObjectPath($"level:game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM"));
                            if (gametypeFullAlreadySent.Add($"DM"))
                            {
                                yield return new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM", new DataCache() { { "name", "Deathmatch" } });
                            }
                            else if (gametypeStubAlreadySent.Add($"DM"))
                            {
                                yield return new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}DM");
                            }

                            switch (detailed) // first byte of ivar7?  might be all of ivar7 // Deathmatch subtype (0 = normal; 1 = KOH; 2 = CTF; add 256 for random respawn on same race, or add 512 for random respawn w/o regard to race)
                            {
                                case 0: // Deathmatch
                                    session.AddObjectPath($"level:game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}DM"));
                                    if (gamemodeFullAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}DM"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}DM", new DataCache() { { "name", $"{(m_TeamsOn ? "Team " : string.Empty)}Deathmatch" } });
                                    }
                                    else if (gamemodeStubAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}DM"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}DM");
                                    }
                                    break;
                                case 1: // King of the Hill
                                    session.AddObjectPath($"level:game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}KOTH"));
                                    if (gamemodeFullAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}KOTH"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}KOTH", new DataCache() { { "name", $"{(m_TeamsOn ? "Team " : string.Empty)}King of the Hill" } });
                                    }
                                    else if (gamemodeStubAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}KOTH"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}KOTH");
                                    }
                                    break;
                                case 2: // Capture the Flag
                                    session.AddObjectPath($"level:game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}CTF"));
                                    if (gamemodeFullAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}CTF"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}CTF", new DataCache() { { "name", $"{(m_TeamsOn ? "Team " : string.Empty)}Capture the Flag" } });
                                    }
                                    else if (gamemodeStubAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}CTF"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}CTF");
                                    }
                                    break;
                                case 3: // Loot
                                    session.AddObjectPath($"level:game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}LOOT"));
                                    if (gamemodeFullAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}LOOT"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}LOOT", new DataCache() { { "name", $"{(m_TeamsOn ? "Team " : string.Empty)}Loot" } });
                                    }
                                    else if (gamemodeStubAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}LOOT"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}LOOT");
                                    }
                                    break;
                                case 4: // DM [RESERVED]
                                    break;
                                case 5: // Race
                                    session.AddObjectPath($"level:game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}RACE"));
                                    if      (gamemodeFullAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}RACE")) { yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}RACE", new DataCache() { { "name", $"{(m_TeamsOn ? "Team " : string.Empty)}Race" } }); }
                                    else if (gamemodeStubAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}RACE")) { yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}RACE"); }
                                    break;
                                case 6: // Race (Vehicle Only)
                                    session.AddObjectPath($"level:game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}RACE"));
                                    if (gamemodeFullAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}RACE"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}RACE", new DataCache() { { "name", $"{(m_TeamsOn ? "Team " : string.Empty)}Race" } });
                                    }
                                    else if (gamemodeStubAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}RACE"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}RACE");
                                    }
                                    session.AddObjectPath($"level:rules:vehicle_only", true);
                                    break;
                                case 7: // DM (Vehicle Only)
                                    session.AddObjectPath($"level:game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}DM"));
                                    if (gamemodeFullAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}DM"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}DM", new DataCache() { { "name", $"{(m_TeamsOn ? "Team " : string.Empty)}Deathmatch" } });
                                    }
                                    else if (gamemodeStubAlreadySent.Add($"{(m_TeamsOn ? "TEAM_" : string.Empty)}DM"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}{(m_TeamsOn ? "TEAM_" : string.Empty)}DM");
                                    }
                                    session.AddObjectPath($"level:rules:vehicle_only", true);
                                    break;
                                default:
                                    //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : string.Empty) + "DM [UNKNOWN {raw.GameSubType}]";
                                    break;
                            }
                        }
                        break;
                    case 2:
                        {
                            int GetGameModeOutput = raw.GameSubType.Value % (int)GameMode.GAMEMODE_MAX; // extract if we are team or not

                            session.AddObjectPath($"level:game_type", new DatumRef("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                            if (gametypeFullAlreadySent.Add($"STRAT"))
                            {
                                yield return new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT", new DataCache() { { "name", "Strategy" } });
                            }
                            else if (gametypeStubAlreadySent.Add($"STRAT"))
                            {
                                yield return new Datum("game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT");
                            }

                            switch ((GameMode)GetGameModeOutput)
                            {
                                case GameMode.GAMEMODE_TEAM_STRAT:
                                    session.AddObjectPath($"level:game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT"));
                                    m_TeamsOn = true;
                                    m_OnlyOneTeam = false;
                                    if (gamemodeFullAlreadySent.Add("STRAT"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT", new DataCache() { { "name", "Team Strategy" } });
                                    }
                                    else if (gamemodeStubAlreadySent.Add("STRAT"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}STRAT");
                                    }
                                    break;
                                case GameMode.GAMEMODE_STRAT:
                                    session.AddObjectPath($"level:game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}FFA"));
                                    m_TeamsOn = false;
                                    m_OnlyOneTeam = false;
                                    if (gamemodeFullAlreadySent.Add("FFA"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}FFA", new DataCache() { { "name", "Free for All" } });
                                    }
                                    else if (gamemodeStubAlreadySent.Add("FFA"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}FFA");
                                    }
                                    break;
                                case GameMode.GAMEMODE_MPI:
                                    session.AddObjectPath($"level:game_mode", new DatumRef("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}MPI"));
                                    m_TeamsOn = true;
                                    m_OnlyOneTeam = true;
                                    if (gamemodeFullAlreadySent.Add("MPI"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}MPI", new DataCache() { { "name", "Multiplayer Instant Action" } });
                                    }
                                    else if (gamemodeStubAlreadySent.Add("MPI"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}MPI");
                                    }
                                    break;
                                default:
                                    //game.Level["GameType"] = $"STRAT [UNKNOWN {GetGameModeOutput}]";
                                    if (gamemodeFullAlreadySent.Add("STRAT"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}UNK{GetGameModeOutput}", new DataCache() { { "name", $"{GetGameModeOutput}" } });
                                    }
                                    else if (gamemodeStubAlreadySent.Add("STRAT"))
                                    {
                                        yield return new Datum("game_mode", $"{(multiGame ? $"{GameID}:" : string.Empty)}UNK{GetGameModeOutput}");
                                    }
                                    break;
                            }
                        }
                        break;
                    case 3: // impossible, BZCC limits to 0-2
                        session.AddObjectPath($"level:game_type", $"{(multiGame ? $"{GameID}:" : string.Empty)}MPI"); //  "MPI [Invalid]";
                        break;
                }

                if (!string.IsNullOrWhiteSpace(raw.d))
                {
                    session.AddObjectPath($"game:other:mod_hash", raw.d); // base64 encoded CRC32
                    if (admin)
                    {
                        //game.Game.Add("ModHash_bin", BitConverter.ToString(Base64DecodeBinary(raw.d)));
                    }
                }

                if (raw.pl != null)
                {
                    List<DataCache> Players = new List<DataCache>();
                    //foreach (var dr in raw.pl)
                    for (int pl_i = 0; pl_i < raw.pl.Length; pl_i++)
                    {
                        var dr = raw.pl[pl_i];
                        DataCache player = new DataCache();

                        player["name"] = dr.Name;
                        player["type"] = GAMELIST_TERMS.PLAYERTYPE_PLAYER;
                        if (pl_i == 0)
                            player["is_host"] = true; // assume the first player is the owner of the game

                        if ((dr.Team ?? 255) != 255) // 255 means not on a team yet? could be understood as -1
                        {
                            if (m_TeamsOn)
                            {
                                if (!m_OnlyOneTeam)
                                {
                                    if (dr.Team >= 1 && dr.Team <=  5) player.AddObjectPath("team:id", "1");
                                    if (dr.Team >= 6 && dr.Team <= 10) player.AddObjectPath("team:id", "2");
                                    if (dr.Team == 1 || dr.Team ==  6) player.AddObjectPath("team:leader", true);
                                    if (dr.Team >= 1 && dr.Team <= 10) player.AddObjectPath("team:index", (dr.Team - 1) % 5);
                                }
                                else // MPI, only teams 1-5 should be valid but let's assume all are valid
                                {
                                    // TODO confirm if map data might need to influence this
                                    player.AddObjectPath("team:id", "1");
                                    if (dr.Team == 1) player.AddObjectPath("team:leader", true);
                                    if (dr.Team >= 1) player.AddObjectPath("team:index", dr.Team - 1);
                                }
                            }
                            //player.AddObjectPath("team:sub_team:id", dr.Team.Value.ToString());
                            player.AddObjectPath("ids:slot:id", dr.Team);
                            player.AddObjectPath("index", dr.Team);
                        }

                        if (dr.Kills.HasValue)
                            player.AddObjectPath("stats:kills", dr.Kills);
                        if (dr.Deaths.HasValue)
                            player.AddObjectPath("stats:deaths", dr.Deaths);
                        if (dr.Score.HasValue)
                            player.AddObjectPath("stats:score", dr.Score);

                        if (!string.IsNullOrWhiteSpace(dr.PlayerID))
                        {
                            player.AddObjectPath("ids:bzr_net:id", dr.PlayerID);
                            switch (dr.PlayerID[0])
                            {
                                case 'S':
                                    {
                                        ulong playerID = 0;
                                        if (ulong.TryParse(dr.PlayerID.Substring(1), out playerID))
                                        {
                                            yield return new Datum("identity/steam", playerID.ToString(), new DataCache()
                                            {
                                                { "type", "steam" },
                                            });
                                            DontSendStub.Add($"identity/steam\t{playerID.ToString()}"); // we already sent the a stub don't send another

                                            player.AddObjectPath("ids:steam", new DataCache() {
                                                { "id", playerID.ToString() },
                                                { "raw", dr.PlayerID.Substring(1) },
                                                { "identity", new DatumRef("identity/steam", playerID.ToString()) },
                                            });

                                            DelayedDatumTasks.Add(steamInterface.GetPendingDataAsync(playerID));
                                        }
                                    }
                                    break;
                                case 'G':
                                    {
                                        ulong playerID = 0;
                                        if (ulong.TryParse(dr.PlayerID.Substring(1), out playerID))
                                        {
                                            playerID = GogInterface.CleanGalaxyUserId(playerID);

                                            yield return new Datum("identity/gog", playerID.ToString(), new DataCache()
                                            {
                                                { "type", "gog" },
                                            });
                                            DontSendStub.Add($"identity/gog\t{playerID.ToString()}"); // we already sent the a stub don't send another

                                            player.AddObjectPath("ids:gog", new DataCache() {
                                                { "id", playerID.ToString() },
                                                { "raw", dr.PlayerID.Substring(1) },
                                                { "identity", new DatumRef("identity/gog", playerID.ToString()) },
                                            });

                                            DelayedDatumTasks.Add(gogInterface.GetPendingDataAsync(playerID));
                                        }
                                    }
                                    break;
                            }
                        }

                        Players.Add(player);
                    }
                    session["players"] = Players;
                }

                if (raw.GameTimeMinutes.HasValue)
                {
                    session.AddObjectPath("time:seconds", raw.GameTimeMinutes * 60);
                    session.AddObjectPath("time:resolution", 60);
                    session.AddObjectPath("time:max", raw.GameTimeMinutes.Value == 255); // 255 appears to mean it maxed out?  Does for currently playing.
                    if (!string.IsNullOrWhiteSpace(ServerState))
                        session.AddObjectPath("time:context", ServerState);
                }

                if (m_TeamsOn)
                {
                    session.AddObjectPath("teams:1:human", true);
                    session.AddObjectPath("teams:1:computer", false);
                    //if ((mapData?.netVars?.Count ?? 0) > 0)
                    //{
                    //    if (mapData.netVars.ContainsKey("svar1")) game.Teams.AddObjectPath("1:Name", mapData.netVars["svar1"]);
                    //    if (mapData.netVars.ContainsKey("svar2")) game.Teams.AddObjectPath("2:Name", mapData.netVars["svar2"]);
                    //}
                    if (!m_OnlyOneTeam)
                    {
                        session.AddObjectPath("teams:2:human", true);
                        session.AddObjectPath("teams:2:computer", false);
                        if (raw.MaxPlayers.HasValue)
                        {
                            session.AddObjectPath("teams:1:max", Math.Min(5, raw.MaxPlayers.Value - 1));
                            session.AddObjectPath("teams:2:max", Math.Min(5, raw.MaxPlayers.Value - 1));
                        }
                    }
                    else
                    {
                        session.AddObjectPath("teams:2:human", false);
                        session.AddObjectPath("teams:2:computer", true);
                        if (raw.MaxPlayers.HasValue)
                            session.AddObjectPath("teams:1:max", Math.Min(5, raw.MaxPlayers.Value));
                    }
                }

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

            //foreach (var item in MapDataFetchTasks)
            //{
            //    MapData mapData = await item.Value;
            //    if (mapData != null)
            //    {
            //        Datum mapDatum = new Datum("map", $"{item.Key.mod}:{item.Key.map}", new DataCache2() {
            //            { "name", mapData?.title },
            //            { "description", mapData?.description },
            //        });
            //        if (mapData.image != null)
            //            mapDatum["image"] = $"{mapUrl.TrimEnd('/')}/{mapData.image}";
            //        yield return mapDatum;
            //        //game.AddObjectPath($"Level:Attributes:Vehicles", new JArray(mapData.map.vehicles.Select(dr => $"{modID}:{dr}").ToArray()));
            //
            //        if (mapData?.mods != null)
            //        {
            //            foreach (var mod in mapData.mods)
            //            {
            //                if (!ModsAlreadyReturned.Contains(mod.Key))
            //                {
            //                    Datum modData = new Datum("mod", mod.Key, new DataCache2() {
            //                        { "name", mod.Value?.name ?? mod.Value?.workshop_name },
            //                    });
            //
            //                    if (mod.Value?.image != null)
            //                        modData.Data["image"] = $"{mapUrl.TrimEnd('/')}/{mod.Value.image}";
            //
            //                    if (UInt64.TryParse(mod.Key, out UInt64 modId) && modId > 0)
            //                        modData.Data["url"] = $"http://steamcommunity.com/sharedfiles/filedetails/?id={mod.Key}";
            //
            //                    yield return modData;
            //
            //                    ModsAlreadyReturned.Add(mod.Key);
            //                }
            //            }
            //        }
            //    }
            //}

            yield break;
        }

        private async Task<List<PendingDatum>> BuildDatumsForMapDataAsync(string modID, string mapID, bool multiGame, SemaphoreSlim modsAlreadyReturnedLock, HashSet<string> modsAlreadyReturnedFull)
        {
            List<PendingDatum> retVal = new List<PendingDatum>();
            CachedData<MapData> mapDataC = await cachedAdvancedWebClient.GetObject<MapData>($"{mapUrl.TrimEnd('/')}/getdata.php?map={mapID}&mod={modID}");
            MapData mapData = mapDataC.Data;
            if (mapData != null)
            {
                Datum mapDatum = new Datum("map", $"{(multiGame ? $"{GameID}:" : string.Empty)}{modID}:{mapID}", new DataCache() {
                    { "name", mapData?.title },
                    { "description", mapData?.description },
                    { "map_file", mapID + @".bzn" },
                });
                if (mapData.image != null)
                    mapDatum["image"] = $"{mapUrl.TrimEnd('/')}/{mapData.image}";
                if ((mapData?.netVars?.Count ?? 0) > 0)
                {
                    if (mapData.netVars.ContainsKey("svar1")) mapDatum.AddObjectPath("teams:1:name", mapData.netVars["svar1"]);
                    if (mapData.netVars.ContainsKey("svar2")) mapDatum.AddObjectPath("teams:2:name", mapData.netVars["svar2"]);
                }
                retVal.Add(new PendingDatum(mapDatum, null, false));

                if (mapData?.mods != null)
                {
                    foreach (var mod in mapData.mods)
                    {
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

                                retVal.Add(new PendingDatum(modData, null, false));

                                modsAlreadyReturnedFull.Add(mod.Key);
                            }
                        }
                        finally
                        {
                            modsAlreadyReturnedLock.Release();
                        }
                    }
                }
            }
            return retVal;
        }
    }
}
