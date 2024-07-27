using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Plugins.RetroArchNetplay;
using MultiplayerSessionList.Services;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http;
using System;
using System.Threading.Tasks;
using Steam.Models.GameServers;
using SteamWebAPI2.Models.GameServers;
using Newtonsoft.Json.Linq;
using Okolni.Source.Query.Responses;
using Okolni.Source.Query;
using System.Threading;
using System.Numerics;

namespace MultiplayerSessionList.Plugins.Steam
{
    public class GameListModule : IGameListModule
    {
        public virtual string GameID => "steam";

        public virtual string Title => "Steam Raw";
        public bool IsPublic => false;

        protected virtual string Filter => null;

        protected virtual bool DoQueryInfo => false;
        protected virtual bool DoQueryPlayers => false;
        protected virtual bool DoQueryPlayersEvenIf0 => false;
        protected virtual bool DoQueryRules => false;

        private SteamInterface steamInterface;
        public GameListModule(IConfiguration configuration, SteamInterface steamInterface)
        {
            this.steamInterface = steamInterface;
        }
        public async Task<GameListData> GetGameList(bool admin)
        {
            GetServerResponse[] games = await steamInterface.GetGames(Filter);

            SessionItem DefaultSession = new SessionItem();
            DefaultSession.Type = GAMELIST_TERMS.TYPE_LISTEN;

            DataCache DataCache = new DataCache();
            //DataCache Mods = new DataCache();

            List<SessionItem> Sessions = new List<SessionItem>();

            List<Task> Tasks = new List<Task>();
            SemaphoreSlim DataCacheLock = new SemaphoreSlim(1);
            //SemaphoreSlim ModsLock = new SemaphoreSlim(1);
            SemaphoreSlim SessionsLock = new SemaphoreSlim(1);

            foreach (GetServerResponse raw in games)
            {
                Tasks.Add(Task.Run(async () =>
                {
                    IQueryConnection conn = null;

                    if (DoQueryInfo
                     || (DoQueryPlayers && (DoQueryPlayersEvenIf0 || raw.Players > 0))
                     || DoQueryRules)
                    {
                        conn = new QueryConnection()
                        {
                            Host = raw.Address.Split(':')[0],
                            Port = int.Parse(raw.Address.Split(':')[1]),
                        };
                        conn.Connect(); // Create the initial connection
                    }

                    // if we move this to the general handler have rules like "do QInfo, do QPlayers, do QPlayers even if 0 players, do QRules, etc."
                    InfoResponse QInfo = null;
                    if (DoQueryInfo)
                        try { QInfo = await conn.GetInfoAsync(); } catch { }

                    PlayerResponse QPlayers = null;
                    if (DoQueryPlayers && (DoQueryPlayersEvenIf0 || raw.Players > 0))
                        try { QPlayers = await conn.GetPlayersAsync(); } catch { }

                    RuleResponse QRules = null;
                    if (DoQueryRules)
                        try { QRules = await conn.GetRulesAsync(); } catch { }

                    if (QInfo != null)
                    {
                        if (QInfo.ServerType == Okolni.Source.Common.Enums.ServerType.SourceTvRelay)
                            return;

                        // not sure about this one but it seems rare
                        if (QInfo.Visibility == Okolni.Source.Common.Enums.Visibility.Private)
                            return;
                    }

                    SessionItem game = new SessionItem();

                    game.ID = raw.Address;

                    game.Name = QInfo?.Name ?? raw.Name;

                    bool hasType = false;
                    if (QInfo != null)
                    {
                        switch (QInfo.ServerType)
                        {
                            case Okolni.Source.Common.Enums.ServerType.Dedicated:
                                game.Type = GAMELIST_TERMS.TYPE_DEDICATED;
                                hasType = true;
                                break;
                            case Okolni.Source.Common.Enums.ServerType.NonDedicated:
                                game.Type = GAMELIST_TERMS.TYPE_LISTEN;
                                hasType = true;
                                break;
                        }
                    }
                    if (!hasType)
                    {
                        game.Type = raw.Dedicated ? GAMELIST_TERMS.TYPE_DEDICATED : GAMELIST_TERMS.TYPE_LISTEN;
                    }

                    // serverid $"{raw.Address}:{raw.GamePort}";

                    game.Address["IP"] = raw.Address.Split(':')[0];
                    game.Address["Port"] = raw.Address.Split(':')[1];

                    ushort GamePort = raw.GamePort;
                    if (QInfo?.HasPort ?? false)
                        GamePort = QInfo?.Port ?? GamePort;
                    game.Address["GamePort"] = GamePort;

                    game.Address["Region"] = raw.Region.ToString();

                    game.Attributes["AppID"] = raw.AppId;

                    //QInfo.Game // clean game name? seens to have capital letters

                    // would this be QInfo.GameID if QInfo.HasGameID? work that out
                    UInt32 appid = QInfo?.ID ?? raw.AppId;
                    game.Level.AddObjectPath("AppId", appid);
                    game.Level.AddObjectPath("Product", raw.Product);
                    string gamedir = QInfo?.Folder ?? raw.GameDir;
                    game.Level.AddObjectPath("GameDir", gamedir);
                    string map = QInfo?.Map ?? raw.Map;
                    game.Level.AddObjectPath("Map", map);

                    // keyworks are strange, likely need to parse
                    game.Level.AddObjectPath("KeyWords", QInfo?.KeyWords ?? raw.GameType);

                    game.Level["ID"] = $"{appid}:{raw.Product}:{gamedir}:{map}";

                    if (QInfo?.HasSteamID ?? false)
                    {
                        game.Attributes["SteamID"] = QInfo?.SteamID?.ToString() ?? raw.SteamId;
                    }
                    else if (!String.IsNullOrWhiteSpace(raw.SteamId))
                    {
                        game.Attributes["SteamID"] = raw.SteamId;
                    }
                    game.Attributes["Version"] = QInfo?.Version ?? raw.Version;
                    game.Attributes["Secure"] = QInfo?.VAC ?? raw.Secure;
                    if (QInfo?.HasSourceTv ?? false)
                    {
                        game.Attributes.AddObjectPath("SourceTV:Port", QInfo.SourceTvPort);
                        game.Attributes.AddObjectPath("SourceTV:Name", QInfo.SourceTvName);
                    }
                    bool hasOS = false;
                    if (QInfo != null)
                    {
                        switch (QInfo.Environment)
                        {
                            case Okolni.Source.Common.Enums.Environment.Windows:
                                game.Attributes["OS"] = "Windows";
                                hasOS = true;
                                break;
                            case Okolni.Source.Common.Enums.Environment.Linux:
                                game.Attributes["OS"] = "Linux";
                                hasOS = true;
                                break;
                            case Okolni.Source.Common.Enums.Environment.Mac:
                                game.Attributes["OS"] = "MacOS";
                                hasOS = true;
                                break;
                        }
                    }
                    if (!hasOS)
                    {
                        switch (raw.OS)
                        {
                            case 'w':
                                game.Attributes["OS"] = "Windows";
                                break;
                            case 'l':
                                game.Attributes["OS"] = "Linux";
                                break;
                            case 'm':
                                game.Attributes["OS"] = "MacOS";
                                break;
                            default:
                                game.Attributes["OS"] = $"Unknown ({raw.OS.ToString()})";
                                break;
                        }
                    }

                    game.PlayerTypes.Add(new PlayerTypeData()
                    {
                        Types = new List<string>() { GAMELIST_TERMS.PLAYERTYPE_PLAYER },
                        Max = QInfo?.MaxPlayers ?? (int)raw.MaxPlayers
                    });
                    game.PlayerCount.Add(GAMELIST_TERMS.PLAYERTYPE_PLAYER, QInfo?.Players ?? (int)raw.Players);

                    // does The Ship have bots?
                    //game.PlayerTypes.Add(new PlayerTypeData()
                    //{
                    //    Types = new List<string>() { GAMELIST_TERMS.PLAYERTYPE_BOT }
                    //});
                    //game.PlayerCount.Add(GAMELIST_TERMS.PLAYERTYPE_BOT, QInfo?.Bots ?? (int)raw.Bots);


                    if (QInfo != null && QInfo.IsTheShip)
                    {
                        switch (QInfo.Mode)
                        {
                            case Okolni.Source.Common.Enums.TheShipMode.Hunt:
                                game.Level.AddObjectPath("GameType:ID", "HUNT");
                                game.Level.AddObjectPath("GameMode:ID", "HUNT");
                                await DataCacheLock.WaitAsync();
                                try
                                {
                                    if (!DataCache.ContainsPath($"Level:GameType:HUNT"))
                                        DataCache.AddObjectPath($"Level:GameType:HUNT:Name", "Hunt");
                                    if (!DataCache.ContainsPath($"Level:GameMode:HUNT"))
                                        DataCache.AddObjectPath($"Level:GameMode:HUNT:Name", "Hunt");
                                }
                                finally
                                {
                                    DataCacheLock.Release();
                                }
                                break;
                            case Okolni.Source.Common.Enums.TheShipMode.Elimination:
                                game.Level.AddObjectPath("GameType:ID", "ELIMINATION");
                                game.Level.AddObjectPath("GameMode:ID", "ELIMINATION");
                                await DataCacheLock.WaitAsync();
                                try
                                {
                                    if (!DataCache.ContainsPath($"Level:GameType:ELIMINATION"))
                                        DataCache.AddObjectPath($"Level:GameType:ELIMINATION:Name", "Elimination");
                                    if (!DataCache.ContainsPath($"Level:GameMode:ELIMINATION"))
                                        DataCache.AddObjectPath($"Level:GameMode:ELIMINATION:Name", "Elimination");
                                }
                                finally
                                {
                                    DataCacheLock.Release();
                                }
                                break;
                            case Okolni.Source.Common.Enums.TheShipMode.Duel:
                                game.Level.AddObjectPath("GameType:ID", "DUEL");
                                game.Level.AddObjectPath("GameMode:ID", "DUEL");
                                await DataCacheLock.WaitAsync();
                                try
                                {
                                    if (!DataCache.ContainsPath($"Level:GameType:DUEL"))
                                        DataCache.AddObjectPath($"Level:GameType:DUEL:Name", "Duel");
                                    if (!DataCache.ContainsPath($"Level:GameMode:DUEL"))
                                        DataCache.AddObjectPath($"Level:GameMode:DUEL:Name", "Duel");
                                }
                                finally
                                {
                                    DataCacheLock.Release();
                                }
                                break;
                            case Okolni.Source.Common.Enums.TheShipMode.Deathmatch:
                                game.Level.AddObjectPath("GameType:ID", "DEATHMATCH");
                                game.Level.AddObjectPath("GameMode:ID", "DEATHMATCH");
                                await DataCacheLock.WaitAsync();
                                try
                                {
                                    if (!DataCache.ContainsPath($"Level:GameType:DEATHMATCH"))
                                        DataCache.AddObjectPath($"Level:GameType:DEATHMATCH:Name", "Deathmatch");
                                    if (!DataCache.ContainsPath($"Level:GameMode:DEATHMATCH"))
                                        DataCache.AddObjectPath($"Level:GameMode:DEATHMATCH:Name", "Deathmatch");
                                }
                                finally
                                {
                                    DataCacheLock.Release();
                                }
                                break;
                            case Okolni.Source.Common.Enums.TheShipMode.VipTeam:
                                game.Level.AddObjectPath("GameType:ID", "VIP_ELIMINATION");
                                game.Level.AddObjectPath("GameMode:ID", "VIP_ELIMINATION");
                                await DataCacheLock.WaitAsync();
                                try
                                {
                                    if (!DataCache.ContainsPath($"Level:GameType:VIP_ELIMINATION"))
                                        DataCache.AddObjectPath($"Level:GameType:VIP_ELIMINATION:Name", "VIP Elimination");
                                    if (!DataCache.ContainsPath($"Level:GameMode:VIP_ELIMINATION"))
                                        DataCache.AddObjectPath($"Level:GameMode:VIP_ELIMINATION:Name", "VIP Elimination");
                                }
                                finally
                                {
                                    DataCacheLock.Release();
                                }
                                break;
                            case Okolni.Source.Common.Enums.TheShipMode.TeamElimination:
                                game.Level.AddObjectPath("GameType:ID", "TEAM_ELIMINATION");
                                game.Level.AddObjectPath("GameMode:ID", "TEAM_ELIMINATION");
                                await DataCacheLock.WaitAsync();
                                try
                                {
                                    if (!DataCache.ContainsPath($"Level:GameType:TEAM_ELIMINATION"))
                                        DataCache.AddObjectPath($"Level:GameType:TEAM_ELIMINATION:Name", "Team Elimination");
                                    if (!DataCache.ContainsPath($"Level:GameMode:TEAM_ELIMINATION"))
                                        DataCache.AddObjectPath($"Level:GameMode:TEAM_ELIMINATION:Name", "Team Elimination");
                                }
                                finally
                                {
                                    DataCacheLock.Release();
                                }
                                break;
                        }

                        if (QInfo.Witnesses.HasValue)
                            game.Level.AddObjectPath("Attributes:Witness:Count", QInfo.Witnesses);
                        if (QInfo.Duration.HasValue)
                            game.Level.AddObjectPath("Attributes:Witness:Duration", QInfo.Duration.Value.TotalSeconds);
                    }

                    if (QPlayers != null)
                    {
                        // interesting AI suggested code that needs investigation
                        /*QPlayers.Players.ForEach(p =>
                        {
                            if (p.Name == "NPC")
                            {
                                game.PlayerTypes.Add(new PlayerTypeData()
                                {
                                    Types = new List<string>() { GAMELIST_TERMS.PLAYERTYPE_BOT },
                                    Max = 1
                                });
                                game.PlayerCount.Add(GAMELIST_TERMS.PLAYERTYPE_BOT, 1);
                            }
                        });*/
                        foreach (var qplayer in QPlayers.Players)
                        {
                            PlayerItem player = new PlayerItem();

                            // no player steamID? seems strange to omit that
                            player.Name = qplayer.Name;
                            player.Type = GAMELIST_TERMS.PLAYERTYPE_PLAYER;
                            player.GetIDData("Slot").Add("ID", qplayer.Index);

                            if (qplayer.Deaths.HasValue)
                                player.Stats.Add("Deaths", qplayer.Deaths);
                            player.Stats.Add("Score", qplayer.Score);

                            // only some games use this, counterstrike stuff
                            if (qplayer.Money.HasValue)
                                player.Attributes.Add("Money", qplayer.Money);

                            // connection duration
                            player.Attributes.Add("Duration", qplayer.Duration.TotalSeconds);

                            game.Players.Add(player);
                        }
                    }

                    if (QRules != null)
                    {
                        // TODO account for alternate format rules here with a rule parsing function
                        foreach(var qrule in QRules.Rules)
                        {
                            game.Attributes.AddObjectPath($"Rules:{qrule.Key}", qrule.Value);
                        }
                    }

                    if (admin)
                    {
                        try
                        {
                            //game.Attributes.AddObjectPath("Raw:Http", JsonConvert.SerializeObject(raw));
                            if (QInfo != null) game.Attributes.AddObjectPath("Raw-Info", JObject.FromObject(QInfo));
                            if (QPlayers != null) game.Attributes.AddObjectPath("Raw-Players", JObject.FromObject(QPlayers));
                            if (QRules != null) game.Attributes.AddObjectPath("Raw-Rules", JObject.FromObject(QRules));
                        }
                        catch { }
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
                SessionsDefault = DefaultSession,
                Sessions = Sessions,
                //Raw = admin ? res : null,
            };
        }
    }
}
