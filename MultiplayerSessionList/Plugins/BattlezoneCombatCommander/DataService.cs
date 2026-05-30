using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MultiplayerSessionList.Plugins.BattlezoneCombatCommander
{
    public class DataService
    {
        private readonly TemporalCache<ulong, BZCCGame> temporalCache;
        private readonly TimeSpan maxDataAge;

        public DataService(IConfiguration configuration)
        {
            string? maxDataAgeText = configuration[$"BattlezoneCombatCommander:DataService:MaxDataAge"];
            TimeSpan maxDataAge = TimeSpan.FromDays(1);
            if (maxDataAgeText != null && TimeSpan.TryParse(maxDataAgeText, out TimeSpan parsedMaxDataAge))
                maxDataAge = parsedMaxDataAge;

            this.temporalCache = new TemporalCache<ulong, BZCCGame>(maxDataAge, m =>
            {
                var keys = new List<string>();
                if (!string.IsNullOrEmpty(m.SessionName))
                    keys.Add(m.SessionName);
                return keys;
            });
        }

        public void Decorate(BZCCGame game, DateTime? baseTime)
        {
            // first, build a best effort estimate of when the game's current state started, if possible
            if (baseTime.HasValue)
            {
                if (game.GameTimeMinutes.HasValue)
                {
                    if (game.GameTimeMinutes.Value != 255)
                        game.GameStateStarted = baseTime.Value.AddMinutes(-game.GameTimeMinutes.Value);
                }
            }

            temporalCache.TryGet(game.NATNegGuid, out var cachedGame);
            if (cachedGame != null)
            {
                // the cached game has the same NATNegGuid so we know it's the same game

                if (!game.GameStateStarted.HasValue || (cachedGame.ServerMode == game.ServerMode && cachedGame.GameTimeMinutes.Value <= game.GameTimeMinutes.Value))
                {
                    // either we don't have a current estimate for when the game started or the cached game is in the same server mode and has a game time current or in the past

                    if (game.GameTimeMinutes.Value == 255)
                    {
                        // The current game's data is past possible measurement, so use the cached game's start time if we have it to preserve the best possible estimate of when the game started

                        game.GameStateStarted = cachedGame.GameStateStarted;
                    }
                }
            }

            if (cachedGame == null && game.SessionName != null)
            {
                // there was no successful match to a game, which either means we've never seen it before, or a host migration occurred

                var possibleMatches = temporalCache.FuzzyLookup(game.SessionName);
                if (possibleMatches != null)
                {
                    // try to find the best matching game from the temporal cache
                    // the session name was guaranteed to match, but might not be unique
                    // confirm that the candidate matches are in the same server mode and have a game time that is current or in the past
                    // confirm that at least one player matches between the candidate and current game, which would be required for a migration (a double migration to a whole new player is so extremely unlikely as to be irrelevant)
                    // if multiple candidates remain, take the most recent one, which helps ensure we are matching the same game and not just a different game with the same name and similar time
                    var fuzzyGame = possibleMatches.Select(dr =>
                    {
                        if (temporalCache.TryGet(dr, out BZCCGame value))
                            return value;
                        return null;
                    }).Where(dr => dr != null && dr.ServerMode == game.ServerMode && cachedGame.GameTimeMinutes.Value <= game.GameTimeMinutes.Value)
                    .Where(dr => dr.pl.Any(player => player != null && (game.pl?.Any(px => player.PlayerID == px.PlayerID) ?? false))) // at least one matching player, helps ensure we are matching the same game and not just a different game with the same name and similar time)
                    .OrderByDescending(dr => dr.GameStateStarted)
                    .FirstOrDefault();

                    if (fuzzyGame != null)
                    {
                        if (game.GameTimeMinutes.Value == 255)
                        {
                            // The current game's data is past possible measurement, so use the cached game's start time if we have it to preserve the best possible estimate of when the game started

                            game.GameStateStarted = fuzzyGame.GameStateStarted;
                        }

                        // copy additional data related to the migration here, such as if we add an "ID History" or do other actions
                    }
                }
            }

            // push the current game data into the cache
            temporalCache.Set(game.NATNegGuid, game);
        }
    }
}
