using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Services;
using Newtonsoft.Json.Linq;
using Okolni.Source.Query;
using Okolni.Source.Query.Responses;
using SteamWebAPI2.Models.GameServers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.Steam.KillingFloor
{
    public class GameListModule : Steam.GameListModule
    {
        public override string GameID => "steam:killingfloor";

        public override string Title => "Killing Floor";
        protected override string Filter => @"\appid\1250";
        protected override bool DoQueryInfo => true;
        protected override bool DoQueryPlayers => true;

        public GameListModule(IConfiguration configuration, SteamInterface steamInterface) : base(configuration, steamInterface)
        {

        }
        protected override async Task BuildGameDataAsync(DataCacheOld DataCache, SemaphoreSlim DataCacheLock, SessionItem game, InfoResponse QInfo)
        {
            if (QInfo == null)
                return;

            await base.BuildGameDataAsync(DataCache, DataCacheLock, game, QInfo);

            string[] KeyWordSplit = QInfo.KeyWords.Split(';');

            if (KeyWordSplit.Length > 0)
            {
                switch (KeyWordSplit[0])
                {
                    case "d": game.Type = GAMELIST_TERMS_OLD.TYPE_DEDICATED; break;
                    case "l": game.Type = GAMELIST_TERMS_OLD.TYPE_LISTEN; break;
                }
            }

            //if (KeyWordSplit.Length > 1)
            //{
            //    switch (KeyWordSplit[1])
            //    {
            //        //case "0": break;
            //        case "1": game.Level.AddObjectPath("Attributes:KFGameLength", "Long"); break;
            //        default:
            //            break;
            //    }
            //}

            if (KeyWordSplit.Length > 2)
            {
                switch (KeyWordSplit[2])
                {
                    case "0": game.Level.AddObjectPath("Attributes:GameDifficulty", "Beginner"); break;
                    case "1": game.Level.AddObjectPath("Attributes:GameDifficulty", "Normal"); break;
                    case "2": game.Level.AddObjectPath("Attributes:GameDifficulty", "Hard"); break;
                    case "3": game.Level.AddObjectPath("Attributes:GameDifficulty", "Suicidal"); break;
                    case "4": game.Level.AddObjectPath("Attributes:GameDifficulty", "Hell on Earth"); break;
                    default:
                        break;
                }
            }

            if (!string.IsNullOrEmpty(QInfo.Game))
            {
                game.Level.AddObjectPath("GameType:ID", QInfo.Game);
                game.Level.AddObjectPath("GameMode:ID", QInfo.Game);
                await DataCacheLock.WaitAsync();
                try
                {
                    if (!DataCache.ContainsPath($"Level:GameType:{QInfo.Game}"))
                        DataCache.AddObjectPath($"Level:GameType:{QInfo.Game}:Name", QInfo.Game);
                    if (!DataCache.ContainsPath($"Level:GameMode:{QInfo.Game}"))
                        DataCache.AddObjectPath($"Level:GameMode:{QInfo.Game}:Name", QInfo.Game);
                }
                finally
                {
                    DataCacheLock.Release();
                }
            }
        }
    }
}
