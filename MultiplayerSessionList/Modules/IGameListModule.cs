using MultiplayerSessionList.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Modules
{
    public static class GAMELIST_TERMS
    {
        public const string TYPE_LISTEN = "Listen";
        public const string TYPE_DEDICATED = "Dedicated";
        

        public const string STATUS_LOCKED = "IsLocked";
        public const string STATUS_PASSWORD = "HasPassword";

        public const string PLAYERTYPE_PLAYER = "Player";
        public const string PLAYERTYPE_SPECTATOR = "Spectator";
    }

    public interface IGameListModule
    {
        string GameID { get; }
        string Title { get; }

        Task<(SessionItem, DataCache, IEnumerable<SessionItem>, JToken)> GetGameList();

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
}
