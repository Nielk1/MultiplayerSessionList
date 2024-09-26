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
using System.Text.RegularExpressions;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;

namespace MultiplayerSessionList.Plugins.Steam
{
    public class GameListModule : IGameListModuleOld
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
            GetServerResponse[] games = await steamInterface.GetGames(Filter, 2048);

            SessionItem DefaultSession = new SessionItem();
            DefaultSession.Type = GAMELIST_TERMS_OLD.TYPE_LISTEN;

            DataCacheOld DataCache = new DataCacheOld();
            //DataCache Mods = new DataCache();

            List<SessionItem> Sessions = new List<SessionItem>();

            List<Task> Tasks = new List<Task>();
            SemaphoreSlim DataCacheLock = new SemaphoreSlim(1);
            //SemaphoreSlim ModsLock = new SemaphoreSlim(1);
            SemaphoreSlim SessionsLock = new SemaphoreSlim(1);

            foreach (GetServerResponse response in games)
            {
                Tasks.Add(Task.Run(async () =>
                {
                    IQueryConnection conn = null;

                    if (DoQueryInfo
                     || (DoQueryPlayers && (DoQueryPlayersEvenIf0 || response.Players > 0))
                     || DoQueryRules)
                    {
                        conn = new QueryConnection()
                        {
                            Host = response.Address.Split(':')[0],
                            Port = int.Parse(response.Address.Split(':')[1]),
                        };
                        conn.Connect(); // Create the initial connection
                    }

                    // if we move this to the general handler have rules like "do QInfo, do QPlayers, do QPlayers even if 0 players, do QRules, etc."
                    InfoResponse QInfo = null;
                    Task<InfoResponse> QInfoT = null;
                    if (DoQueryInfo)
                        try { QInfoT = conn.GetInfoAsync(); } catch { }

                    PlayerResponse QPlayers = null;
                    Task<PlayerResponse> QPlayersT = null;
                    if (DoQueryPlayers && (DoQueryPlayersEvenIf0 || response.Players > 0))
                        try { QPlayersT = conn.GetPlayersAsync(); } catch { }

                    RuleResponse QRules = null;
                    Task<RuleResponse> QRulesT = null;
                    if (DoQueryRules)
                        try { QRulesT = conn.GetRulesAsync(); } catch { }

                    try
                    {
                        if (QInfoT != null)
                            QInfo = await QInfoT;
                    }
                    catch { }
                    try
                    {
                        if (QPlayers != null)
                            QPlayers = await QPlayersT;
                    }
                    catch { }
                    try
                    {
                        if (QRules != null)
                            QRules = await QRulesT;
                    }
                    catch { }

                    if (QInfo != null)
                    {
                        if (QInfo.ServerType == Okolni.Source.Common.Enums.ServerType.SourceTvRelay)
                            return;

                        // not sure about this one but it seems rare
                        if (QInfo.Visibility == Okolni.Source.Common.Enums.Visibility.Private)
                            return;
                    }

                    SessionItem game = new SessionItem();

                    await BuildGameDataAsync(DataCache, DataCacheLock, game, response);
                    await BuildGameDataAsync(DataCache, DataCacheLock, game, QInfo);
                    await BuildGameDataAsync(DataCache, DataCacheLock, game, QPlayers);
                    await BuildGameDataAsync(DataCache, DataCacheLock, game, QRules);

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

        public async IAsyncEnumerable<Datum> GetGameListChunksAsync(bool admin, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }

        protected virtual async Task BuildGameDataAsync(DataCacheOld DataCache, SemaphoreSlim DataCacheLock, SessionItem game, GetServerResponse response)
        {
            game.ID = response.Address;
            game.Name = response.Name;

            // remove Unreal Tournament color codes (for KF)
            game.Name = Regex.Replace(game.Name, "\u001b[@`¤ Ąą\ufffd\u003f]{3}", string.Empty);
            // remove strange characters (for KF)
            game.Name = game.Name.Replace('\ufffd', ' ');

            // '@' = 0x40
            // '`' = 0x60
            // '¤' = 0x80
            // ' ' = 0xA0
            // 'Ą' = 0xC0
            // 'ą' = 0xE0
            // \ufffd - unknown (side effect of bad parsing?)
            // \u003f - unknown (side effect of bad parsing?)
            game.Type = game.Type ?? (response.Dedicated ? GAMELIST_TERMS_OLD.TYPE_DEDICATED : GAMELIST_TERMS_OLD.TYPE_LISTEN);

            // serverid $"{raw.Address}:{raw.GamePort}";

            game.Address["IP"] = response.Address.Split(':')[0];
            game.Address["Port"] = response.Address.Split(':')[1];
            game.Address["GamePort"] = response.GamePort;
            game.Address["Region"] = response.Region.ToString();

            game.Attributes["AppID"] = response.AppId;

            game.Level.AddObjectPath("AppId", response.AppId);
            game.Level.AddObjectPath("Product", response.Product);
            game.Level.AddObjectPath("GameDir", response.GameDir);
            game.Level.AddObjectPath("Map", response.Map);

            // keyworks are strange, likely need to parse
            game.Level.AddObjectPath("KeyWords", response.GameType);

            game.Level["ID"] = $"{game.Level["AppId"]}:{response.Product}:{game.Level["GameDir"]}:{game.Level["Map"]}";

            if (!string.IsNullOrWhiteSpace(response.SteamId))
                game.Attributes.AddObjectPath("SteamID", response.SteamId);

            game.Attributes.AddObjectPath("Version", response.Version);
            game.Attributes.AddObjectPath("Secure", response.Secure);

            switch (response.OS)
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
                case 'o': // OSX is possible, documentation notes "(the code changed after L4D1)"
                    game.Attributes["OS"] = "MacOS";
                    break;
                default:
                    game.Attributes["OS"] = $"Unknown ({response.OS.ToString()})";
                    break;
            }

            game.PlayerTypes.Add(new PlayerTypeData()
            {
                Types = new List<string>() { GAMELIST_TERMS_OLD.PLAYERTYPE_PLAYER },
                Max = (int)response.MaxPlayers
            });
            game.PlayerCount.Add(GAMELIST_TERMS_OLD.PLAYERTYPE_PLAYER, (int)response.Players);
        }
        protected virtual async Task BuildGameDataAsync(DataCacheOld DataCache, SemaphoreSlim DataCacheLock, SessionItem game, InfoResponse QInfo)
        {
            if (QInfo == null)
                return;

            game.Name = QInfo.Name;

            switch (QInfo.ServerType)
            {
                case Okolni.Source.Common.Enums.ServerType.Dedicated:
                    game.Type = GAMELIST_TERMS_OLD.TYPE_DEDICATED;
                    break;
                case Okolni.Source.Common.Enums.ServerType.NonDedicated:
                    game.Type = GAMELIST_TERMS_OLD.TYPE_LISTEN;
                    break;
                    // server type SourceTvRelay is not handled, we don't want to even list them
                    //case Okolni.Source.Common.Enums.ServerType.SourceTvRelay:
                    //    continue;
            }

            if (QInfo?.HasPort ?? false)
                game.Address["GamePort"] = QInfo.Port;

            //QInfo.Game // clean game name? seens to have capital letters
            // would this be QInfo.GameID if QInfo.HasGameID? work that out

            game.Level.AddObjectPath("AppId", QInfo.ID);
            game.Level.AddObjectPath("GameDir", QInfo.Folder);
            game.Level.AddObjectPath("Map", QInfo.Map);

            // keyworks are strange, likely need to parse
            game.Level.AddObjectPath("KeyWords", QInfo.KeyWords);

            if (QInfo.HasSteamID)
                game.Attributes["SteamID"] = QInfo.SteamID.ToString();

            game.Attributes.AddObjectPath("Version", QInfo.Version);
            game.Attributes.AddObjectPath("Secure", QInfo.VAC);

            if (QInfo?.HasSourceTv ?? false)
            {
                game.Attributes.AddObjectPath("SourceTV:Port", QInfo.SourceTvPort);
                game.Attributes.AddObjectPath("SourceTV:Name", QInfo.SourceTvName);
            }

            switch (QInfo.Environment)
            {
                case Okolni.Source.Common.Enums.Environment.Windows:
                    game.Attributes["OS"] = "Windows";
                    break;
                case Okolni.Source.Common.Enums.Environment.Linux:
                    game.Attributes["OS"] = "Linux";
                    break;
                case Okolni.Source.Common.Enums.Environment.Mac:
                    game.Attributes["OS"] = "MacOS";
                    break;
            }

            game.PlayerTypes = new List<PlayerTypeData>();
            game.PlayerCount = new Dictionary<string, int?>();
            game.PlayerTypes.Add(new PlayerTypeData()
            {
                Types = new List<string>() { GAMELIST_TERMS_OLD.PLAYERTYPE_PLAYER },
                Max = QInfo.MaxPlayers
            });
            game.PlayerCount[GAMELIST_TERMS_OLD.PLAYERTYPE_PLAYER] = QInfo.Players;

            // does The Ship have bots?
            //game.PlayerTypes.Add(new PlayerTypeData()
            //{
            //    Types = new List<string>() { GAMELIST_TERMS.PLAYERTYPE_BOT }
            //});
            //game.PlayerCount.Add(GAMELIST_TERMS.PLAYERTYPE_BOT, QInfo?.Bots ?? (int)raw.Bots);

            if (QInfo.IsTheShip)
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
        }
        protected virtual async Task BuildGameDataAsync(DataCacheOld DataCache, SemaphoreSlim DataCacheLock, SessionItem game, PlayerResponse QPlayers)
        {
            if (QPlayers == null)
                return;

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
                player.Type = GAMELIST_TERMS_OLD.PLAYERTYPE_PLAYER;
                player.GetIDData("Slot")["ID"] = qplayer.Index;

                if (qplayer.Deaths.HasValue)
                    player.Stats["Deaths"] = qplayer.Deaths;
                player.Stats["Score"] = qplayer.Score;

                // only some games use this, counterstrike stuff
                if (qplayer.Money.HasValue)
                    player.Attributes["Money"] = qplayer.Money;

                // connection duration
                player.Attributes["Duration"] = qplayer.Duration.TotalSeconds;

                game.Players.Add(player);
            }
        }
        protected virtual async Task BuildGameDataAsync(DataCacheOld DataCache, SemaphoreSlim DataCacheLock, SessionItem game, RuleResponse QRules)
        {
            if (QRules == null)
                return;

            // TODO account for alternate format rules here with a rule parsing function
            foreach (var qrule in QRules.Rules)
            {
                game.Attributes.AddObjectPath($"Rules:{qrule.Key}", qrule.Value);
            }
        }
    }
}
