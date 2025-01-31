using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Models
{
    public static class SESSION_STATE
    {
        public const string Unknown = "unknown"; // Normally has a state, but it is unknown

        public const string PreGame = "pre_game"; // Pre-Game Setup
        public const string Loading = "loading"; // Loading Game
        public const string InGame = "in_game"; // In-Game
        public const string PostGame = "post_game"; // Post-Game or GameOver

        // unused options so far
        public const string Paused = "paused";
        public const string Away = "away";
        public const string Busy = "busy";
        public const string NotResponding = "not_responding";
    }
}
