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
        public string InternalID { get { return ExtractGameSettings(2); } }

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
}
