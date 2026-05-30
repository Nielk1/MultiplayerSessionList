using System;
using System.Collections.Generic;
using System.Threading;

namespace MultiplayerSessionList.Services
{
    public class TemporalCache<K, T> where K : notnull
    {
        private readonly Dictionary<K, T> _cache = new();
        private readonly Dictionary<K, DateTime> _lastTouched = new();
        private readonly Dictionary<string, HashSet<K>> _fuzzyLookup = new();
        private readonly Dictionary<K, HashSet<string>> _reverseFuzzyLookup = new();
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly Func<T, IEnumerable<string>> _fuzzyKeySelector;
        private readonly TimeSpan _checkRate = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _expiration;
        private DateTime lastCheckedExpiration = DateTime.UtcNow;

        public TemporalCache(TimeSpan expiration, Func<T, IEnumerable<string>> fuzzyKeySelector)
        {
            _expiration = expiration;
            _fuzzyKeySelector = fuzzyKeySelector ?? throw new ArgumentNullException(nameof(fuzzyKeySelector));
        }

        public void Set(K key, T value)
        {
            _lock.EnterWriteLock();
            try
            {
                // Remove old fuzzy keys if key already exists
                if (_cache.ContainsKey(key) && _reverseFuzzyLookup.TryGetValue(key, out var oldFuzzyKeys))
                {
                    foreach (var fuzzyKey in oldFuzzyKeys)
                    {
                        if (_fuzzyLookup.TryGetValue(fuzzyKey, out var set))
                        {
                            set.Remove(key);
                            if (set.Count == 0)
                                _fuzzyLookup.Remove(fuzzyKey);
                        }
                    }
                }

                _cache[key] = value;
                _lastTouched[key] = DateTime.UtcNow;

                var fuzzyKeys = new HashSet<string>(_fuzzyKeySelector(value) ?? Array.Empty<string>());
                _reverseFuzzyLookup[key] = fuzzyKeys;

                foreach (var fuzzyKey in fuzzyKeys)
                {
                    if (!_fuzzyLookup.TryGetValue(fuzzyKey, out var set))
                    {
                        set = new HashSet<K>();
                        _fuzzyLookup[fuzzyKey] = set;
                    }
                    set.Add(key);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool TryGet(K key, out T value)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_cache.TryGetValue(key, out value))
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        _lastTouched[key] = DateTime.UtcNow;
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                    return true;
                }
                return false;
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
        }

        public IEnumerable<K> FuzzyLookup(string fuzzyKey)
        {
            _lock.EnterReadLock();
            try
            {
                if (_fuzzyLookup.TryGetValue(fuzzyKey, out var set))
                {
                    return [.. set];
                }
                return Array.Empty<K>();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private void TryCleanupExpiredEntries()
        {
            if (DateTime.UtcNow - lastCheckedExpiration < _checkRate)
                return;
            _lock.EnterWriteLock();
            try
            {
                var now = DateTime.UtcNow;
                var keysToRemove = new List<K>();
                foreach (var kvp in _lastTouched)
                {
                    if (now - kvp.Value > _expiration)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                foreach (var key in keysToRemove)
                {
                    Remove(key);
                }
                lastCheckedExpiration = now;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public DateTime? GetLastTouched(K key)
        {
            _lock.EnterReadLock();
            try
            {
                if (_lastTouched.TryGetValue(key, out var dt))
                {
                    return dt;
                }
                return null;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Remove(K key)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_cache.Remove(key))
                {
                    _lastTouched.Remove(key);

                    if (_reverseFuzzyLookup.TryGetValue(key, out var fuzzyKeys))
                    {
                        foreach (var fuzzyKey in fuzzyKeys)
                        {
                            if (_fuzzyLookup.TryGetValue(fuzzyKey, out var set))
                            {
                                set.Remove(key);
                                if (set.Count == 0)
                                    _fuzzyLookup.Remove(fuzzyKey);
                            }
                        }
                        _reverseFuzzyLookup.Remove(key);
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        // Add expiration/purge logic as needed, calling Remove(key) for expired entries.
    }
}