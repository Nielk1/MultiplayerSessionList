using MultiplayerSessionList.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Modules
{
    public static class GAMELIST_TERMS_OLD
    {
        public const string TYPE_LISTEN = "Listen";
        public const string TYPE_DEDICATED = "Dedicated";
        
        public const string ATTRIBUTE_LISTSERVER = "ListServer";
        
        public const string STATUS_LOCKED = "IsLocked";
        public const string STATUS_PASSWORD = "HasPassword";
        public const string STATUS_STATE = "State";

        public const string PLAYERTYPE_PLAYER = "Player";
        public const string PLAYERTYPE_SPECTATOR = "Spectator";
        public const string PLAYERTYPE_BOT = "Bot";
    }
    public static class GAMELIST_TERMS
    {
        public const string TYPE_ROOT = "root";

        //public const string TYPE_DEFAULT = "default";

        public const string TYPE_LOBBY = "lobby";

        public const string TYPE_SESSION = "session";
            public const string SESSION_TYPE = "type";
                public const string SESSION_TYPE_VALUE_LISTEN = "listen";
                public const string SESSION_TYPE_VALUE_DEDICATED = "dedicated";
            public const string SESSION_SOURCES = "sources";
            public const string SESSION_NAME = "name";
            public const string SESSION_MESSAGE = "message";
            public const string SESSION_GAME = "game";
                public const string SESSION_GAME_MODS = "mods";
                    public const string SESSION_GAME_MODS_MAJOR = "major";
                    public const string SESSION_GAME_MODS_MINOR = "minor";
                    public const string SESSION_GAME_MODS_OPTION = "option";
                public const string SESSION_GAME_VERSION = "version";
                public const string SESSION_GAME_GAMEBALANCE = "game_balance";
                public const string SESSION_GAME_OTHER = "other";
            public const string SESSION_STATUS = "status";
                public const string SESSION_STATUS_LOCKED = "is_locked";
                public const string SESSION_STATUS_PASSWORD = "has_password";
                public const string SESSION_STATUS_STATE = "state";
                public const string SESSION_STATUS_OTHER = "other";
            public const string SESSION_ADDRESS = "address";
                public const string SESSION_ADDRESS_TOKEN = "token";
                public const string SESSION_ADDRESS_OTHER = "other";
            public const string SESSION_LEVEL = "level";
                public const string SESSION_LEVEL_GAMETYPE = "game_type";
                public const string SESSION_LEVEL_GAMEMODE = "game_mode";
                public const string SESSION_LEVEL_MAP = "map";
                public const string SESSION_LEVEL_RULES = "rules"; // kind of like other but we consider these as "rules"
                public const string SESSION_LEVEL_OTHER = "other";
            public const string SESSION_TEAMS = "teams";
                public const string SESSION_TEAMS_X_MAX = "max";
                public const string SESSION_TEAMS_X_HUMAN = "human";
                public const string SESSION_TEAMS_X_COMPUTER = "computer";
            public const string SESSION_TIME = "time";
                public const string SESSION_TIME_SECONDS = "seconds";
                public const string SESSION_TIME_RESOLUTION = "resolution";
                public const string SESSION_TIME_MAX = "max";
                public const string SESSION_TIME_CONTEXT = "context"; // if the timer is constrained to a context
            public const string SESSION_PLAYERS = "players";
            public const string SESSION_PLAYERTYPES = "player_types";
            public const string SESSION_PLAYERCOUNT = "player_count";
            public const string SESSION_OTHER = "other";

        public const string TYPE_SOURCE = "source";
            public const string SOURCE_NAME = "name";

        public const string TYPE_PLAYER = "player";
            public const string PLAYER_NAME = "name";
            public const string PLAYER_TYPE = "type";
            public const string PLAYER_INDEX = "index";
            public const string PLAYER_IDS = "ids";
                public const string PLAYER_IDS_X_ID = "id";
                public const string PLAYER_IDS_X_TYPE = "type";
                public const string PLAYER_IDS_X_RAW = "raw";
                public const string PLAYER_IDS_X_IDENTITY = "identity";
            public const string PLAYER_TEAM = "team";
                public const string PLAYER_TEAM_ID = "id";
                public const string PLAYER_TEAM_LEADER = "leader";
                public const string PLAYER_TEAM_INDEX = "index";
            public const string PLAYER_ISHOST = "is_host";
            public const string PLAYER_STATS = "stats";
            public const string PLAYER_HERO = "hero";
            public const string PLAYER_OTHER = "other";

        //public const string TYPE_MODWRAP = "modwrap";
            public const string MODWRAP_ROLE = "role";
            public const string MODWRAP_MOD = "mod";
            public const string MODWRAP_ROLES_MAIN = "main";
            public const string MODWRAP_ROLES_RULE = "rule"; // special, not used yet
        public const string MODWRAP_ROLES_DEPENDENCY = "dependency";

        public const string TYPE_MOD = "mod";
            public const string MOD_NAME = "name";
            public const string MOD_IMAGE = "image";
            public const string MOD_URL = "url";
            public const string MOD_DEPENDENCIES = "dependencies";

        public const string TYPE_MAP = "map";
            public const string MAP_NAME = "name";
            public const string MAP_DESCRIPTION = "description";
            public const string MAP_IMAGE = "image";
            public const string MAP_MAPFILE = "map_file";
            public const string MAP_GAMETYPE = "game_type";
            public const string MAP_GAMEMODE = "game_mode";
            public const string MAP_GAMEBALANCE = "game_balance";
            public const string MAP_TEAMS = "teams";
                public const string MAP_TEAMS_X_NAME = "name";
            public const string MAP_ALLOWEDHEROES = "allowed_heroes";
            public const string MAP_OTHER = "other";

        public const string TYPE_HERO = "hero";
            public const string HERO_NAME = "name";
            public const string HERO_DESCRIPTION = "description";
            public const string HERO_FACTION = "faction";

        public const string TYPE_GAMEBALANCE = "game_balance";
            public const string GAMEBALANCE_NAME = "name";
            public const string GAMEBALANCE_ABBR = "abbr";
            public const string GAMEBALANCE_NOTE = "note";

        public const string TYPE_GAMETYPE = "game_type";
            public const string GAMETYPE_NAME = "name";
            public const string GAMETYPE_ICON = "icon";
            public const string GAMETYPE_COLOR = "color";
            public const string GAMETYPE_COLORF = "color_f";
            public const string GAMETYPE_COLORB = "color_b";
            public const string GAMETYPE_COLORDF = "color_df";
            public const string GAMETYPE_COLORDB = "color_db";
            public const string GAMETYPE_COLORLF = "color_lf";
            public const string GAMETYPE_COLORLB = "color_lb";

        public const string TYPE_GAMEMODE = "game_mode";
            public const string GAMEMODE_NAME = "name";
            public const string GAMEMODE_ICON = "icon";
            public const string GAMEMODE_COLOR = "color";
            public const string GAMEMODE_COLORF = "color_f";
            public const string GAMEMODE_COLORB = "color_b";
            public const string GAMEMODE_COLORDF = "color_df";
            public const string GAMEMODE_COLORDB = "color_db";
            public const string GAMEMODE_COLORLF = "color_lf";
            public const string GAMEMODE_COLORLB = "color_lb";

        public const string TYPE_IDENTITYSTEAM = "identity/steam";
        public const string TYPE_IDENTITYGOG = "identity/gog";

        public const string PLAYERTYPE_TYPES = "types";
            public const string PLAYERTYPE_TYPES_VALUE_PLAYER = "player";
            public const string PLAYERTYPE_TYPES_VALUE_SPECTATOR = "spectator";
            public const string PLAYERTYPE_TYPES_VALUE_BOT = "bot";
            public const string PLAYERTYPE_MAX = "max";

        public const string TYPE_FACTION = "faction";
            public const string FACTION_NAME = "name";
            public const string FACTION_ABBR = "abbr";
            public const string FACTION_BLOCK = "block";
    }

    public interface IGameListModuleOld
    {
        string GameID { get; }
        string Title { get; }
        bool IsPublic { get; }

        Task<GameListData> GetGameList(bool admin);

        /// <summary>
        /// Change to alter QueryString values.
        /// </summary>
        /// <param name="queryString"></param>
        //void InterceptQueryStringForGet(ref Microsoft.AspNetCore.Http.IQueryCollection queryString);

        /// <summary>
        /// Alterations to the game list may be made here immediatly after the database lookup
        /// </summary>
        /// <param name="queryString"></param>
        /// <param name="rawGames"></param>
        //void PreProcessGameList(Microsoft.AspNetCore.Http.IQueryCollection queryString, ref List<GameData> rawGames, ref Dictionary<string, JObject> ExtraData);



        //double GetLastResult { get; }
        //double Execute(double value1, double value2);

        //event EventHandler OnExecute;

        //void ExceptionTest(string input);
    }

    public interface IGameListModule
    {
        //string GameID { get; }
        //string Title { get; }
        //bool IsPublic { get; }

        IAsyncEnumerable<Datum> GetGameListChunksAsync(bool admin, bool mock, CancellationToken cancellationToken = default);
    }

    public class GameListData
    {
        public DataCacheOld? Metadata { get; set; }
        public SessionItem? SessionsDefault { get; set; }
        public DataCacheOld? DataCache { get; set; }
        public DataCacheOld? Heroes { get; set; }
        public DataCacheOld? Mods { get; set; }
        public IEnumerable<SessionItem>? Sessions { get; set; }
        public string? Raw { get; set; }
    }
}
