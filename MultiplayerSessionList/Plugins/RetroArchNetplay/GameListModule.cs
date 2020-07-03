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

namespace MultiplayerSessionList.Plugins.RetroArchNetplay
{
    public class GameListModule : IGameListModule
    {
        public string GameID => "retroarch:netplay";
        public string Title => "RetroArch Netplay";


        private string queryUrl;

        public GameListModule(IConfiguration configuration)
        {
            queryUrl = configuration["retroarch:netplay"];
        }

        public async Task<(SessionItem, DataCache, IEnumerable<SessionItem>, JToken)> GetGameList()
        {
            using (var http = new HttpClient())
            {
                var res = await http.GetStringAsync(queryUrl).ConfigureAwait(false);
                var gamelist = JsonConvert.DeserializeObject<List<SessionWrapper>>(res);

                SessionItem DefaultSession = new SessionItem()
                {
                    Type = GAMELIST_TERMS.TYPE_LISTEN,
                };

                List<SessionItem> Sessions = new List<SessionItem>();

                foreach (SessionWrapper raw in gamelist)
                {
                    Session s = raw.fields;
                    SessionItem game = new SessionItem();

                    game.Address["IP"] = s.IP.ToString();
                    game.Address["Port"] = s.Port;
                    game.Address["MitmAddress"] = s.MitmAddress;
                    game.Address["MitmPort"] = s.MitmPort;
                    game.Address["HostMethod"] = s.HostMethod.ToString();
                    game.Address["Country"] = s.Country;

                    game.Level = new LevelData();
                    game.Level.Attributes["Game"] = new JObject
                    {
                        { "Name", s.GameName },
                        { "CRC", s.GameCRC },
                    };
                    game.Level.Attributes["Core"] = new JObject
                    {
                        { "Name", s.CoreName },
                        { "Version", s.CoreVersion },
                    };
                    //JArray MapID = new JArray(s.RetroArchVersion, s.GameCRC, s.GameName, s.CoreName, s.CoreVersion);
                    if (s.SubsystemName != "N/A")
                    {
                        game.Level.Attributes["Core"]["SubsystemName"] = s.SubsystemName;
                        //MapID.Add(s.SubsystemName);
                    }
                    game.Level.Attributes["RetroArchVersion"] = s.RetroArchVersion;
                    //game.Level.MapID = MapID.ToString(Formatting.None);
                    game.Level.MapID = $"{s.GameCRC}:{s.GameName}";

                    game.Status[GAMELIST_TERMS.STATUS_PASSWORD] = s.HasPassword;
                    game.Status[$"{GAMELIST_TERMS.STATUS_PASSWORD}.{GAMELIST_TERMS.PLAYERTYPE_SPECTATOR}"] = s.HasSpectatePassword;

                    game.Attributes["RoomID"] = s.RoomID;
                    game.Attributes["Username"] = s.Username;
                    game.Attributes["Frontend"] = s.Frontend;
                    game.Attributes["CreatedAt"] = s.CreatedAt;
                    game.Attributes["UpdatedAt"] = s.UpdatedAt;

                    Sessions.Add(game);
                }

                return (DefaultSession, null, Sessions, JArray.Parse(res));
            }
        }
    }
}
