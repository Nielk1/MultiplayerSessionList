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

namespace MultiplayerSessionList.Plugins.Steam.TheShip
{
    public class GameListModule : Steam.GameListModule
    {
        public override string GameID => "steam:theship";

        public override string Title => "The Ship";
        protected override string Filter => @"\appid\2400";
        //protected override string Filter => @"\appid\383790";
        protected override bool DoQueryInfo => true;
        protected override bool DoQueryPlayers => true;
        protected override bool DoQueryRules => true;

        public GameListModule(IConfiguration configuration, SteamInterface steamInterface) : base(configuration, steamInterface)
        {

        }
    }
}
