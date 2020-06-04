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

namespace MultiplayerSessionList.Plugins.Battlezone98Redux
{
    public class GameListModule : IGameListModule
    {
        public string GameID => "bzrnet:bz98r";
        public string Title => "Battlezone 98 Redux";


        private string queryUrl;
        private GogInterface gogInterface;
        private SteamInterface steamInterface;

        public GameListModule(IConfiguration configuration, GogInterface gogInterface, SteamInterface steamInterface)
        {
            queryUrl = configuration["rebellion:battlezone_98_redux"];
            this.gogInterface = gogInterface;
            this.steamInterface = steamInterface;
        }

        public async Task<(SessionItem, IEnumerable<SessionItem>, JToken)> GetGameList()
        {
            using (var http = new HttpClient())
            {
                var res = await http.GetStringAsync(queryUrl).ConfigureAwait(false);
                var gamelist = JsonConvert.DeserializeObject<Dictionary<string, Lobby>>(res);

                SessionItem DefaultSession = new SessionItem()
                {
                    Type = "listen",
                    SpectatorPossible = false, // unless we add special mod support
                    //SpectatorSeperate = false,
                };

                List<SessionItem> Sessions = new List<SessionItem>();

                foreach (var raw in gamelist.Values)
                {
                    if (raw.LobbyType != Lobby.ELobbyType.Game)
                        continue;

                    SessionItem game = new SessionItem();

                    game.Name = raw.Name;
                    //if (!string.IsNullOrWhiteSpace(raw.MOTD))
                    //    game.Message = raw.MOTD;

                    game.PlayerCount = raw.userCount;
                    game.PlayerMax = raw.PlayerLimit;

                    game.Level.Add("MapFile", raw.MapFile);
                    game.Level.Add("MapID", GameID + @":" + (raw.WorkshopID ?? @"0") + @":" + raw.MapFile);

                    game.Status.Add("Locked", raw.isLocked);
                    game.Status.Add("Passworded", raw.IsPassworded);

                    game.Status.Add("State", raw.IsEnded ? "Over" : raw.IsLaunched ? "InGame" : "Lobby");

                    if (!string.IsNullOrWhiteSpace(raw.WorkshopID))
                        game.Attributes.Add("Mod", raw.WorkshopID);

                    if (!string.IsNullOrWhiteSpace(raw.clientVersion))
                        game.Attributes.Add("Version", raw.clientVersion);

                    if (raw.TimeLimit.HasValue && raw.TimeLimit > 0)
                        game.Attributes.Add("TimeLimit", raw.TimeLimit);

                    if (raw.KillLimit.HasValue && raw.KillLimit > 0)
                        game.Attributes.Add("KillLimit", raw.KillLimit);

                    if (raw.Lives.HasValue && raw.Lives.Value > 0)
                        game.Attributes.Add("Lives", raw.Lives.Value);
                    if (raw.SyncJoin.HasValue)
                        game.Attributes.Add("SyncJoin", raw.SyncJoin.Value);
                    if (raw.SatelliteEnabled.HasValue)
                        game.Attributes.Add("Satellite", raw.SatelliteEnabled.Value);
                    if (raw.BarracksEnabled.HasValue)
                        game.Attributes.Add("Barracks", raw.BarracksEnabled.Value);
                    if (raw.SniperEnabled.HasValue)
                        game.Attributes.Add("Sniper", raw.SniperEnabled.Value);
                    if (raw.SplinterEnabled.HasValue)
                        game.Attributes.Add("Splinter", raw.SplinterEnabled.Value);

                    foreach (var dr in raw.users.Values)
                    {
                        PlayerItem player = new PlayerItem();

                        player.Name = dr.name;

                        if (dr.Team.HasValue)
                        {
                            player.Team = new PlayerTeam();
                            player.Team.ID = dr.Team;
                            player.GetIDData("Slot").Add("ID", dr.Team);
                        }

                        player.Attributes.Add("Vehicle", dr.Vehicle);

                        if (!string.IsNullOrWhiteSpace(dr.id))
                        {
                            player.GetIDData("BZRNet").Add("ID", dr.id);
                            switch (dr.id[0]) 
                            {
                                case 'S': // dr.authType == "steam"
                                    {
                                        player.GetIDData("Steam").Add("Raw", dr.id.Substring(1));
                                        try
                                        {
                                            ulong playerID = 0;
                                            if (ulong.TryParse(dr.id.Substring(1), out playerID))
                                            {
                                                player.GetIDData("Steam").Add("ID", playerID);

                                                PlayerSummaryModel playerData = await steamInterface.Users(playerID);
                                                player.GetIDData("Steam").Add("AvatarUrl", playerData.AvatarFullUrl);
                                                player.GetIDData("Steam").Add("Nickname", playerData.Nickname);
                                                player.GetIDData("Steam").Add("ProfileUrl", playerData.ProfileUrl);
                                            }
                                        }
                                        catch { }
                                    }
                                    break;
                                case 'G':
                                    {
                                        player.GetIDData("Gog").Add("Raw", dr.id.Substring(1));
                                        try
                                        {
                                            ulong playerID = 0;
                                            if (ulong.TryParse(dr.id.Substring(1), out playerID))
                                            {
                                                //player.GetIDData("Gog").Add("LargeID", playerID);
                                                playerID &= 0x00ffffffffffffff;
                                                player.GetIDData("Gog").Add("ID", playerID);

                                                GogUserData playerData = await gogInterface.Users(playerID);
                                                player.GetIDData("Gog").Add("AvatarUrl", playerData.Avatar.sdk_img_184 ?? playerData.Avatar.large_2x ?? playerData.Avatar.large);
                                                player.GetIDData("Gog").Add("UserName", playerData.username);
                                                player.GetIDData("Gog").Add("ProfileUrl", $"https://www.gog.com/u/{playerData.username}");
                                            }
                                        }
                                        catch { }
                                    }
                                    break;
                            }
                        }

                        game.Players.Add(player);
                    }

                    Sessions.Add(game);
                }

                return (DefaultSession, Sessions, JObject.Parse(res));
            }
        }
    }
}
