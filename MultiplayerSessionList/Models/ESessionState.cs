using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Models
{
    public enum ESessionState
    {
        Unknown, // Normally has a state, but it is unknown

        PreGame, // Pre-Game Setup
        Loading, // Loading Game
        InGame, // In-Game
        PostGame, // Post-Game or GameOver
        
        Paused,
        Away,
        Busy,
        NotResponding,
    }
}
