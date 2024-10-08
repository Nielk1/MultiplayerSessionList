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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.RetroArchNetplay
{
    public class GameListModule : IGameListModuleOld
    {
        public string GameID => "retroarch:netplay";
        public string Title => "RetroArch Netplay";
        public bool IsPublic => true;


        private string queryUrl;

        public GameListModule(IConfiguration configuration)
        {
            queryUrl = configuration["retroarch:netplay"];

            //IConfigurationSection myArraySection = configuration.GetSection("MyArray");
            //var itemArray = myArraySection.AsEnumerable();

            //"Clients": [ {..}, {..} ]
            //configuration.GetSection("Clients").GetChildren();
            
            //"Clients": [ "", "", "" ]
            //configuration.GetSection("Clients").GetChildren().ToArray().Select(c => c.Value).ToArray();
        }

        public async Task<GameListData> GetGameList(bool admin)
        {
            using (var http = new HttpClient())
            {
                var res = await http.GetStringAsync(queryUrl).ConfigureAwait(false);
                var gamelist = JsonConvert.DeserializeObject<List<SessionWrapper>>(res);

                SessionItem DefaultSession = new SessionItem();
                DefaultSession.Type = GAMELIST_TERMS_OLD.TYPE_LISTEN;
                DefaultSession.Time.AddObjectPath("Resolution", 1);

                List<SessionItem> Sessions = new List<SessionItem>();

                foreach (SessionWrapper raw in gamelist)
                {
                    Session s = raw.fields;
                    SessionItem game = new SessionItem();

                    game.ID = $"libretro:{s.RoomID}";

                    game.Name = $"{s.Username} - {s.GameName}";

                    game.Address["IP"] = s.IP;
                    game.Address["Port"] = s.Port;
                    game.Address["HostMethod"] = s.HostMethod.ToString();
                    if (s.HostMethod == HostMethod.HostMethodMITM)
                    {
                        game.Address["MitmAddress"] = s.MitmAddress;
                        game.Address["MitmPort"] = s.MitmPort;
                    }
                    game.Address["Country"] = s.Country;

                    game.Level.AddObjectPath("GameName", s.GameName);
                    game.Level.AddObjectPath("GameCRC", s.GameCRC);

                    //game.Level.ID = MapID.ToString(Formatting.None);
                    game.Level["ID"] = $"{s.GameCRC}:{s.GameName}";

                    game.Status[GAMELIST_TERMS_OLD.STATUS_PASSWORD] = s.HasPassword;
                    game.Status[$"{GAMELIST_TERMS_OLD.STATUS_PASSWORD}.{GAMELIST_TERMS_OLD.PLAYERTYPE_SPECTATOR}"] = s.HasSpectatePassword;

                    game.Game.AddObjectPath("Attributes:Core:Name", s.CoreName);
                    game.Game.AddObjectPath("Attributes:Core:Version", s.CoreVersion);

                    if (s.SubsystemName != "N/A")
                        game.Game.AddObjectPath("Attributes:Core:SubsystemName", s.SubsystemName);

                    game.Game.AddObjectPath("Attributes:RetroArchVersion", s.RetroArchVersion);

                    game.Attributes["Username"] = s.Username;
                    game.Attributes["RoomID"] = s.RoomID;
                    game.Attributes["Frontend"] = s.Frontend;
                    game.Attributes["CreatedAt"] = s.CreatedAt;
                    game.Attributes["UpdatedAt"] = s.UpdatedAt;

                    game.Time.AddObjectPath("Seconds", (DateTime.UtcNow - s.CreatedAt).TotalSeconds);

                    Sessions.Add(game);
                }

                return new GameListData()
                {
                    SessionsDefault = DefaultSession,
                    Sessions = Sessions,
                    Raw = admin ? res : null,
                };
            }
        }

        public async IAsyncEnumerable<Datum> GetGameListChunksAsync(bool admin, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }
    }
}
