using Microsoft.Extensions.Configuration;
using MultiplayerSessionList.Services;
using System;
using System.Collections.Generic;

namespace MultiplayerSessionList.Plugins.BattlezoneCombatCommander
{
    public class DataService
    {
        private readonly TemporalCache<ulong, CachedGameSnapshot> temporalCache;

        public DataService(IConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            var maxDataAgeText = configuration["BattlezoneCombatCommander:DataService:MaxDataAge"];
            var maxDataAge = TimeSpan.FromDays(1);
            if (!string.IsNullOrWhiteSpace(maxDataAgeText) && TimeSpan.TryParse(maxDataAgeText, out var parsedMaxDataAge))
            {
                maxDataAge = parsedMaxDataAge;
            }

            temporalCache = new TemporalCache<ulong, CachedGameSnapshot>(
                maxDataAge,
                snapshot => string.IsNullOrEmpty(snapshot.SessionName) ? Array.Empty<string>() : new[] { snapshot.SessionName });
        }

        public void Decorate(BZCCGame game, DateTime? baseTime)
        {
            if (game == null)
            {
                throw new ArgumentNullException(nameof(game));
            }

            var currentGameMinutes = game.GameTimeMinutes;
            TryEstimateCurrentGameStateStart(game, baseTime, currentGameMinutes);

            temporalCache.TryGet(game.NATNegGuid, out var cachedGame);

            if (cachedGame != null)
            {
                if (ShouldReuseStartTime(cachedGame, game, currentGameMinutes))
                {
                    game.GameStateStarted = cachedGame.GameStateStarted;
                }
            }
            else if (!string.IsNullOrEmpty(game.SessionName))
            {
                var fuzzyGame = TryFindBestFuzzyMatch(game, currentGameMinutes);
                if (fuzzyGame != null)
                {
                    if (currentGameMinutes == 255)
                    {
                        game.GameStateStarted = fuzzyGame.GameStateStarted;
                    }

                    // copy additional data related to the migration here, such as if we add an "ID History" or do other actions
                }
            }

            temporalCache.Set(game.NATNegGuid, CachedGameSnapshot.FromGame(game));
        }

        private static void TryEstimateCurrentGameStateStart(BZCCGame game, DateTime? baseTime, int? currentGameMinutes)
        {
            if (baseTime.HasValue && currentGameMinutes.HasValue && currentGameMinutes.Value != 255)
            {
                game.GameStateStarted = baseTime.Value.AddMinutes(-currentGameMinutes.Value);
            }
        }

        private static bool ShouldReuseStartTime(CachedGameSnapshot cachedGame, BZCCGame currentGame, int? currentGameMinutes)
        {
            if (currentGameMinutes != 255)
            {
                return false;
            }

            if (!currentGame.GameStateStarted.HasValue)
            {
                return true;
            }

            if (cachedGame.ServerMode != currentGame.ServerMode)
            {
                return false;
            }

            if (!cachedGame.GameTimeMinutes.HasValue || !currentGameMinutes.HasValue)
            {
                return false;
            }

            return cachedGame.GameTimeMinutes.Value <= currentGameMinutes.Value;
        }

        private CachedGameSnapshot? TryFindBestFuzzyMatch(BZCCGame currentGame, int? currentGameMinutes)
        {
            if (!currentGameMinutes.HasValue)
            {
                return null;
            }

            CachedGameSnapshot? bestMatch = null;

            foreach (var cacheKey in temporalCache.FuzzyLookup(currentGame.SessionName!))
            {
                if (!temporalCache.TryGet(cacheKey, out var candidate) || candidate == null)
                {
                    continue;
                }

                if (candidate.ServerMode != currentGame.ServerMode)
                {
                    continue;
                }

                if (!candidate.GameTimeMinutes.HasValue || candidate.GameTimeMinutes.Value > currentGameMinutes.Value)
                {
                    continue;
                }

                if (!candidate.HasAnyPlayerOverlap(currentGame))
                {
                    continue;
                }

                if (bestMatch == null || candidate.GameStateStarted > bestMatch.GameStateStarted)
                {
                    bestMatch = candidate;
                }
            }

            return bestMatch;
        }

        private sealed class CachedGameSnapshot
        {
            public string? SessionName { get; private set; }
            public EServerInfoMode? ServerMode { get; private set; }
            public int? GameTimeMinutes { get; private set; }
            public DateTime? GameStateStarted { get; private set; }
            public HashSet<string> PlayerIds { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

            public static CachedGameSnapshot FromGame(BZCCGame game)
            {
                var playerIds = new HashSet<string>(StringComparer.Ordinal);

                if (game.pl != null)
                {
                    foreach (var player in game.pl)
                    {
                        if (player != null && !string.IsNullOrEmpty(player.PlayerID))
                        {
                            playerIds.Add(player.PlayerID);
                        }
                    }
                }

                return new CachedGameSnapshot
                {
                    SessionName = game.SessionName,
                    ServerMode = game.ServerMode,
                    GameTimeMinutes = game.GameTimeMinutes,
                    GameStateStarted = game.GameStateStarted,
                    PlayerIds = playerIds
                };
            }

            public bool HasAnyPlayerOverlap(BZCCGame game)
            {
                if (PlayerIds.Count == 0 || game.pl == null)
                {
                    return false;
                }

                foreach (var player in game.pl)
                {
                    if (player != null && !string.IsNullOrEmpty(player.PlayerID) && PlayerIds.Contains(player.PlayerID))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
