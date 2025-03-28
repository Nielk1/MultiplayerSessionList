using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.DotHackFragment
{
	public class LobbyServerData
	{
		[JsonProperty("playerList")]
		public Player[] PlayerList { get; set; }

		[JsonProperty("areaServerList")]
        public AreaServer[] AreaServerList { get; set; }
    }

    public class Player
    {
        [JsonProperty("saveSlot")]
        public byte saveSlot { get; set; }

        [JsonProperty("saveId")]
        public string saveId{ get; set; }

        [JsonProperty("charId")]
        public string charId { get; set; }

        [JsonProperty("charName")]
        public string charName { get; set; }

        [JsonProperty("charClass")]
        public string charClass { get; set; }

        [JsonProperty("charLevel")]
        public byte charLevel { get; set; }

        [JsonProperty("greeting")]
        public string greeting { get; set; }

        [JsonProperty("charModel")]
        public string charModel { get; set; }

        [JsonProperty("charHp")]
        public Int64 charHp { get; set; }

        [JsonProperty("charSp")]
        public Int64 charSp { get; set; }

        [JsonProperty("charGp")]
        public Int64 charGp { get; set; }

        [JsonProperty("onlineGodCounter")]
        public Int64 onlineGodCounter { get; set; }

        [JsonProperty("offlineGodCounter")]
        public Int64 offlineGodCounter { get; set; }

        [JsonProperty("charModelFile")]
        public string charModelFile { get; set; }
    }

	public class AreaServer
    {
        [JsonProperty("serverName")]
        public string Name { get; set; }

        [JsonProperty("serverLevel")]
        public byte Level { get; set; }

        [JsonProperty("serverStatus")]
        public string Status { get; set; }

        [JsonProperty("numberOfPlayers")]
        public byte NumberOfPlayers { get; set; }
    }
}
