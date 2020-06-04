using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Models
{
    public class SessionItem
    {
        public string Type { get; set; }

        public string Name { get; set; }
        public string Message { get; set; }


        public int? PlayerCount { get; set; }

        public int? PlayerMax { get; set; }

        /// <summary>
        /// Are spectators possible
        /// </summary>
        public bool? SpectatorPossible { get; set; }

        /// <summary>
        /// Spectators have a seperate max count
        /// </summary>
        public bool? SpectatorSeperate { get; set; }

        public int? SpectatorCount { get; set; }

        public int? SpectatorsMax { get; set; }

        public Dictionary<string, JToken> Level { get; set; }
        public bool ShouldSerializeLevel() { return Level.Count > 0; }

        public Dictionary<string, JToken> Status { get; set; }
        public bool ShouldSerializeStatus() { return Status.Count > 0; }

        public List<PlayerItem> Players { get; set; }
        public bool ShouldSerializePlayers() { return Players.Count > 0; }

        public Dictionary<string, JToken> Attributes { get; set; }
        public bool ShouldSerializeAttributes() { return Attributes.Count > 0; }


        public SessionItem()
        {
            Level = new Dictionary<string, JToken>();
            Status = new Dictionary<string, JToken>();
            Players = new List<PlayerItem> ();
            Attributes = new Dictionary<string, JToken>();
        }
    }

    public class PlayerItem
    {
        public string Name { get; set; }
        public Dictionary<string, JToken> Stats { get; set; }
        public bool ShouldSerializeStats()
        {
            return Stats.Count > 0;
        }

        public PlayerTeam Team { get; set; }

        public Dictionary<string, JToken> Attributes { get; set; }
        public bool ShouldSerializeAttributes()
        {
            return Attributes.Count > 0;
        }

        public Dictionary<string, Dictionary<string, JToken>> IDs { get; set; }
        public bool ShouldSerializeIDs()
        {
            return IDs.Count > 0;
        }

        public PlayerItem()
        {
            Stats = new Dictionary<string, JToken>();
            Attributes = new Dictionary<string, JToken>();
            IDs = new Dictionary<string, Dictionary<string, JToken>>();
        }

        public Dictionary<string, JToken> GetIDData(string key)
        {
            if (!IDs.ContainsKey(key))
                IDs[key] = new Dictionary<string, JToken>();
            return IDs[key];
        }
    }

    public class PlayerTeam
    {
        public int? ID { get; set; }
        public bool? Leader { get; set; }
        public PlayerTeam SubTeam { get; set; }
    }
}
