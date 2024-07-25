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

namespace MultiplayerSessionList.Plugins.Steam.GarrysMod
{
    public class GameListModule : Steam.GameListModule
    {
        public override string GameID => "steam:garrysmod";

        public override string Title => "Garry's Mod";
        protected override string Filter => @"\appid\4000";
        protected override bool DoQueryInfo => true;
        protected override bool DoQueryPlayers => true;
        protected override bool DoQueryRules => true;

        public GameListModule(IConfiguration configuration, SteamInterface steamInterface) : base(configuration, steamInterface)
        {

        }
    }
}
