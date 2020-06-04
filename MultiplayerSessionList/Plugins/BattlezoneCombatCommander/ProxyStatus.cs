using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.BattlezoneCombatCommander
{
    public class ProxyStatus
    {
        public DateTime? updated { get; set; }
        public string status { get; set; }
        public bool? success { get; set; }
    }
}
