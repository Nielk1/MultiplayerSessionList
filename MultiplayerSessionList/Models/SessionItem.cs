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


        public List<PlayerTypeData> PlayerTypes { get; set; }
        public bool ShouldSerializePlayerTypes() { return PlayerTypes.Count > 0; }


        public Dictionary<string, int?> PlayerCount { get; set; }
        public bool ShouldSerializePlayerCount() { return PlayerCount.Count > 0; }


        //public Dictionary<string, JToken> Level { get; set; }
        //public bool ShouldSerializeLevel() { return Level.Count > 0; }
        public LevelData Level { get; set; }


        public Dictionary<string, JToken> Status { get; set; }
        public bool ShouldSerializeStatus() { return Status.Count > 0; }


        public List<PlayerItem> Players { get; set; }
        public bool ShouldSerializePlayers() { return Players.Count > 0; }


        public Dictionary<string, JToken> Attributes { get; set; }
        public bool ShouldSerializeAttributes() { return Attributes.Count > 0; }


        public Dictionary<string, JToken> Address { get; set; }
        public bool ShouldSerializeAddress() { return Address.Count > 0; }


        public SessionItem()
        {
            //Level = new Dictionary<string, JToken>();
            Status = new Dictionary<string, JToken>();
            PlayerTypes = new List<PlayerTypeData>();
            PlayerCount = new Dictionary<string, int?>();
            Players = new List<PlayerItem> ();
            Attributes = new Dictionary<string, JToken>();
            Address = new Dictionary<string, JToken>();
        }
    }

    public class PlayerTypeData
    {
        public List<string> Types { get; set; }
        public int? Max { get; set; }

        public PlayerTypeData()
        {
            Types = new List<string>();
        }
    }

    public class PlayerItem
    {
        public string Name { get; set; }


        public Dictionary<string, JToken> Stats { get; set; }
        public bool ShouldSerializeStats() { return Stats.Count > 0; }


        public PlayerTeam Team { get; set; }
        public PlayerHero Hero { get; set; }


        public Dictionary<string, JToken> Attributes { get; set; }
        public bool ShouldSerializeAttributes() { return Attributes.Count > 0; }


        public Dictionary<string, Dictionary<string, JToken>> IDs { get; set; }
        public bool ShouldSerializeIDs() { return IDs.Count > 0; }


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

    public class PlayerHero
    {
        public string ID { get; set; }

        public Dictionary<string, JToken> Attributes { get; set; }
        public bool ShouldSerializeAttributes() { return Attributes.Count > 0; }

        public PlayerHero()
        {
            Attributes = new Dictionary<string, JToken>();
        }
    }

    public class LevelData
    {
        public string MapID { get; set; }
        public string MapFile { get; set; }
        public string GameMode { get; set; }


        public Dictionary<string, JToken> Attributes { get; set; }
        public bool ShouldSerializeAttributes() { return Attributes.Count > 0; }


        public LevelData()
        {
            Attributes = new Dictionary<string, JToken>();
        }
    }

    public class DataCache : Dictionary<string, JObject>
    {
        public void AddObject(string Path, JToken Value)
        {
            string[] PathParts = Path.Split(':');
            if (!this.ContainsKey(PathParts[0]))
                this[PathParts[0]] = new JObject();

            JObject mrk = this[PathParts[0]];
            for (int i = 1; i < PathParts.Length - 1; i++)
            {
                if (!mrk.ContainsKey(PathParts[i]))
                    mrk[PathParts[i]] = new JObject();
                mrk = mrk[PathParts[i]] as JObject;
            }
            mrk[PathParts.Last()] = Value;
        }
        public bool ContainsPath(string Path)
        {
            string[] PathParts = Path.Split(':');
            if (!this.ContainsKey(PathParts[0]))
                return false;

            JObject mrk = this[PathParts[0]];
            for (int i = 1; i < PathParts.Length; i++)
            {
                if (!mrk.ContainsKey(PathParts[i]))
                    return false;
                mrk = mrk[PathParts[i]] as JObject;
            }
            return true;
        }
    }
}
