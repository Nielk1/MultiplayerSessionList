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
using System.Threading.Tasks;
using static MultiplayerSessionList.Services.GogInterface;

namespace MultiplayerSessionList.Plugins.BattlezoneCombatCommander
{
    public class GameListModule : IGameListModule
    {
        public string GameID => "iondriver:raknetmaster2:bzcc";
        public string Title => "Battlezone Combat Commander";




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
        private GogInterface gogInterface;
        private SteamInterface steamInterface;

        public GameListModule(IConfiguration configuration, GogInterface gogInterface, SteamInterface steamInterface)
        {
            queryUrl = configuration["rebellion:battlezone_combat_commander"];
            this.gogInterface = gogInterface;
            this.steamInterface = steamInterface;
        }

        public async Task<(SessionItem, DataCache, IEnumerable<SessionItem>, JToken)> GetGameList()
        {
            using (var http = new HttpClient())
            {
                var res = await http.GetStringAsync(queryUrl).ConfigureAwait(false);
                var gamelist = JsonConvert.DeserializeObject<BZCCRaknetData>(res);

                SessionItem DefaultSession = new SessionItem()
                {
                    Type = GAMELIST_TERMS.TYPE_LISTEN,
                };

                DataCache DataCache = new DataCache();

                List<SessionItem> Sessions = new List<SessionItem>();

                foreach (var raw in gamelist.GET)
                {
                    SessionItem game = new SessionItem();

                    game.Address["NAT"] = raw.d;
                    if (!raw.Passworded)
                    {
                        game.Address["Rich"] = string.Join(null, $"N,{raw.Name.Length},{raw.Name},{raw.mm.Length},{raw.mm},{raw.g},0,".Select(dr => $"{((int)dr):x2}"));
                    }

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
                    game.Level["ID"] = (raw.Mods?.FirstOrDefault() ?? @"0") + @":" + raw.MapFile?.ToLowerInvariant();

                    game.Status.Add(GAMELIST_TERMS.STATUS_LOCKED, raw.Locked);
                    game.Status.Add(GAMELIST_TERMS.STATUS_PASSWORD, raw.Passworded);

                    string ServerState = null;
                    if (raw.ServerInfoMode.HasValue)
                    {
                        switch (raw.ServerInfoMode)
                        {
                            case 0: // ServerInfoMode_Unknown
                                ServerState =  Enum.GetName(typeof(ESessionState), ESessionState.Unknown);
                                break;
                            case 1: // ServerInfoMode_OpenWaiting
                            case 2: // ServerInfoMode_ClosedWaiting (full)
                                ServerState =  Enum.GetName(typeof(ESessionState), ESessionState.PreGame);
                                break;
                            case 3: // ServerInfoMode_OpenPlaying
                            case 4: // ServerInfoMode_ClosedPlaying (full)
                                ServerState =  Enum.GetName(typeof(ESessionState), ESessionState.InGame);
                                break;
                            case 5: // ServerInfoMode_Exiting
                                ServerState =  Enum.GetName(typeof(ESessionState), ESessionState.PostGame);
                                break;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(ServerState))
                        game.Status.Add("State", ServerState);


                    if ((raw.Mods?.Length ?? 0) > 0)
                        game.Game.Add("Mods", JArray.FromObject(raw.Mods));

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

                    if (string.IsNullOrWhiteSpace(raw.proxySource))
                    {
                        game.Attributes.Add("ListServer", $"IonDriver");
                    }
                    else
                    {
                        game.Attributes.Add("ListServer", raw.proxySource);
                    }

                    bool m_TeamsOn = false;
                    bool m_OnlyOneTeam = false;
                    switch (raw.GameType)
                    {
                        case 0:
                            game.Level["GameMode"] = $"All";
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
                                    case 0:
                                        //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"DM");
                                        game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"DM";
                                        break;
                                    case 1:
                                        //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"KOTH");
                                        game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"KOTH";
                                        break;
                                    case 2:
                                        //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"CTF");
                                        game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"CTF";
                                        break;
                                    case 3:
                                        //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"Loot");
                                        game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"Loot";
                                        break;
                                    case 4:
                                        //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"DM [RESERVED]");
                                        game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"DM [RESERVED]";
                                        break;
                                    case 5:
                                        //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"Race");
                                        game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"Race";
                                        break;
                                    case 6:
                                        //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"Race (Vehicle Only)");
                                        game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"Race (Vehicle Only)";
                                        break;
                                    case 7:
                                        //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"DM (Vehicle Only)");
                                        game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"DM (Vehicle Only)";
                                        break;
                                    default:
                                        //game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"DM [UNKNOWN {raw.GameSubType}]");
                                        game.Level["GameMode"] = (m_TeamsOn ? "TEAM " : String.Empty) + $"DM [UNKNOWN {raw.GameSubType}]";
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
                                        //game.Level["GameMode"] = $"TEAM STRAT");
                                        game.Level["GameMode"] = $"TEAM STRAT";
                                        m_TeamsOn = true;
                                        m_OnlyOneTeam = false;
                                        break;
                                    case GameMode.GAMEMODE_STRAT:
                                        //game.Level["GameMode"] = $"STRAT");
                                        game.Level["GameMode"] = $"STRAT";
                                        m_TeamsOn = false;
                                        m_OnlyOneTeam = false;
                                        break;
                                    case GameMode.GAMEMODE_MPI:
                                        //game.Level["GameMode"] = $"MPI");
                                        game.Level["GameMode"] = $"MPI";
                                        m_TeamsOn = true;
                                        m_OnlyOneTeam = true;
                                        break;
                                    default:
                                        //game.Level["GameMode"] = $"STRAT [UNKNOWN {GetGameModeOutput}]");
                                        game.Level["GameMode"] = $"STRAT [UNKNOWN {GetGameModeOutput}]";
                                        break;
                                }
                            }
                            break;
                        case 3: // impossible, BZCC limits to 0-2
                            //game.Attributes.Add("GameMode", $"MPI [Invalid]");
                            game.Level["GameMode"] = $"MPI [Invalid]";
                            break;
                    }

                    if (!string.IsNullOrWhiteSpace(raw.d))
                    {
                        game.Game.Add("ModHash", raw.d); // base64 encoded CRC32
                    }

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

                                                if (!DataCache.ContainsPath($"Players:IDs:Steam:{playerID.ToString()}"))
                                                {
                                                    PlayerSummaryModel playerData = await steamInterface.Users(playerID);
                                                    DataCache.AddObjectPath($"Players:IDs:Steam:{playerID.ToString()}:AvatarUrl", playerData.AvatarFullUrl);
                                                    DataCache.AddObjectPath($"Players:IDs:Steam:{playerID.ToString()}:Nickname", playerData.Nickname);
                                                    DataCache.AddObjectPath($"Players:IDs:Steam:{playerID.ToString()}:ProfileUrl", playerData.ProfileUrl);
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

                                                if (!DataCache.ContainsPath($"Players:IDs:Gog:{playerID.ToString()}"))
                                                {
                                                    GogUserData playerData = await gogInterface.Users(playerID);
                                                    DataCache.AddObjectPath($"Players:IDs:Gog:{playerID.ToString()}:AvatarUrl", playerData.Avatar.sdk_img_184 ?? playerData.Avatar.large_2x ?? playerData.Avatar.large);
                                                    DataCache.AddObjectPath($"Players:IDs:Gog:{playerID.ToString()}:UserName", playerData.username);
                                                    DataCache.AddObjectPath($"Players:IDs:Gog:{playerID.ToString()}:ProfileUrl", $"https://www.gog.com/u/{playerData.username}");
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

                    Sessions.Add(game);
                }

                return (DefaultSession, DataCache, Sessions, JObject.Parse(res));
            }
        }
    }
}
