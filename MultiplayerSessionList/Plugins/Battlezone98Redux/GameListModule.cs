using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Steam.Models.SteamCommunity;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using static MultiplayerSessionList.Services.GogInterface;

namespace MultiplayerSessionList.Plugins.Battlezone98Redux
{
    public class GameListModule : IGameListModule
    {
        public string GameID => "bigboat:battlezone_98_redux";
        public string Title => "Battlezone 98 Redux";


        private string queryUrl;
        private string mapUrl;
        private GogInterface gogInterface;
        private SteamInterface steamInterface;
        private CachedWebClient<MapData> mapDataInterface;

        public GameListModule(IConfiguration configuration, GogInterface gogInterface, SteamInterface steamInterface, CachedWebClient<MapData> mapDataInterface)
        {
            queryUrl = configuration["bigboat:battlezone_98_redux:sessions"];
            mapUrl = configuration["bigboat:battlezone_98_redux:maps"];
            this.gogInterface = gogInterface;
            this.steamInterface = steamInterface;
            this.mapDataInterface = mapDataInterface;
        }

        public async Task<GameListData> GetGameList(bool admin)
        {
            using (var http = new HttpClient())
            {
                var res_raw = await http.GetAsync(queryUrl).ConfigureAwait(false);
                var res = await res_raw.Content.ReadAsStringAsync();
                var gamelist = JsonConvert.DeserializeObject<Dictionary<string, Lobby>>(res);

                SessionItem DefaultSession = new SessionItem();
                DefaultSession.Type = GAMELIST_TERMS.TYPE_LISTEN;
                DefaultSession.Attributes.Add(GAMELIST_TERMS.ATTRIBUTE_LISTSERVER, $"Rebellion");

                DataCache Metadata = new DataCache();
                if (res_raw.Content.Headers.LastModified.HasValue)
                    Metadata.AddObjectPath($"{GAMELIST_TERMS.ATTRIBUTE_LISTSERVER}:Rebellion:Timestamp", res_raw.Content.Headers.LastModified.Value.ToUniversalTime().UtcDateTime);

                DataCache DataCache = new DataCache();
                DataCache Heroes = new DataCache();

                List<SessionItem> Sessions = new List<SessionItem>();

                List<Task> Tasks = new List<Task>();
                SemaphoreSlim DataCacheLock = new SemaphoreSlim(1);
                SemaphoreSlim HeroesLock = new SemaphoreSlim(1);
                SemaphoreSlim SessionsLock = new SemaphoreSlim(1);

                foreach (var raw in gamelist.Values)
                {
                    if (raw.LobbyType != Lobby.ELobbyType.Game)
                        continue;

                    if (raw.isPrivate && !(raw.IsPassworded ?? false))
                        continue;

                    Tasks.Add(Task.Run(async () =>
                    {
                        SessionItem game = new SessionItem();

                        game.Name = raw.Name;

                        game.Address["LobbyID"] = $"B{raw.id}";

                        game.PlayerTypes.Add(new PlayerTypeData()
                        {
                            Types = new List<string>() { GAMELIST_TERMS.PLAYERTYPE_PLAYER },
                            Max = raw.PlayerLimit
                        });

                        game.PlayerCount.Add(GAMELIST_TERMS.PLAYERTYPE_PLAYER, raw.userCount);

                        string modID = (raw.WorkshopID ?? @"0");
                        string mapID = System.IO.Path.GetFileNameWithoutExtension(raw.MapFile).ToLowerInvariant();
                        game.Level["ID"] = $"{modID}:{mapID}";

                        game.Level["MapFile"] = raw.MapFile;
                        game.Level["CRC32"] = raw.CRC32;

                        Task<MapData> mapDataTask = mapDataInterface.GetJson($"{mapUrl.TrimEnd('/')}/getdata.php?map={mapID}&mods={modID},0");

                        if (!string.IsNullOrWhiteSpace(raw.WorkshopID)) game.Level.Add("Mod", raw.WorkshopID);

                        if (raw.TimeLimit.HasValue && raw.TimeLimit > 0) game.Level.AddObjectPath("Attributes:TimeLimit", raw.TimeLimit);
                        if (raw.KillLimit.HasValue && raw.KillLimit > 0) game.Level.AddObjectPath("Attributes:KillLimit", raw.KillLimit);
                        if (raw.Lives.HasValue && raw.Lives.Value > 0) game.Level.AddObjectPath("Attributes:Lives", raw.Lives.Value);
                        if (raw.SatelliteEnabled.HasValue) game.Level.AddObjectPath("Attributes:Satellite", raw.SatelliteEnabled.Value);
                        if (raw.BarracksEnabled.HasValue) game.Level.AddObjectPath("Attributes:Barracks", raw.BarracksEnabled.Value);
                        if (raw.SniperEnabled.HasValue) game.Level.AddObjectPath("Attributes:Sniper", raw.SniperEnabled.Value);
                        if (raw.SplinterEnabled.HasValue) game.Level.AddObjectPath("Attributes:Splinter", raw.SplinterEnabled.Value);

                        // unlocked in progress games with SyncJoin will trap the user due to a bug, just list as locked
                        if (raw.SyncJoin.HasValue && raw.SyncJoin.Value && (!raw.IsEnded && raw.IsLaunched))
                        {
                            game.Status.Add(GAMELIST_TERMS.STATUS_LOCKED, true);
                        }
                        else
                        {
                            game.Status.Add(GAMELIST_TERMS.STATUS_LOCKED, raw.isLocked);
                        }
                        game.Status.Add(GAMELIST_TERMS.STATUS_PASSWORD, raw.IsPassworded);
                        game.Status.Add(GAMELIST_TERMS.STATUS_STATE, Enum.GetName(typeof(ESessionState), raw.IsEnded ? ESessionState.PostGame : raw.IsLaunched ? ESessionState.InGame : ESessionState.PreGame));

                        foreach (var dr in raw.users.Values)
                        {
                            PlayerItem player = new PlayerItem();

                            player.Name = dr.name;
                            player.Type = GAMELIST_TERMS.PLAYERTYPE_PLAYER;
                            if (admin) player.Attributes.Add("wanAddress", dr.wanAddress);
                            player.Attributes.Add("Launched", dr.Launched);
                            if (admin) player.Attributes.Add("lanAddresses", JArray.FromObject(dr.lanAddresses));
                            player.Attributes.Add("isAuth", dr.isAuth);

                            if (dr.Team.HasValue)
                            {
                                player.Team = new PlayerTeam();
                                player.Team.ID = dr.Team.Value.ToString();
                                player.GetIDData("Slot").Add("ID", dr.Team);
                            }

                            //player.Attributes.Add("Vehicle", dr.Vehicle);
                            if (dr.Vehicle != null)
                            {
                                player.Hero = new PlayerHero();
                                player.Hero.ID = (raw.WorkshopID ?? @"0") + @":" + dr.Vehicle.ToLowerInvariant();
                                player.Hero.Attributes["ODF"] = dr.Vehicle;
                            }

                            if (!string.IsNullOrWhiteSpace(dr.id))
                            {
                                player.GetIDData("BZRNet").Add("ID", dr.id);
                                if (dr.id == raw.owner)
                                    player.Attributes.Add("IsOwner", true);
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
                                            player.GetIDData("Gog").Add("Raw", dr.id.Substring(1));
                                            try
                                            {
                                                ulong playerID = 0;
                                                if (ulong.TryParse(dr.id.Substring(1), out playerID))
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
                                                            DataCache.AddObjectPath($"Players:IDs:Gog:{playerID.ToString()}:UserName", playerData.username);
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

                        if (!string.IsNullOrWhiteSpace(raw.clientVersion))
                            game.Game["Version"] = raw.clientVersion;
                        else if (!string.IsNullOrWhiteSpace(raw.GameVersion))
                            game.Game["Version"] = raw.GameVersion;

                        if (raw.SyncJoin.HasValue)
                            game.Attributes.Add("SyncJoin", raw.SyncJoin.Value);
                        if (raw.MetaDataVersion.HasValue)
                            game.Attributes.Add("MetaDataVersion", raw.MetaDataVersion);

                        MapData mapData = null;
                        if (mapDataTask != null)
                            mapData = await mapDataTask;
                        if (mapData != null)
                        {
                            game.Level["Image"] = $"{mapUrl.TrimEnd('/')}/{mapData.image ?? "nomap.png"}";
                            game.Level["Name"] = mapData?.map?.title;
                            game.Level["Type"] = mapData?.map?.type;
                            game.Level["TypeCustom"] = mapData?.map?.custom_type;
                            game.Level["AllowedHeroes"] = new JArray(mapData.map.vehicles.Select(dr => $"{dr}").ToArray());
                            foreach (var vehicle in mapData.vehicles)
                            {
                                await HeroesLock.WaitAsync();
                                try
                                {
                                    if (!Heroes.ContainsPath($"{vehicle.Key.Replace(":", "\\:")}"))
                                    {
                                        Heroes.AddObjectPath($"{vehicle.Key.Replace(":", "\\:")}:Name", vehicle.Value.name);
                                        if (vehicle.Value.description != null)
                                        {
                                            if (vehicle.Value.description.ContainsKey("en"))
                                            {
                                                Heroes.AddObjectPath($"{vehicle.Key.Replace(":", "\\:")}:Description", vehicle.Value.description["en"].content);
                                            }
                                            else if (vehicle.Value.description.ContainsKey("default"))
                                            {
                                                Heroes.AddObjectPath($"{vehicle.Key.Replace(":", "\\:")}:Description", vehicle.Value.description["default"].content);
                                            }
                                        }
                                    }
                                }
                                finally
                                {
                                    HeroesLock.Release();
                                }
                            }

                            foreach (var player in game.Players)
                            {
                                if (player.Hero != null)
                                {
                                    string ODF = player.Hero.Attributes["ODF"]?.Value<string>();
                                    if (!string.IsNullOrWhiteSpace(ODF))
                                    {
                                        string ProperHeroID = mapData.map?.vehicles?.Where(heroData => heroData.EndsWith($":{ODF}")).FirstOrDefault();
                                        if (!string.IsNullOrWhiteSpace(ProperHeroID))
                                        {
                                            player.Hero.ID = ProperHeroID;
                                        }
                                        else
                                        {
                                            ProperHeroID = Heroes.Where(heroData => heroData.Key.EndsWith($":{ODF}")).Select(dr => dr.Key).FirstOrDefault();
                                            if (!string.IsNullOrWhiteSpace(ProperHeroID))
                                            {
                                                player.Hero.ID = ProperHeroID;
                                            }
                                        }
                                    }
                                }
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
                    SessionDefault = DefaultSession,
                    DataCache = DataCache,
                    Sessions = Sessions,
                    Heroes = Heroes,
                    Raw = admin ? res : null,
                };
            }
        }
    }
}
