using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.Battlezone98Redux
{
    public class Lobby
    {
        public int id { get; set; }
        public string owner { get; set; }
        public bool isLocked { get; set; }
        public bool isChat { get; set; }
        public bool isPrivate { get; set; }
        public string password { get; set; } // is this a real property?
        public int memberLimit { get; set; }
        public Dictionary<string, User> users { get; set; }
        public int userCount { get; set; }
        public Dictionary<string, string> metadata { get; set; }
        /////////////////////////////////////////////
        public string clientVersion { get; set; }

        [JsonIgnore]
        public string GameVersion
        {
            get
            {
                if(metadata != null && metadata.ContainsKey("GameVersion"))
                    return metadata["GameVersion"];
                return null;
            }
        }

        [JsonIgnore]
        public bool IsLaunched
        {
            get
            {
                return metadata != null && metadata.ContainsKey("launched") && metadata["launched"] == "1";
            }
        }

        [JsonIgnore]
        public bool IsEnded
        {
            get
            {
                return metadata != null && metadata.ContainsKey("gameended") && metadata["gameended"] == "1";
            }
        }

        public enum ELobbyType
        {
            Chat,
            Game,
            Unknown
        }

        public enum ELobbyVisibility
        {
            Public,
            Private,
            Unknown
        }

        /// <summary>
        /// might this reveal mobile? or is mobile only in the version?
        /// </summary>
        public enum EGameType
        {
            Unknown,
            Broken,
            Valid,
        }

        [JsonIgnore]
        public ELobbyType LobbyType
        {
            get
            {
                string name = ExtractName(1);
                if (name == null) return ELobbyType.Unknown;
                if (name == "chat") return ELobbyType.Chat;
                if (name == "game") return ELobbyType.Game;
                return ELobbyType.Unknown;
            }
        }

        [JsonIgnore]
        public ELobbyVisibility LobbyVisibility
        {
            get
            {
                string name = ExtractName(2);
                if (name == null) return ELobbyVisibility.Unknown;
                if (name == "pub") return ELobbyVisibility.Public;
                if (name == "priv") return ELobbyVisibility.Private;
                return ELobbyVisibility.Unknown;
            }
        }

        [JsonIgnore]
        public EGameType GameType
        {
            get
            {
                if (!metadata.ContainsKey("gameType"))
                    return EGameType.Unknown;
                if (metadata["gameType"] == "0") return EGameType.Broken;
                if (metadata["gameType"] == "1") return EGameType.Valid;
                return EGameType.Unknown;
            }
        }

        [JsonIgnore]
        public bool? IsPassworded
        {
            get
            {
                string passworded = ExtractName(3);
                if (passworded == null) return null;
                return passworded.Length > 0; // contains something, valid values are "" and "*"
            }
        }

        [JsonIgnore]
        public string Name { get { return ExtractName(4); } }

        /// <summary>
        /// Increments with every metadata change
        /// </summary>
        [JsonIgnore]
        public int? MetaDataVersion
        {
            get
            {
                string GameSettingVersion = ExtractGameSettings(0);
                if (string.IsNullOrWhiteSpace(GameSettingVersion)) return null;

                int versionNum = 0;
                if (int.TryParse(GameSettingVersion, out versionNum))
                    return versionNum;

                return null;
            }
        }

        [JsonIgnore]
        public string MapFile { get { return ExtractGameSettings(1); } }

        [JsonIgnore]
        public string CRC32 { get { return ExtractGameSettings(2); } }

        [JsonIgnore]
        public string WorkshopID { get { return ExtractGameSettings(3); } }

        [JsonIgnore]
        public bool? SyncJoin
        {
            get
            {
                string val = ExtractGameSettings(4);
                if (string.IsNullOrWhiteSpace(val)) return null;
                if (val == "0") return false;
                if (val == "1") return true;
                return null;
            }
        }

        [JsonIgnore]
        public bool? SatelliteEnabled
        {
            get
            {
                string val = ExtractGameSettings(5);
                if (string.IsNullOrWhiteSpace(val)) return null;
                if (val == "0") return false;
                if (val == "1") return true;
                return null;
            }
        }

        [JsonIgnore]
        public bool? BarracksEnabled
        {
            get
            {
                string val = ExtractGameSettings(6);
                if (string.IsNullOrWhiteSpace(val)) return null;
                if (val == "0") return false;
                if (val == "1") return true;
                return null;
            }
        }

        [JsonIgnore]
        public int? TimeLimit
        {
            get
            {
                string val = ExtractGameSettings(7);
                if (string.IsNullOrWhiteSpace(val)) return null;
                int tmpVal = 0;
                if (int.TryParse(val, out tmpVal)) return tmpVal;
                return null;
            }
        }

        [JsonIgnore]
        public int? Lives
        {
            get
            {
                string val = ExtractGameSettings(8);
                if (string.IsNullOrWhiteSpace(val)) return null;
                int tmpVal = 0;
                if (int.TryParse(val, out tmpVal)) return tmpVal;
                return null;
            }
        }

        [JsonIgnore]
        public int? PlayerLimit
        {
            get
            {
                string val = ExtractGameSettings(9);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    int tmpVal = 0;
                    if (int.TryParse(val, out tmpVal)) return tmpVal;
                }
                //return null;
                return memberLimit; // memberLimit as fallback as the lobby size should be the same as the player limit
            }
        }

        [JsonIgnore]
        public bool? SniperEnabled
        {
            get
            {
                string val = ExtractGameSettings(10);
                if (string.IsNullOrWhiteSpace(val)) return null;
                if (val == "0") return false;
                if (val == "1") return true;
                return null;
            }
        }

        [JsonIgnore]
        public int? KillLimit
        {
            get
            {
                string val = ExtractGameSettings(11);
                if (string.IsNullOrWhiteSpace(val)) return null;
                int tmpVal = 0;
                if (int.TryParse(val, out tmpVal)) return tmpVal;
                return null;
            }
        }

        [JsonIgnore]
        public bool? SplinterEnabled
        {
            get
            {
                string val = ExtractGameSettings(12);
                if (string.IsNullOrWhiteSpace(val)) return null;
                if (val == "0") return false;
                if (val == "1") return true;
                return null;
            }
        }

        private string ExtractName(int index)
        {
            if (!metadata.ContainsKey("name"))
                return null;

            string metaValue = metadata["name"];
            string[] metaValues = metaValue.Split(new[] { "~" }, 5, StringSplitOptions.None);

            if (metaValues.Length > index)
                return metaValues[index];

            return null;
        }

        private string ExtractGameSettings(int index)
        {
            if (!metadata.ContainsKey("gameSettings"))
                return null;

            string metaValue = metadata["gameSettings"];
            string[] metaValues = metaValue.Split(new[] { "*" }, StringSplitOptions.None);

            if (metaValues.Length > index)
                return metaValues[index];

            return null;
        }
    }

    public class User
    {
        public string id { get; set; }
        public string name { get; set; }
        public string authType { get; set; }
        public string clientVersion { get; set; }
        public string ipAddress { get; set; }
        public int lobby { get; set; }
        public bool isAdmin { get; set; }
        public bool isInLounge { get; set; }
        public Dictionary<string, string> metadata { get; set; }
        public string wanAddress;
        public string[] lanAddresses { get; set; }
        /////////////////////////////////////////////
        public bool isAuth { get; set; }

        [JsonIgnore]
        public bool Launched
        {
            get
            {
                if (!metadata.ContainsKey("launched"))
                    return false;

                if (metadata["launched"] == "0")
                    return false;
                if (metadata["launched"] == "1")
                    return true;

                return false;
            }
        }

        [JsonIgnore]
        public int? Team
        {
            get
            {
                if (!metadata.ContainsKey("team"))
                    return null;

                int team;
                if (int.TryParse(metadata["team"], out team))
                    return team;

                return null;
            }
        }

        [JsonIgnore]
        public string Vehicle
        {
            get
            {
                if (metadata.ContainsKey("vehicle"))
                    return metadata["vehicle"];
                return null;
            }
        }
    }

    public class MapData
    {
        public MapData_Map map { get; set; }
        public Dictionary<string, MapData_Vehicle> vehicles { get; set; }
        public Dictionary<string, MapData_Mods> mods { get; set; }
        public string image { get; set; }
    }

    public class MapData_Map
    {
        public string title { get; set; }
        public string type { get; set; }
        public string custom_type { get; set; }
        public int min { get; set; }
        public int max { get; set; }
        public List<string> vehicles { get; set; }
    }

    public class MapData_Vehicle
    {
        public string name { get; set; }
        public Dictionary<string, MapData_Vehicle_Description> description { get; set; }
    }
    public class MapData_Vehicle_Description
    {
        public string file { get; set; }
        public string content { get; set; }
    }
    public class MapData_Mods
    {
        public string name { get; set; }
        public string workshop_name { get; set; }
    }
}
