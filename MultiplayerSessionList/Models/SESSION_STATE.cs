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
        public const string PreMatch = "pre_match"; // in-game but not yet in-play, either in a waiting room, map, or interactive staging area
        public const string InGame = "in_game"; // In-Game
        public const string PostGame = "post_game"; // Post-Game or GameOver

        // Not used by anything yet
        public const string Matchmaking = "matchmaking"; // Server is participating in matchmaking as a usable server, basically a form of automated "pre_game"
        public const string Intermission = "intermission"; // break period between rounds of "in_game"
        public const string Overtime = "overtime"; // "in_game" beyond normal playable time, like sudden_death for example
        public const string Replay = "replay"; // might just be "post_game" but note if we ever actually use this
        public const string Maintenance = "maintenance"; // server online but not available for play, such as updates or admin tasks

        // unused options so far
        public const string Paused = "paused";
        public const string Away = "away";
        public const string Busy = "busy";
        public const string NotResponding = "not_responding";
    }

    public static class SESSION_STATE_LEGACY
    {
        public const string Unknown = "Unknown"; // Normally has a state, but it is unknown

        public const string PreGame = "PreGame"; // Pre-Game Setup
        public const string Loading = "Loading"; // Loading Game
        public const string InGame = "InGame"; // In-Game
        public const string PostGame = "PostGame"; // Post-Game or GameOver

        // unused options so far
        public const string Paused = "Paused";
        public const string Away = "Away";
        public const string Busy = "Busy";
        public const string NotResponding = "NotResponding";
    }
}
