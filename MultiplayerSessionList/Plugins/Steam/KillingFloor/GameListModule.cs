using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Services;

namespace MultiplayerSessionList.Plugins.Steam.KillingFloor
{
    public class GameListModule : Steam.GameListModule
    {
        public override string GameID => "steam:killingfloor";

        public override string Title => "Killing Floor";
        protected override string Filter => @"\appid\1250";

        public GameListModule(IConfiguration configuration, SteamInterface steamInterface) : base(configuration, steamInterface)
        {

        }
    }
}
