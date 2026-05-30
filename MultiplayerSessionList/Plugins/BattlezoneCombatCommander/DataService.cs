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
            if (baseTime.HasValue)
            {
                DateTime recordDate = baseTime.Value;

                if (game.GameTimeMinutes.HasValue)
                {
                    if (game.GameTimeMinutes.Value != 255)
                        game.GameStateStarted = recordDate.AddMinutes(-game.GameTimeMinutes.Value);

                    temporalCache.TryGet(game.NATNegGuid, out var cachedGame);
                    if (cachedGame != null && cachedGame.GameTimeMinutes.HasValue && cachedGame.GameStateStarted.HasValue)
                    {
                        if (cachedGame.ServerMode == game.ServerMode && cachedGame.GameTimeMinutes.Value <= game.GameTimeMinutes.Value)
                        {
                            if (game.GameTimeMinutes.Value == 255)
                            {
                                // current game is past all possible measurement, so use the cached game's start time if we have it to preserve the best possible estimate of when the game started
                                game.GameStateStarted = cachedGame.GameStateStarted;
                            }
                        }
                    }
                    else if (game.SessionName != null)
                    {
                        // fuzzy match, which helps with host migration
                        var possibleMatches = temporalCache.FuzzyLookup(game.SessionName);
                        if (possibleMatches != null)
                        {
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
                                    // current game is past all possible measurement, so use the fuzzy matched game's start time if we have it to preserve the best possible estimate of when the game started
                                    game.GameStateStarted = fuzzyGame.GameStateStarted;
                                }
                            }
                        }
                    }
                }
            }

            temporalCache.Set(game.NATNegGuid, game);
        }
    }
}
