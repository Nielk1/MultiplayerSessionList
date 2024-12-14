using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using MultiplayerSessionList.Services;
using static MultiplayerSessionList.Services.SteamInterface;

namespace MultiplayerSessionList.Models
{
    public class PendingDatum
    {
        public Datum data { get; set; }
        public string key { get; set; }
        public bool stub { get; set; }

        public PendingDatum(Datum data, string key, bool isStub)
        {
            this.data = data;
            this.key = key;
            this.stub = isStub;
        }
    }
    public class DatumRef
    {
        [JsonProperty("$ref")]
        public string Ref { get; set; }
        public DatumRef(string type, string id)
        {
            Ref = $"#/{type.Replace("~", "~0").Replace("/", "~1")}/{id.Replace("~", "~0").Replace("/", "~1")}";
        }
    }
    public class Datum
    {
        [JsonProperty("$type")]
        public string Type { get; set; }
        [JsonProperty("$id")]
        public string ID { get; set; }

        public dynamic this[string key]
        {
            get
            {
                //if (!Data.ContainsKey(key))
                //    Data[key] = new DataCache2();
                return Data[key];
            }
            set
            {
                Data[key] = value;
            }
        }

        [JsonProperty("$data")]
        //public dynamic Data { get; set; }
        public DataCache Data { get; set; }
        //public Datum(string type, string id, dynamic data)
        public Datum(string type, string id, bool bare = false) : this(type, id, new DataCache()) { }//, bare) { }
        public Datum(string type, string id, DataCache data)//, bool bare = false)
        {
            Type = type;
            ID = id;
            Data = data;

            // A bare item doesn't reflect its own identity into its data, useful for templates that get applied to data such as defaults
            //if (!bare)
            //{
            //    data["$type"] = type;
            //    data["$id"] = ID;
            //}
        }

        public void AddObjectPath(string Path, dynamic Value) => Data.AddObjectPath(Path, Value);
        public bool ContainsPath(string Path) => Data.ContainsPath(Path);
    }

    public class DataCache : Dictionary<string, dynamic>
    {
        static Regex KeySplit = new Regex("(?<!\\\\):");

        public void AddObjectPath(string Path, dynamic Value)
        {
            //string[] PathParts = Path.Split(':');
            string[] PathParts = KeySplit.Split(Path).Select(dr => dr.Replace("\\:", ":")).ToArray();

            if (PathParts.Length == 1)
            {
                this[PathParts[0]] = Value;
                return;
            }

            if (!this.ContainsKey(PathParts[0]))
                this[PathParts[0]] = new DataCache();

            DataCache mrk = this[PathParts[0]] as DataCache;
            for (int i = 1; i < PathParts.Length - 1; i++)
            {
                if (!mrk.ContainsKey(PathParts[i]))
                    mrk[PathParts[i]] = new DataCache();
                mrk = mrk[PathParts[i]] as DataCache;
            }
            mrk[PathParts.Last()] = Value;
        }
        public bool ContainsPath(string Path)
        {
            //string[] PathParts = Path.Split(':');
            string[] PathParts = KeySplit.Split(Path).Select(dr => dr.Replace("\\:", ":")).ToArray();
            if (!this.ContainsKey(PathParts[0]))
                return false;

            DataCache mrk = this[PathParts[0]] as DataCache;
            for (int i = 1; i < PathParts.Length; i++)
            {
                if (!mrk.ContainsKey(PathParts[i]))
                    return false;
                mrk = mrk[PathParts[i]] as DataCache;
            }
            return true;
        }
    }

    public static class Extensions
    {
        public static async Task<List<PendingDatum>> GetPendingDataAsync(this SteamInterface steamInterface, ulong playerID)
        {
            SteamInterface.WrappedPlayerSummaryModel playerData = await steamInterface.Users(playerID);
            if (playerData == null)
                // TODO consider finding a way to cache total failures like this, maybe with a counter before they get cached, or make the cache longer and longer
                return null;
            Datum accountDataSteam = new Datum("identity/steam", playerID.ToString(), new DataCache()
            {
                { "type", "steam" },
            });
            if (!string.IsNullOrEmpty(playerData.Model.AvatarFullUrl)) accountDataSteam["avatar_url" ] = playerData.Model.AvatarFullUrl;
            if (!string.IsNullOrEmpty(playerData.Model.Nickname))      accountDataSteam["nickname"   ] = playerData.Model.Nickname;
            if (!string.IsNullOrEmpty(playerData.Model.ProfileUrl))    accountDataSteam["profile_url"] = playerData.Model.ProfileUrl;
            if (!string.IsNullOrEmpty(playerData.Model.ProfileUrl))    accountDataSteam["profile_url"] = playerData.Model.ProfileUrl;
            if (playerData.IsPirate) accountDataSteam["is_pirate"] = true; // asshole

            return new List<PendingDatum>() { new PendingDatum(accountDataSteam, $"identity/steam/{playerID.ToString()}", false) };
        }
        public static async Task<List<PendingDatum>> GetPendingDataAsync(this GogInterface gogInterface, ulong playerID)
        {
            GogInterface.GogUserData playerData = await gogInterface.Users(playerID);
            Datum accountDataGog = new Datum("identity/gog", playerID.ToString(), new DataCache()
            {
                { "type", "gog" },
                { "avatar_url", playerData.Avatar.sdk_img_184 ?? playerData.Avatar.large_2x ?? playerData.Avatar.large },
                { "username", playerData.username },
                { "profile_url", $"https://www.gog.com/u/{playerData.username}" },
            });
            return new List<PendingDatum> () { new PendingDatum(accountDataGog, $"identity/gog/{playerID.ToString()}", false) };
        }
    }
}
