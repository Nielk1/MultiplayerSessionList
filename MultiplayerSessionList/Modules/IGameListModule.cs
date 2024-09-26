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
        public const string TYPE_LISTEN = "listen";
        public const string TYPE_DEDICATED = "dedicated";

        //public const string ATTRIBUTE_LISTSERVER = "list_server";

        public const string STATUS_LOCKED = "is_locked";
        public const string STATUS_PASSWORD = "has_password";
        public const string STATUS_STATE = "state";

        public const string PLAYERTYPE_PLAYER = "player";
        public const string PLAYERTYPE_SPECTATOR = "spectator";
        public const string PLAYERTYPE_BOT = "bot";
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
        string GameID { get; }
        string Title { get; }
        bool IsPublic { get; }

        IAsyncEnumerable<Datum> GetGameListChunksAsync(bool multiGame, bool admin, CancellationToken cancellationToken = default);
    }

    public class GameListData
    {
        public DataCacheOld Metadata { get; set; }
        public SessionItem SessionsDefault { get; set; }
        public DataCacheOld DataCache { get; set; }
        public DataCacheOld Heroes { get; set; }
        public DataCacheOld Mods { get; set; }
        public IEnumerable<SessionItem> Sessions { get; set; }
        public string Raw { get; set; }
    }
}
