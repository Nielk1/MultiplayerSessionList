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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.RetroArchNetplay
{
    [GameListModule(GameID, "RetroArch Netplay", true)]
    public class GameListModule : IGameListModule
    {
        public const string GameID = "retroarch:netplay";


        private string queryUrl;
        private CachedAdvancedWebClient cachedAdvancedWebClient;

        public GameListModule(IConfiguration configuration, CachedAdvancedWebClient cachedAdvancedWebClient)
        {
            queryUrl = configuration[$"{GameID}"];

            //IConfigurationSection myArraySection = configuration.GetSection("MyArray");
            //var itemArray = myArraySection.AsEnumerable();

            //"Clients": [ {..}, {..} ]
            //configuration.GetSection("Clients").GetChildren();
            
            //"Clients": [ "", "", "" ]
            //configuration.GetSection("Clients").GetChildren().ToArray().Select(c => c.Value).ToArray();

            this.cachedAdvancedWebClient = cachedAdvancedWebClient;
        }

        public async IAsyncEnumerable<Datum> GetGameListChunksAsync(bool multiGame, bool admin, bool mock, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var res = await cachedAdvancedWebClient.GetObject<string>(queryUrl, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
            var gamelist = JsonConvert.DeserializeObject<List<SessionWrapper>>(res.Data);

            if (!multiGame)
            {
                DataCache defTmp = new DataCache() {
                    { GAMELIST_TERMS.SESSION_TYPE, GAMELIST_TERMS.SESSION_TYPE_VALUE_LISTEN },
                };
                defTmp.AddObjectPath($"{GAMELIST_TERMS.SESSION_TIME}:{GAMELIST_TERMS.SESSION_TIME_RESOLUTION}", 1);
                yield return new Datum(GAMELIST_TERMS.TYPE_DEFAULT, GAMELIST_TERMS.TYPE_SESSION, defTmp);
            }

            //if (admin)
            //    yield return new Datum("debug", "raw", new DataCache() { { "raw", res.Data } }); // data is too big somehow, possibly for the NDJson handler

            foreach (SessionWrapper raw in gamelist)
            {
                Session s = raw.fields;

                Datum session = new Datum(GAMELIST_TERMS.TYPE_SESSION, $"{(multiGame ? $"{GameID}:" : string.Empty)}libretro:{s.RoomID}");

                if (multiGame)
                {
                    session[GAMELIST_TERMS.SESSION_TYPE] = GAMELIST_TERMS.SESSION_TYPE_VALUE_LISTEN;
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TIME}:{GAMELIST_TERMS.SESSION_TIME_RESOLUTION}", 1);
                }

                session[GAMELIST_TERMS.SESSION_NAME] = $"{s.Username} - {s.GameName}";

                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_OTHER}:IP", s.IP);
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_OTHER}:Port", s.Port);
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_OTHER}:HostMethod", s.HostMethod.ToString());
                if (s.HostMethod == HostMethod.HostMethodMITM)
                {
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_OTHER}:MitmAddress", s.MitmAddress);
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_OTHER}:MitmPort", s.MitmPort);
                }
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_ADDRESS}:{GAMELIST_TERMS.SESSION_ADDRESS_OTHER}:Country", s.Country);


                Datum mapDatum = new Datum(GAMELIST_TERMS.TYPE_MAP, $"{(multiGame ? $"{GameID}:" : string.Empty)}{s.GameCRC}:{s.GameName}", new DataCache() {
                    { GAMELIST_TERMS.MAP_NAME, s.GameName }, // consider a DB lookup or something
                    { GAMELIST_TERMS.MAP_MAPFILE, s.GameName }, // no file extension
                });
                mapDatum.AddObjectPath($"{GAMELIST_TERMS.MAP_OTHER}:GameCRC", s.GameCRC);
                yield return mapDatum;

                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_MAP}", new DatumRef(GAMELIST_TERMS.TYPE_MAP, $"{(multiGame ? $"{GameID}:" : string.Empty)}{s.GameCRC}:{s.GameName}"));

                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_OTHER}:ID", $"{s.GameCRC}:{s.GameName}");

                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_PASSWORD}", s.HasPassword);
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_STATUS}:{GAMELIST_TERMS.SESSION_STATUS_PASSWORD}.{GAMELIST_TERMS.PLAYERTYPE_TYPES_VALUE_SPECTATOR}", s.HasSpectatePassword);

                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_GAME}:{GAMELIST_TERMS.SESSION_GAME_VERSION}", s.RetroArchVersion);

                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_OTHER}:Core:Name", s.CoreName);
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_OTHER}:Core:Version", s.CoreVersion);
                if (s.SubsystemName != "N/A")
                    session.AddObjectPath($"{GAMELIST_TERMS.SESSION_LEVEL}:{GAMELIST_TERMS.SESSION_LEVEL_OTHER}:Core:SubsystemName", s.SubsystemName);

                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:UnsortedAttributes:Username", s.Username);
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:UnsortedAttributes:RoomID", s.RoomID);
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:UnsortedAttributes:Frontend", s.Frontend);
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:UnsortedAttributes:CreatedAt", s.CreatedAt);
                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_OTHER}:UnsortedAttributes:UpdatedAt", s.UpdatedAt);

                session.AddObjectPath($"{GAMELIST_TERMS.SESSION_TIME}:{GAMELIST_TERMS.SESSION_TIME_SECONDS}", (DateTime.UtcNow - s.CreatedAt).TotalSeconds);

                yield return session;
            }
        }
    }
}
