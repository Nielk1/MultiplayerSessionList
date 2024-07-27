﻿using Microsoft.Extensions.Configuration;
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
using System.Threading;
using System.Threading.Tasks;
using static MultiplayerSessionList.Services.GogInterface;

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
        private CachedJsonWebClient mapDataInterface;

        public GameListModule(IConfiguration configuration, GogInterface gogInterface, SteamInterface steamInterface, CachedJsonWebClient mapDataInterface)
        {
            queryUrl = configuration["bigboat:battlezone_combat_commander:sessions"];
            mapUrl = configuration["bigboat:battlezone_combat_commander:maps"];
            this.gogInterface = gogInterface;
            this.steamInterface = steamInterface;
            this.mapDataInterface = mapDataInterface;
        }

        public async Task<GameListData> GetGameList(bool admin)
        {
            using (var http = new HttpClient())
            {
                var res = await http.GetStringAsync(queryUrl).ConfigureAwait(false);
                var gamelist = JsonConvert.DeserializeObject<BZCCRaknetData>(res);

                SessionItem DefaultSession = new SessionItem();
                DefaultSession.Type = GAMELIST_TERMS.TYPE_LISTEN;

                DataCache Metadata = new DataCache();

                foreach(var proxyStatus in gamelist.proxyStatus)
                {
                    Metadata.AddObjectPath($"{GAMELIST_TERMS.ATTRIBUTE_LISTSERVER}:{proxyStatus.Key}:Status", proxyStatus.Value.status);
                    Metadata.AddObjectPath($"{GAMELIST_TERMS.ATTRIBUTE_LISTSERVER}:{proxyStatus.Key}:Success", proxyStatus.Value.success);
                    Metadata.AddObjectPath($"{GAMELIST_TERMS.ATTRIBUTE_LISTSERVER}:{proxyStatus.Key}:Timestamp", proxyStatus.Value.updated);
                }

                DataCache DataCache = new DataCache();
                DataCache Mods = new DataCache();

                List<SessionItem> Sessions = new List<SessionItem>();

                List<Task> Tasks = new List<Task>();
                SemaphoreSlim DataCacheLock = new SemaphoreSlim(1);
                SemaphoreSlim ModsLock = new SemaphoreSlim(1);
                SemaphoreSlim SessionsLock = new SemaphoreSlim(1);

                /*
                Tasks.Add(Task.Run(async () =>
                {
                    await DataCacheLock.WaitAsync();
                    try
                    {
                        DataCache.AddObjectPath($"Level:GameType:DM:Name", "Deathmatch");
                        DataCache.AddObjectPath($"Level:GameType:STRAT:Name", "Strategy");

                        DataCache.AddObjectPath($"Level:GameMode:DM:Name", "Deathmatch");
                        DataCache.AddObjectPath($"Level:GameMode:KOTH:Name", "King of the Hill");
                        DataCache.AddObjectPath($"Level:GameMode:CTF:Name", "Capture the Flag");
                        DataCache.AddObjectPath($"Level:GameMode:LOOT:Name", "Loot");
                        DataCache.AddObjectPath($"Level:GameMode:RACE:Name", "Race");

                        DataCache.AddObjectPath($"Level:GameMode:TEAM_DM:Name", "Team Deathmatch");
                        DataCache.AddObjectPath($"Level:GameMode:TEAM_KOTH:Name", "Team King of the Hill");
                        DataCache.AddObjectPath($"Level:GameMode:TEAM_CTF:Name", "Team Capture the Flag");
                        DataCache.AddObjectPath($"Level:GameMode:TEAM_LOOT:Name", "Team Loot");
                        DataCache.AddObjectPath($"Level:GameMode:TEAM_RACE:Name", "Team Race");

                        DataCache.AddObjectPath($"Level:GameMode:STRAT:Name", "Strategy");
                        DataCache.AddObjectPath($"Level:GameMode:FFA:Name", "Free for All");
                        DataCache.AddObjectPath($"Level:GameMode:MPI:Name", "MPI");
                    }
                    finally
                    {
                        DataCacheLock.Release();
                    }
                }));
                */

                foreach (var raw in gamelist.GET)
                {
                    Tasks.Add(Task.Run(async () =>
                    {
                        SessionItem game = new SessionItem();

                        if (raw.g == "XXXXXXX@XX")
                            return;

                        game.ID = $"{raw.proxySource ?? "IonDriver"}:{raw.g}";

                        game.Address["NAT"] = raw.g;
                        //if (!raw.Passworded)
                        //{
                        //    game.Address["Rich"] = string.Join(null, $"N,{raw.Name.Length},{raw.Name},{raw.mm.Length},{raw.mm},{raw.g},0,".Select(dr => $"{((int)dr):x2}"));
                        //}

                        game.Name = raw.Name;
                        if (!string.IsNullOrWhiteSpace(raw.MOTD))
                            game.Message = raw.MOTD;

                        game.PlayerTypes.Add(new PlayerTypeData()
                        {
                            Types = new List<string>() { GAMELIST_TERMS.PLAYERTYPE_PLAYER },
                            Max = raw.MaxPlayers
                        });

                        game.PlayerCount.Add(GAMELIST_TERMS.PLAYERTYPE_PLAYER, raw.CurPlayers);

                        if (!string.IsNullOrWhiteSpace(raw.MapFile))
                            game.Level["MapFile"] = raw.MapFile + @".bzn";
                        string modID = (raw.Mods?.FirstOrDefault() ?? @"0");
                        string mapID = raw.MapFile?.ToLowerInvariant();
                        game.Level["ID"] = $"{modID}:{mapID}";

                        Task<MapData> mapDataTask = null;
                        if (!string.IsNullOrWhiteSpace(raw.MapFile))
                            mapDataTask = mapDataInterface.GetObject<MapData>($"{mapUrl.TrimEnd('/')}/getdata.php?map={mapID}&mod={modID}");

                        game.Status.Add(GAMELIST_TERMS.STATUS_LOCKED, raw.Locked);
                        game.Status.Add(GAMELIST_TERMS.STATUS_PASSWORD, raw.Passworded);

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
                            game.Status.Add("State", ServerState);

                        int ModsLen = (raw.Mods?.Length ?? 0);
                        if (ModsLen > 0 && raw.Mods[0] != "0")
                            game.Game.Add("Mod", raw.Mods[0]);
                        if (ModsLen > 1)
                            game.Game.Add("Mods", JArray.FromObject(raw.Mods.Skip(1)));

                        if (!string.IsNullOrWhiteSpace(raw.v))
                            game.Game["Version"] = raw.v;

                        if (raw.TPS.HasValue && raw.TPS > 0)
                            game.Attributes.Add("TPS", raw.TPS);

                        if (raw.MaxPing.HasValue && raw.MaxPing > 0)
                            game.Attributes.Add("MaxPing", raw.MaxPing);


                        if (raw.TimeLimit.HasValue && raw.TimeLimit > 0)
                            game.Level.AddObjectPath("Attributes:TimeLimit", raw.TimeLimit);
                        if (raw.KillLimit.HasValue && raw.KillLimit > 0)
                            game.Level.AddObjectPath("Attributes:KillLimit", raw.KillLimit);

                        if (!string.IsNullOrWhiteSpace(raw.t))
                            switch (raw.t)
                            {
                                case "0":
                                    game.Address.Add("NAT_TYPE", $"NONE"); /// Works with anyone
                                    break;
                                case "1":
                                    game.Address.Add("NAT_TYPE", $"FULL CONE"); /// Accepts any datagrams to a port that has been previously used. Will accept the first datagram from the remote peer.
                                    break;
                                case "2":
                                    game.Address.Add("NAT_TYPE", $"ADDRESS RESTRICTED"); /// Accepts datagrams to a port as long as the datagram source IP address is a system we have already sent to. Will accept the first datagram if both systems send simultaneously. Otherwise, will accept the first datagram after we have sent one datagram.
                                    break;
                                case "3":
                                    game.Address.Add("NAT_TYPE", $"PORT RESTRICTED"); /// Same as address-restricted cone NAT, but we had to send to both the correct remote IP address and correct remote port. The same source address and port to a different destination uses the same mapping.
                                    break;
                                case "4":
                                    game.Address.Add("NAT_TYPE", $"SYMMETRIC"); /// A different port is chosen for every remote destination. The same source address and port to a different destination uses a different mapping. Since the port will be different, the first external punchthrough attempt will fail. For this to work it requires port-prediction (MAX_PREDICTIVE_PORT_RANGE>1) and that the router chooses ports sequentially.
                                    break;
                                case "5":
                                    game.Address.Add("NAT_TYPE", $"UNKNOWN"); /// Hasn't been determined. NATTypeDetectionClient does not use this, but other plugins might
                                    break;
                                case "6":
                                    game.Address.Add("NAT_TYPE", $"DETECTION IN PROGRESS"); /// In progress. NATTypeDetectionClient does not use this, but other plugins might
                                    break;
                                case "7":
                                    game.Address.Add("NAT_TYPE", $"SUPPORTS UPNP"); /// Didn't bother figuring it out, as we support UPNP, so it is equivalent to NAT_TYPE_NONE. NATTypeDetectionClient does not use this, but other plugins might
                                    break;
                                default:
                                    game.Address.Add("NAT_TYPE", $"[" + raw.t + "]");
                                    break;
                            }

                        game.Attributes.Add(GAMELIST_TERMS.ATTRIBUTE_LISTSERVER, raw.proxySource ?? "IonDriver");

                        bool m_TeamsOn = false;
                        bool m_OnlyOneTeam = false;
                        switch (raw.GameType)
                        {
                            case 0:
                                game.Level["GameType"] = $"All";
                                break;
                            case 1:
                                {
                                    int GetGameModeOutput = raw.GameSubType.Value % (int)GameMode.GAMEMODE_MAX; // extract if we are team or not
                                    int detailed = raw.GameSubType.Value / (int)GameMode.GAMEMODE_MAX; // ivar7
                                    bool RespawnSameRace = (detailed & 256) == 256;
                                    bool RespawnAnyRace = (detailed & 512) == 512;
                                    game.Level.AddObjectPath("Attributes:Respawn", RespawnSameRace ? "Race" : RespawnAnyRace ? "Any" : "One");
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

                                    switch (detailed) // first byte of ivar7?  might be all of ivar7 // Deathmatch subtype (0 = normal; 1 = KOH; 2 = CTF; add 256 for random respawn on same race, or add 512 for random respawn w/o regard to race)
                                    {
                                        case 0: // Deathmatch
                                            game.Level.AddObjectPath("GameType:ID", "DM");
                                            game.Level.AddObjectPath("GameMode:ID", (m_TeamsOn ? "TEAM_" : String.Empty) + "DM");
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:DM"))
                                                    DataCache.AddObjectPath($"Level:GameType:DM:Name", "Deathmatch");
                                                if (!DataCache.ContainsPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}DM"))
                                                    DataCache.AddObjectPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}DM:Name", "Deathmatch");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            break;
                                        case 1: // King of the Hill
                                            game.Level.AddObjectPath("GameType:ID", "DM");
                                            game.Level.AddObjectPath("GameMode:ID", (m_TeamsOn ? "TEAM_" : String.Empty) + "KOTH");
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:DM"))
                                                    DataCache.AddObjectPath($"Level:GameType:DM:Name", "Deathmatch");
                                                if (!DataCache.ContainsPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}KOTH"))
                                                    DataCache.AddObjectPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}KOTH:Name", "King of the Hill");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            break;
                                        case 2: // Capture the Flag
                                            game.Level.AddObjectPath("GameType:ID", "DM");
                                            game.Level.AddObjectPath("GameMode:ID", (m_TeamsOn ? "TEAM_" : String.Empty) + "CTF");
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:DM"))
                                                    DataCache.AddObjectPath($"Level:GameType:DM:Name", "Deathmatch");
                                                if (!DataCache.ContainsPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}CTF"))
                                                    DataCache.AddObjectPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}CTF:Name", "Capture the Flag");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            break;
                                        case 3: // Loot
                                            game.Level.AddObjectPath("GameType:ID", "DM");
                                            game.Level.AddObjectPath("GameMode:ID", (m_TeamsOn ? "TEAM_" : String.Empty) + "LOOT");
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:DM"))
                                                    DataCache.AddObjectPath($"Level:GameType:DM:Name", "Deathmatch");
                                                if (!DataCache.ContainsPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}LOOT"))
                                                    DataCache.AddObjectPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}LOOT:Name", "Loot");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            break;
                                        case 4: // DM [RESERVED]
                                            game.Level.AddObjectPath("GameType:ID", "DM");
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:DM"))
                                                    DataCache.AddObjectPath($"Level:GameType:DM:Name", "Deathmatch");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            break;
                                        case 5: // Race
                                            game.Level.AddObjectPath("GameType:ID", "DM");
                                            game.Level.AddObjectPath("GameMode:ID", (m_TeamsOn ? "TEAM_" : String.Empty) + "RACE");
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:DM"))
                                                    DataCache.AddObjectPath($"Level:GameType:DM:Name", "Deathmatch");
                                                if (!DataCache.ContainsPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}RACE"))
                                                    DataCache.AddObjectPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}RACE:Name", "Race");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            break;
                                        case 6: // Race (Vehicle Only)
                                            game.Level.AddObjectPath("GameType:ID", "DM");
                                            game.Level.AddObjectPath("GameMode:ID", (m_TeamsOn ? "TEAM_" : String.Empty) + "RACE");
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:DM"))
                                                    DataCache.AddObjectPath($"Level:GameType:DM:Name", "Deathmatch");
                                                if (!DataCache.ContainsPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}RACE"))
                                                    DataCache.AddObjectPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}RACE:Name", "Race");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            game.Level.AddObjectPath("Attributes:VehicleOnly", true);
                                            break;
                                        case 7: // DM (Vehicle Only)
                                            game.Level.AddObjectPath("GameType:ID", "DM");
                                            game.Level.AddObjectPath("GameMode:ID", (m_TeamsOn ? "TEAM_" : String.Empty) + "DM");
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:DM"))
                                                    DataCache.AddObjectPath($"Level:GameType:DM:Name", "Deathmatch");
                                                if (!DataCache.ContainsPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}DM"))
                                                    DataCache.AddObjectPath($"Level:GameMode:{(m_TeamsOn ? "TEAM_" : String.Empty)}DM:Name", "Deathmatch");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            game.Level.AddObjectPath("Attributes:VehicleOnly", true);
                                            break;
                                        default:
                                            game.Level.AddObjectPath("GameType:ID", "DM");
                                            //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + "DM [UNKNOWN {raw.GameSubType}]";
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:DM"))
                                                    DataCache.AddObjectPath($"Level:GameType:DM:Name", "Deathmatch");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            break;
                                    }
                                }
                                break;
                            case 2:
                                {
                                    int GetGameModeOutput = raw.GameSubType.Value % (int)GameMode.GAMEMODE_MAX; // extract if we are team or not
                                    switch ((GameMode)GetGameModeOutput)
                                    {
                                        case GameMode.GAMEMODE_TEAM_STRAT:
                                            game.Level.AddObjectPath("GameType:ID", "STRAT");
                                            game.Level.AddObjectPath("GameMode:ID", "STRAT");
                                            m_TeamsOn = true;
                                            m_OnlyOneTeam = false;
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:STRAT"))
                                                    DataCache.AddObjectPath($"Level:GameType:STRAT:Name", "Strategy");
                                                if (!DataCache.ContainsPath($"Level:GameMode:STRAT"))
                                                    DataCache.AddObjectPath($"Level:GameMode:STRAT:Name", "Strategy");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            break;
                                        case GameMode.GAMEMODE_STRAT:
                                            game.Level.AddObjectPath("GameType:ID", "STRAT");
                                            game.Level.AddObjectPath("GameMode:ID", "FFA");
                                            m_TeamsOn = false;
                                            m_OnlyOneTeam = false;
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:STRAT"))
                                                    DataCache.AddObjectPath($"Level:GameType:STRAT:Name", "Strategy");
                                                if (!DataCache.ContainsPath($"Level:GameMode:FFA"))
                                                    DataCache.AddObjectPath($"Level:GameMode:FFA:Name", "Free for All");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            break;
                                        case GameMode.GAMEMODE_MPI:
                                            game.Level.AddObjectPath("GameType:ID", "STRAT");
                                            game.Level.AddObjectPath("GameMode:ID", "MPI");
                                            m_TeamsOn = true;
                                            m_OnlyOneTeam = true;
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:STRAT"))
                                                    DataCache.AddObjectPath($"Level:GameType:STRAT:Name", "Strategy");
                                                if (!DataCache.ContainsPath($"Level:GameMode:MPI"))
                                                    DataCache.AddObjectPath($"Level:GameMode:MPI:Name", "MPI");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            break;
                                        default:
                                            //game.Level["GameType"] = $"STRAT [UNKNOWN {GetGameModeOutput}]";
                                            game.Level.AddObjectPath("GameType:ID", "STRAT");
                                            //game.Level["GameMode"] = null;
                                            await DataCacheLock.WaitAsync();
                                            try
                                            {
                                                if (!DataCache.ContainsPath($"Level:GameType:STRAT"))
                                                    DataCache.AddObjectPath($"Level:GameType:STRAT:Name", "Strategy");
                                            }
                                            finally
                                            {
                                                DataCacheLock.Release();
                                            }
                                            break;
                                    }
                                }
                                break;
                            case 3: // impossible, BZCC limits to 0-2
                                game.Level.AddObjectPath("GameType:ID", "MPI"); //  "MPI [Invalid]";
                                await DataCacheLock.WaitAsync();
                                try
                                {
                                    if (!DataCache.ContainsPath($"Level:GameType:MPI"))
                                        DataCache.AddObjectPath($"Level:GameType:MPI:Name", "MPI");
                                }
                                finally
                                {
                                    DataCacheLock.Release();
                                }
                                break;
                        }

                        if (!string.IsNullOrWhiteSpace(raw.d))
                        {
                            game.Game.Add("ModHash", raw.d); // base64 encoded CRC32
                        }

                        if (raw.pl != null)
                            foreach (var dr in raw.pl)
                            {
                                PlayerItem player = new PlayerItem();

                                player.Name = dr.Name;
                                player.Type = GAMELIST_TERMS.PLAYERTYPE_PLAYER;

                                if ((dr.Team ?? 255) != 255) // 255 means not on a team yet? could be understood as -1
                                {
                                    player.Team = new PlayerTeam();
                                    if (m_TeamsOn)
                                    {
                                        if (!m_OnlyOneTeam)
                                        {
                                            if (dr.Team >= 1 && dr.Team <= 5)
                                                player.Team.ID = "1";
                                            if (dr.Team >= 6 && dr.Team <= 10)
                                                player.Team.ID = "2";
                                            if (dr.Team == 1 || dr.Team == 6)
                                                player.Team.Leader = true;
                                        }
                                        else // MPI, only teams 1-5 should be valid but let's assume all are valid
                                        {
                                            // TODO confirm if map data might need to influence this
                                            player.Team.ID = "1";
                                            if (dr.Team == 1)
                                                player.Team.Leader = true;
                                        }
                                    }
                                    player.Team.SubTeam = new PlayerTeam() { ID = dr.Team.Value.ToString() };
                                    player.GetIDData("Slot").Add("ID", dr.Team);
                                }

                                if (dr.Kills.HasValue)
                                    player.Stats.Add("Kills", dr.Kills);
                                if (dr.Deaths.HasValue)
                                    player.Stats.Add("Deaths", dr.Deaths);
                                if (dr.Score.HasValue)
                                    player.Stats.Add("Score", dr.Score);

                                if (!string.IsNullOrWhiteSpace(dr.PlayerID))
                                {
                                    player.GetIDData("BZRNet").Add("ID", dr.PlayerID);
                                    switch (dr.PlayerID[0])
                                    {
                                        case 'S':
                                            {
                                                player.GetIDData("Steam").Add("Raw", dr.PlayerID.Substring(1));
                                                try
                                                {
                                                    ulong playerID = 0;
                                                    if (ulong.TryParse(dr.PlayerID.Substring(1), out playerID))
                                                    {
                                                        player.GetIDData("Steam").Add("ID", playerID.ToString());

                                                        await DataCacheLock.WaitAsync();
                                                        try
                                                        {
                                                            if (!DataCache.ContainsPath($"Players:IDs:Steam:{playerID.ToString()}"))
                                                            {
                                                                PlayerSummaryModel playerData = await steamInterface.Users(playerID);
                                                                DataCache.AddObjectPath($"Players:IDs:Steam:{playerID.ToString()}:AvatarUrl", playerData.AvatarFullUrl);
                                                                DataCache.AddObjectPath($"Players:IDs:Steam:{playerID.ToString()}:Nickname", playerData.Nickname);
                                                                DataCache.AddObjectPath($"Players:IDs:Steam:{playerID.ToString()}:ProfileUrl", playerData.ProfileUrl);
                                                            }
                                                        }
                                                        finally
                                                        {
                                                            DataCacheLock.Release();
                                                        }
                                                    }
                                                }
                                                catch { }
                                            }
                                            break;
                                        case 'G':
                                            {
                                                player.GetIDData("Gog").Add("Raw", dr.PlayerID.Substring(1));
                                                try
                                                {
                                                    ulong playerID = 0;
                                                    if (ulong.TryParse(dr.PlayerID.Substring(1), out playerID))
                                                    {
                                                        playerID = GogInterface.CleanGalaxyUserId(playerID);
                                                        player.GetIDData("Gog").Add("ID", playerID.ToString());

                                                        await DataCacheLock.WaitAsync();
                                                        try
                                                        {
                                                            if (!DataCache.ContainsPath($"Players:IDs:Gog:{playerID.ToString()}"))
                                                            {
                                                                GogUserData playerData = await gogInterface.Users(playerID);
                                                                DataCache.AddObjectPath($"Players:IDs:Gog:{playerID.ToString()}:AvatarUrl", playerData.Avatar.sdk_img_184 ?? playerData.Avatar.large_2x ?? playerData.Avatar.large);
                                                                DataCache.AddObjectPath($"Players:IDs:Gog:{playerID.ToString()}:Username", playerData.username);
                                                                DataCache.AddObjectPath($"Players:IDs:Gog:{playerID.ToString()}:ProfileUrl", $"https://www.gog.com/u/{playerData.username}");
                                                            }
                                                        }
                                                        finally
                                                        {
                                                            DataCacheLock.Release();
                                                        }
                                                    }
                                                }
                                                catch { }
                                            }
                                            break;
                                    }
                                }

                                game.Players.Add(player);
                            }

                        if (raw.GameTimeMinutes.HasValue)
                        {
                            game.Time.AddObjectPath("Seconds", raw.GameTimeMinutes * 60);
                            game.Time.AddObjectPath("Resolution", 60);
                            game.Time.AddObjectPath("Max", raw.GameTimeMinutes.Value == 255); // 255 appears to mean it maxed out?  Does for currently playing.
                            if (!string.IsNullOrWhiteSpace(ServerState))
                                game.Time.AddObjectPath("Context", ServerState);
                        }

                        MapData mapData = null;
                        if (mapDataTask != null)
                            mapData = await mapDataTask;
                        if (mapData != null)
                        {
                            game.Level["Image"] = $"{mapUrl.TrimEnd('/')}/{mapData.image ?? "nomap.png"}";
                            game.Level["Name"] = mapData?.title;
                            game.Level["Description"] = mapData?.description;
                            //game.Level.AddObjectPath("Attributes:Vehicles", new JArray(mapData.map.vehicles.Select(dr => $"{modID}:{dr}").ToArray()));

                            await ModsLock.WaitAsync();
                            if (mapData?.mods != null)
                            {
                                foreach (var mod in mapData.mods)
                                {
                                    if (!Mods.ContainsKey(mod.Key))
                                    {
                                        Mods.AddObjectPath($"{mod.Key}:Name", mod.Value?.name ?? mod.Value?.workshop_name);
                                        Mods.AddObjectPath($"{mod.Key}:ID", mod.Key);
                                        if (mod.Value?.image != null)
                                            Mods.AddObjectPath($"{mod.Key}:Image", $"{mapUrl.TrimEnd('/')}/{mod.Value.image}");
                                        if (UInt64.TryParse(mod.Key, out _))
                                        {
                                            Mods.AddObjectPath($"{mod.Key}:Url", $"http://steamcommunity.com/sharedfiles/filedetails/?id={mod.Key}");
                                        }
                                    }
                                }
                            }
                            ModsLock.Release();
                        }

                        if (m_TeamsOn)
                        {
                            game.Teams.AddObjectPath("1:Human", true);
                            game.Teams.AddObjectPath("1:Computer", false);
                            if ((mapData?.netVars?.Count ?? 0) > 0)
                            {
                                if (mapData.netVars.ContainsKey("svar1")) game.Teams.AddObjectPath("1:Name", mapData.netVars["svar1"]);
                                if (mapData.netVars.ContainsKey("svar2")) game.Teams.AddObjectPath("2:Name", mapData.netVars["svar2"]);
                            }
                            if (!m_OnlyOneTeam)
                            {
                                game.Teams.AddObjectPath("2:Human", true);
                                game.Teams.AddObjectPath("2:Computer", false);
                            }
                            else
                            {
                                game.Teams.AddObjectPath("2:Human", false);
                                game.Teams.AddObjectPath("2:Computer", true);
                            }
                        }

                        await SessionsLock.WaitAsync();
                        try
                        {
                            Sessions.Add(game);
                        }
                        finally
                        {
                            SessionsLock.Release();
                        }
                    }));
                }

                Task.WaitAll(Tasks.ToArray());

                return new GameListData()
                {
                    Metadata = Metadata,
                    SessionsDefault = DefaultSession,
                    DataCache = DataCache,
                    Sessions = Sessions,
                    Mods = Mods,
                    Raw = admin ? res : null,
                };
            }
        }
    }
}
