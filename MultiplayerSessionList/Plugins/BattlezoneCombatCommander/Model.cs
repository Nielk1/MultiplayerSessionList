using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.BattlezoneCombatCommander
{
    public class BZCCRaknetData
    {
        public List<BZCCGame> GET { get; set; } = null!;

        public Dictionary<string, ProxyStatus>? proxyStatus { get; set; }
    }

    public class BZCCPlayerData
    {
        public string? n { get; set; } // name (base 64)
        
        [Newtonsoft.Json.JsonProperty("i")][JsonPropertyName("i")] public string? PlayerID { get; set; } // id (player ID)
        [Newtonsoft.Json.JsonProperty("k")][JsonPropertyName("k")] public int? Kills { get; set; } // kills
        [Newtonsoft.Json.JsonProperty("d")][JsonPropertyName("d")] public int? Deaths { get; set; } // deaths
        [Newtonsoft.Json.JsonProperty("s")][JsonPropertyName("s")] public int? Score { get; set; } // score
        [Newtonsoft.Json.JsonProperty("t")][JsonPropertyName("t")] public int? Team { get; set; } // team

        [Newtonsoft.Json.JsonIgnore][JsonIgnore] public string? Name { get { return string.IsNullOrWhiteSpace(n) ? null : Encoding.GetEncoding(1252).GetString(Convert.FromBase64String(n)); } }
    }
    enum EGameMode : int
    {
        GAMEMODE_UNKNOWN,
        GAMEMODE_DM,
        GAMEMODE_TEAM_DM,
        GAMEMODE_KOTH,
        GAMEMODE_TEAM_KOTH,
        GAMEMODE_CTF,
        GAMEMODE_TEAM_CTF,
        GAMEMODE_LOOT,
        GAMEMODE_TEAM_LOOT = 8,
        GAMEMODE_RACE = 9,
        GAMEMODE_TEAM_RACE = 10,
        GAMEMODE_STRAT = 11,
        GAMEMODE_TEAM_STRAT = 12,
        GAMEMODE_MPI = 13,

        GAMEMODE_MAX // Must be last, thank you.
    };
    public enum ENatType : byte
    {
        NONE = 0, /// Works with anyone
        FULL_CONE = 1, /// Accepts any datagrams to a port that has been previously used. Will accept the first datagram from the remote peer.
        ADDRESS_RESTRICTED = 2, /// Accepts datagrams to a port as long as the datagram source IP address is a system we have already sent to. Will accept the first datagram if both systems send simultaneously. Otherwise, will accept the first datagram after we have sent one datagram.
        PORT_RESTRICTED = 3, /// Same as address-restricted cone NAT, but we had to send to both the correct remote IP address and correct remote port. The same source address and port to a different destination uses the same mapping.
        SYMMETRIC = 4, /// A different port is chosen for every remote destination. The same source address and port to a different destination uses a different mapping. Since the port will be different, the first external punchthrough attempt will fail. For this to work it requires port-prediction (MAX_PREDICTIVE_PORT_RANGE>1) and that the router chooses ports sequentially.
        UNKNOWN = 5, /// Hasn't been determined. NATTypeDetectionClient does not use this, but other plugins might
        DETECTION_IN_PROGRESS = 6, /// In progress. NATTypeDetectionClient does not use this, but other plugins might
        SUPPORTS_UPNP = 7, /// Didn't bother figuring it out, as we support UPNP, so it is equivalent to NAT_TYPE_NONE. NATTypeDetectionClient does not use this, but other plugins might
    }

    public enum EServerInfoMode
    {
         Unknown = 0,
         OpenWaiting = 1,
         ClosedWaiting = 2,
         OpenPlaying = 3,
         ClosedPlaying = 4,
         Exiting = 5,
    }

    public class BZCCGame
    {
        //public string __addr { get; set; }
        public string? proxySource { get; set; }

        [Newtonsoft.Json.JsonProperty("g")][JsonPropertyName("g")] public string NATNegID { get; set; } = null!; // Raknet GUID, Base64 encoded with custom alphabet
        [JsonIgnore] public UInt64 NATNegGuid { get { return CustomBase64.DecodeRaknetGuid(NATNegID); } set { NATNegID = CustomBase64.EncodeRaknetGuid(value); } }
        public string? n { get; set; } // varchar(256) | Name of client game session, base64 and null terminate.
        [Newtonsoft.Json.JsonProperty("m")][JsonPropertyName("m")] public string? MapFile { get; set; } // varchar(68)  | Name of client map, no bzn extension.
        public byte? k { get; set; } // tinyint      | Password Flag.
        public string? d { get; set; } // varchar(16)  | MODSLISTCRC_KEY
        [Newtonsoft.Json.JsonProperty("t")][JsonPropertyName("t")] public ENatType? NATType { get; set; } // tinyint      | NATTYPE_KEY //nat type 5 seems bad, 7 seems to mean direct connect
        public string? v { get; set; } // varchar(8)   | GAMEVERSION_KEY (nice string now)
        [JsonConverter(typeof(FaultTolerantIntConverter))] public int? l { get; set; } // locked, this was a string in error in my raknet server's fake marker game
        [Newtonsoft.Json.JsonProperty("h")][JsonPropertyName("h")] public string? MOTD { get; set; } // server message (not base64 yet)

        public string? mm { get; set; } // mod list ex: "1300825258;1300820029"
        [Newtonsoft.Json.JsonProperty("gt")][JsonPropertyName("gt")] public int? GameType { get; set; } // game type
        [Newtonsoft.Json.JsonProperty("gtd")][JsonPropertyName("gtd")] public int? GameSubType { get; set; } // sub game type
        [Newtonsoft.Json.JsonProperty("pm")][JsonPropertyName("pm")] public int? MaxPlayers { get; set; } // max players

        [Newtonsoft.Json.JsonProperty("tps")][JsonPropertyName("tps")] public int? TPS { get; set; } // tps
        [Newtonsoft.Json.JsonProperty("si")][JsonPropertyName("si")] public EServerInfoMode? ServerInfoMode { get; set; } // gamestate

        // Map the closed modes to their open equivalents for time context purposes
        [JsonIgnore]
        public EServerInfoMode? ServerMode
        {
            get
            {
                return ServerInfoMode switch
                {
                    EServerInfoMode.ClosedWaiting => EServerInfoMode.OpenWaiting,
                    EServerInfoMode.ClosedPlaying => EServerInfoMode.OpenPlaying,
                    _ => ServerInfoMode
                };
            }
        }

        [Newtonsoft.Json.JsonProperty("ti")][JsonPropertyName("ti")] public int? TimeLimit { get; set; } // time limit
        [Newtonsoft.Json.JsonProperty("ki")][JsonPropertyName("ki")] public int? KillLimit { get; set; } // kill limit

        [Newtonsoft.Json.JsonProperty("gtm")][JsonPropertyName("gtm")] public int? GameTimeMinutes { get; set; } // game time min (max 255) (appears to be hard-locked to 60 for post-game)
        [Newtonsoft.Json.JsonProperty("pg")][JsonPropertyName("pg")] public int? MaxPingSeen { get; set; } // seen max ping
        [Newtonsoft.Json.JsonProperty("pgm")][JsonPropertyName("pgm")] public int? MaxPing { get; set; } // max ping


        //#define RAKNET_MASTERSERVER_GAMEID_KEY "gid"
        //#define RAKNET_MASTERSERVER_CLIENTREQUESTID_KEY "cri"
        //#define RAKNET_MASTERSERVER_ROWPASSWORD_KEY "rpwd"
        //#define RAKNET_MASTERSERVER_ROWID_KEY "rid"
        //#define RAKNET_MASTERSERVER_LISTINGTIMEOUT_KEY "to"
        //#define RAKNET_MASTERSERVER_GAMEPASSWORD_KEY "upwd"

        //public string mu { get; set; } // map url, often an empty string // RAKNET_MASTERSERVER_MAPULR_KEY


        public BZCCPlayerData[]? pl { get; set; }

        [Newtonsoft.Json.JsonIgnore][JsonIgnore] public int CurPlayers { get { return pl?.Length ?? 0; } }
        [Newtonsoft.Json.JsonIgnore][JsonIgnore] public bool Locked { get { return l == 1; } }
        [Newtonsoft.Json.JsonIgnore][JsonIgnore] public bool Passworded { get { return k == 1; } }
        [Newtonsoft.Json.JsonIgnore][JsonIgnore] public string? SessionName { get { return string.IsNullOrWhiteSpace(n) ? null : Encoding.GetEncoding(1252).GetString(Convert.FromBase64String(n).TakeWhile(chr => chr != 0x00).ToArray()).Replace('�', '#'); } }

        [Newtonsoft.Json.JsonIgnore][JsonIgnore] public string[] Mods { get { return mm?.Split(';') ?? []; } }


        [Newtonsoft.Json.JsonIgnore][JsonIgnore] public DateTime? GameStateStarted { get; set;}

        public bool IsOnRebellion()
        {
            return proxySource == "Rebellion";
        }

        public bool IsOnIonDriver()
        {
            return proxySource == null;
        }
    }

    public class MapData
    {
        public Dictionary<string, MapData_Mods>? mods { get; set; }
        public string? title { get; set; }
        public string? image { get; set; }
        public string? description { get; set; }
        public Dictionary<string, string>? netVars { get; set; }
    }

    public class MapData_Mods
    {
        public string? name { get; set; }
        public string? workshop_name { get; set; }
        public string? type { get; set; }
        public List<string>? dependencies { get; set; }
        public string? image { get; set; }
    }
}
