using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace MultiplayerSessionList.Models
{
    public class SessionItem
    {
        public string ID { get; set; }

        public string Type { get; set; }

        public string Name { get; set; }
        public string Message { get; set; }


        public List<PlayerTypeData> PlayerTypes { get; set; }
        public bool ShouldSerializePlayerTypes() { return PlayerTypes.Count > 0; }


        public Dictionary<string, int?> PlayerCount { get; set; }
        public bool ShouldSerializePlayerCount() { return PlayerCount.Count > 0; }


        public DataCache Level { get; set; }
        public bool ShouldSerializeLevel() { return Level.Count > 0; }


        public DataCache Status { get; set; }
        public bool ShouldSerializeStatus() { return Status.Count > 0; }


        public List<PlayerItem> Players { get; set; }
        public bool ShouldSerializePlayers() { return Players.Count > 0; }


        public DataCache Attributes { get; set; }
        public bool ShouldSerializeAttributes() { return Attributes.Count > 0; }


        public DataCache Address { get; set; }
        public bool ShouldSerializeAddress() { return Address.Count > 0; }


        public DataCache Game { get; set; }
        public bool ShouldSerializeGame() { return Game.Count > 0; }


        public DataCache Time { get; set; }
        public bool ShouldSerializeTime() { return Time.Count > 0; }


        public DataCache Teams { get; set; }
        public bool ShouldSerializeTeams() { return Teams.Count > 0; }


        public SessionItem()
        {
            Level = new DataCache();
            Status = new DataCache();
            PlayerTypes = new List<PlayerTypeData>();
            PlayerCount = new Dictionary<string, int?>();
            Players = new List<PlayerItem> ();
            Attributes = new DataCache();
            Address = new DataCache();
            Game = new DataCache();
            Time = new DataCache();
            Teams = new DataCache();
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
        public string Type { get; set; }


        public DataCache Stats { get; set; }
        public bool ShouldSerializeStats() { return Stats.Count > 0; }


        public PlayerTeam Team { get; set; }
        public PlayerHero Hero { get; set; }


        public DataCache Attributes { get; set; }
        public bool ShouldSerializeAttributes() { return Attributes.Count > 0; }


        public Dictionary<string, DataCache> IDs { get; set; }

        public bool ShouldSerializeIDs() { return IDs.Count > 0; }


        public PlayerItem()
        {
            Stats = new DataCache();
            Attributes = new DataCache();
            IDs = new Dictionary<string, DataCache>();
        }

        public Dictionary<string, JToken> GetIDData(string key)
        {
            if (!IDs.ContainsKey(key))
                IDs[key] = new DataCache();
            return IDs[key];
        }
    }

    public class PlayerTeam
    {
        public string ID { get; set; }
        public bool? Leader { get; set; }
        public PlayerTeam SubTeam { get; set; }
    }

    public class PlayerHero
    {
        public string ID { get; set; }

        public DataCache Attributes { get; set; }
        public bool ShouldSerializeAttributes() { return Attributes.Count > 0; }

        public PlayerHero()
        {
            Attributes = new DataCache();
        }
    }

    public class DataCache : Dictionary<string, JToken>
    {
        static Regex KeySplit = new Regex("(?<!\\\\):");

        public void AddObjectPath(string Path, JToken Value)
        {
            //string[] PathParts = Path.Split(':');
            string[] PathParts = KeySplit.Split(Path).Select(dr => dr.Replace("\\:", ":")).ToArray();

            if (PathParts.Length == 1)
            {
                this[PathParts[0]] = Value;
                return;
            }

            if (!this.ContainsKey(PathParts[0]))
                this[PathParts[0]] = new JObject();

            JObject mrk = this[PathParts[0]] as JObject;
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
            //string[] PathParts = Path.Split(':');
            string[] PathParts = KeySplit.Split(Path).Select(dr => dr.Replace("\\:", ":")).ToArray();
            if (!this.ContainsKey(PathParts[0]))
                return false;

            JObject mrk = this[PathParts[0]] as JObject;
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
