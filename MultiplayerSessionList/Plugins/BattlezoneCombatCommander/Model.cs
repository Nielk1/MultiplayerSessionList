using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.BattlezoneCombatCommander
{
    public class BZCCRaknetData
    {
        public List<BZCCGame> GET { get; set; }

        public Dictionary<string, ProxyStatus> proxyStatus { get; set; }
    }

    ///// All possible types of NATs (except NAT_TYPE_COUNT, which is an internal value) 
    //enum NATTypeDetectionResult
    //{
    //    /// Works with anyone
    //    NAT_TYPE_NONE,
    //    /// Accepts any datagrams to a port that has been previously used. Will accept the first datagram from the remote peer.
    //    NAT_TYPE_FULL_CONE,
    //    /// Accepts datagrams to a port as long as the datagram source IP address is a system we have already sent to. Will accept the first datagram if both systems send simultaneously. Otherwise, will accept the first datagram after we have sent one datagram.
    //    NAT_TYPE_ADDRESS_RESTRICTED,
    //    /// Same as address-restricted cone NAT, but we had to send to both the correct remote IP address and correct remote port. The same source address and port to a different destination uses the same mapping.
    //    NAT_TYPE_PORT_RESTRICTED,
    //    /// A different port is chosen for every remote destination. The same source address and port to a different destination uses a different mapping. Since the port will be different, the first external punchthrough attempt will fail. For this to work it requires port-prediction (MAX_PREDICTIVE_PORT_RANGE>1) and that the router chooses ports sequentially.
    //    NAT_TYPE_SYMMETRIC,
    //    /// Hasn't been determined. NATTypeDetectionClient does not use this, but other plugins might
    //    NAT_TYPE_UNKNOWN,
    //    /// In progress. NATTypeDetectionClient does not use this, but other plugins might
    //    NAT_TYPE_DETECTION_IN_PROGRESS,
    //    /// Didn't bother figuring it out, as we support UPNP, so it is equivalent to NAT_TYPE_NONE. NATTypeDetectionClient does not use this, but other plugins might
    //    NAT_TYPE_SUPPORTS_UPNP,
    //    /// \internal Must be last
    //    NAT_TYPE_COUNT
    //};

    public class BZCCPlayerData
    {
        public string n { get; set; } // name (base 64)

        [JsonProperty("i")] public string PlayerID { get; set; } // id (player ID)
        public string k { get; set; } // kills
        public string d { get; set; } // deaths
        public string s { get; set; } // score
        public string t { get; set; } // team

        [JsonIgnore] public string Name { get { return string.IsNullOrWhiteSpace(n) ? null : Encoding.UTF8.GetString(Convert.FromBase64String(n)); } }
        [JsonIgnore] public int? Kills { get { int tmp = 0; return int.TryParse(k, out tmp) ? (int?)tmp : null; } }
        [JsonIgnore] public int? Deaths { get { int tmp = 0; return int.TryParse(d, out tmp) ? (int?)tmp : null; } }
        [JsonIgnore] public int? Score { get { int tmp = 0; return int.TryParse(s, out tmp) ? (int?)tmp : null; } }
        [JsonIgnore] public int? Team { get { int tmp = 0; return int.TryParse(t, out tmp) ? (int?)tmp : null; } }
    }

    public class BZCCGame
    {
        //public string __addr { get; set; }
        public string proxySource { get; set; }

        public string g { get; set; } // ex "4M-CB73@GX" (seems to go with NAT type 5???)
        public string n { get; set; } // varchar(256) | Name of client game session, base64 and null terminate.
        [JsonProperty("m")] public string MapFile { get; set; } // varchar(68)  | Name of client map, no bzn extension.
        public string k { get; set; } // tinyint      | Password Flag.
        public string d { get; set; } // varchar(16)  | MODSLISTCRC_KEY
        public string t { get; set; } // tinyint      | NATTYPE_KEY //nat type 5 seems bad, 7 seems to mean direct connect
        public string v { get; set; } // varchar(8)   | GAMEVERSION_KEY (nice string now)
        public string l { get; set; } // locked
        [JsonProperty("h")] public string MOTD { get; set; } // server message (not base64 yet)

        public string mm { get; set; } // mod list ex: "1300825258;1300820029"
        public string gt { get; set; } // game type
        public string gtd { get; set; } // sub game type
        public string pm { get; set; } // max players

        public string tps { get; set; } // tps
        public string si { get; set; } // gamestate
        public string ti { get; set; } // time limit
        public string ki { get; set; } // kill limit

        public string gtm { get; set; } // game time min
        public string pgm { get; set; } // max ping

        public BZCCPlayerData[] pl { get; set; }

        [JsonIgnore] public int CurPlayers { get { return pl?.Length ?? 0; } }
        [JsonIgnore] public int? MaxPlayers { get { int tmp = 0; return int.TryParse(pm, out tmp) ? (int?)tmp : null; } }

        [JsonIgnore] public bool Locked { get { return l == "1"; } }
        [JsonIgnore] public bool Passworded { get { return k == "1"; } }

        [JsonIgnore] public int? GameType { get { int tmp = 0; return int.TryParse(gt, out tmp) ? (int?)tmp : null; } }
        [JsonIgnore] public int? GameSubType { get { int tmp = 0; return int.TryParse(gtd, out tmp) ? (int?)tmp : null; } }

        [JsonIgnore] public int? ServerInfoMode { get { int tmp = 0; return int.TryParse(si, out tmp) ? (int?)tmp : null; } }

        [JsonIgnore] public int? GameTimeMinutes { get { int tmp = 0; return int.TryParse(gtm, out tmp) ? (int?)tmp : null; } }

        [JsonIgnore] public int? TPS { get { int tmp = 0; return int.TryParse(tps, out tmp) ? (int?)tmp : null; } }
        [JsonIgnore] public int? MaxPing { get { int tmp = 0; return int.TryParse(pgm, out tmp) ? (int?)tmp : null; } }
        [JsonIgnore] public int? TimeLimit { get { int tmp = 0; return int.TryParse(ti, out tmp) ? (int?)tmp : null; } }
        [JsonIgnore] public int? KillLimit { get { int tmp = 0; return int.TryParse(ki, out tmp) ? (int?)tmp : null; } }

        [JsonIgnore] public string Name { get { return string.IsNullOrWhiteSpace(n) ? null : Encoding.UTF8.GetString(Convert.FromBase64String(n).TakeWhile(chr => chr != 0x00).ToArray()); } }
        //[JsonIgnore] public string MOTD { get { try { return string.IsNullOrWhiteSpace(h) ? null : Encoding.UTF8.GetString(Convert.FromBase64String(h)); } catch { return null; } } }

        [JsonIgnore] public string[] Mods { get { return mm?.Split(';') ?? new string[] { }; } }

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
        public Dictionary<string, MapData_Mods> mods { get; set; }
        public string title { get; set; }
        public string image { get; set; }
        public string description { get; set; }
    }

    public class MapData_Mods
    {
        public string name { get; set; }
        public string type { get; set; }
        public List<string> dependencies { get; set; }
    }
}
