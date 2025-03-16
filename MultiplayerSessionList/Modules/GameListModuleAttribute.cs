using System;

namespace MultiplayerSessionList.Modules
{
    internal class GameListModuleAttribute : Attribute
    {
        public string GameID { get; private set; }
        public string Title { get; private set; }
        public bool IsPublic { get; private set; }
        public GameListModuleAttribute(string GameID, string Title, bool IsPublic)
        {
            this.GameID = GameID;
            this.Title = Title;
            this.IsPublic = IsPublic;
        }
    }
}