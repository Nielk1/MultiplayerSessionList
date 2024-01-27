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

namespace MultiplayerSessionList.Plugins.Steam
{
    public class GameListModule : IGameListModule
    {
        public virtual string GameID => "steam";

        public virtual string Title => "Steam Raw";
        protected virtual string Filter => null;

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
            DefaultSession.Time.AddObjectPath("Resolution", 1);

            List<SessionItem> Sessions = new List<SessionItem>();

            foreach (GetServerResponse raw in games)
            {
                SessionItem game = BuildSessionObject(raw);
                if (game != null)
                    Sessions.Add(game);
            }

            return new GameListData()
            {
                SessionDefault = DefaultSession,
                Sessions = Sessions,
                //Raw = admin ? res : null,
            };
        }

        protected virtual SessionItem BuildSessionObject(GetServerResponse raw)
        {
            SessionItem game = new SessionItem();

            game.Name = raw.Name;

            game.Type = raw.Dedicated ? GAMELIST_TERMS.TYPE_DEDICATED : GAMELIST_TERMS.TYPE_LISTEN;


            // serverid $"{raw.Address}:{raw.GamePort}";

            game.Address["IP"] = raw.Address.Split(':')[0];
            game.Address["Port"] = raw.Address.Split(':')[1];
            game.Address["GamePort"] = raw.GamePort;
            game.Address["Region"] = raw.Region.ToString();

            game.Attributes["AppID"] = raw.AppId;



            game.Level.AddObjectPath("AppId", raw.AppId);
            game.Level.AddObjectPath("Product", raw.Product);
            game.Level.AddObjectPath("GameDir", raw.GameDir);
            game.Level.AddObjectPath("Map", raw.Map);
            game.Level.AddObjectPath("GameType", raw.GameType);

            game.Level["ID"] = $"{raw.AppId}:{raw.Product}:{raw.GameDir}:{raw.Map}";

            game.Attributes["SteamId"] = raw.SteamId;
            game.Attributes["Version"] = raw.Version;
            game.Attributes["Secure"] = raw.Secure;
            game.Attributes["OS"] = raw.OS;

            game.PlayerTypes.Add(new PlayerTypeData()
            {
                Types = new List<string>() { GAMELIST_TERMS.PLAYERTYPE_PLAYER },
                Max = (int)raw.MaxPlayers
            });
            game.PlayerTypes.Add(new PlayerTypeData()
            {
                Types = new List<string>() { GAMELIST_TERMS.PLAYERTYPE_BOT }
            });

            game.PlayerCount.Add(GAMELIST_TERMS.PLAYERTYPE_PLAYER, (int)raw.Players);
            game.PlayerCount.Add(GAMELIST_TERMS.PLAYERTYPE_BOT, (int)raw.Bots);

            return game;
        }
    }
}
