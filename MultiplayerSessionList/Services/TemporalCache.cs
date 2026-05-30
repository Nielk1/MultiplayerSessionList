using System;
using System.Collections.Generic;
using System.Threading;

namespace MultiplayerSessionList.Services
{
    public class TemporalCache<K, T> : IDisposable where K : notnull
    {
        private readonly Dictionary<K, T> _cache = new();
        private readonly Dictionary<K, DateTime> _lastTouched = new();
        private readonly Dictionary<string, HashSet<K>> _fuzzyLookup = new();
        private readonly Dictionary<K, HashSet<string>> _reverseFuzzyLookup = new();
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly Func<T, IEnumerable<string>> _fuzzyKeySelector;
        private readonly TimeSpan _checkRate = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _expiration;

        private long _lastCheckedExpirationTicks = DateTime.UtcNow.Ticks;
        private int _disposed;

        public TemporalCache(TimeSpan expiration, Func<T, IEnumerable<string>> fuzzyKeySelector)
        {
            if (expiration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(expiration), "Expiration must be greater than zero.");
            }

            _expiration = expiration;
            _fuzzyKeySelector = fuzzyKeySelector ?? throw new ArgumentNullException(nameof(fuzzyKeySelector));
        }

        public void Set(K key, T value)
        {
            ThrowIfDisposed();
            TryCleanupExpiredEntries();

            _lock.EnterWriteLock();
            try
            {
                RemoveFuzzyMappingsInternal(key);

                _cache[key] = value;
                _lastTouched[key] = DateTime.UtcNow;

                var fuzzyKeys = new HashSet<string>();
                foreach (var fuzzyKey in _fuzzyKeySelector(value) ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrEmpty(fuzzyKey))
                    {
                        fuzzyKeys.Add(fuzzyKey);
                    }
                }

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
            ThrowIfDisposed();
            TryCleanupExpiredEntries();

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

                value = default!;
                return false;
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
        }

        public IEnumerable<K> FuzzyLookup(string fuzzyKey)
        {
            ThrowIfDisposed();

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

        public DateTime? GetLastTouched(K key)
        {
            ThrowIfDisposed();

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
            ThrowIfDisposed();

            _lock.EnterWriteLock();
            try
            {
                RemoveInternal(key);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private void TryCleanupExpiredEntries()
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            var lastCheckTicks = Interlocked.Read(ref _lastCheckedExpirationTicks);
            if (nowTicks - lastCheckTicks < _checkRate.Ticks)
            {
                return;
            }

            _lock.EnterWriteLock();
            try
            {
                nowTicks = DateTime.UtcNow.Ticks;
                lastCheckTicks = Interlocked.Read(ref _lastCheckedExpirationTicks);
                if (nowTicks - lastCheckTicks < _checkRate.Ticks)
                {
                    return;
                }

                var expirationCutoff = new DateTime(nowTicks - _expiration.Ticks, DateTimeKind.Utc);
                var keysToRemove = new List<K>();

                foreach (var kvp in _lastTouched)
                {
                    if (kvp.Value <= expirationCutoff)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    RemoveInternal(key);
                }

                Interlocked.Exchange(ref _lastCheckedExpirationTicks, nowTicks);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private void RemoveInternal(K key)
        {
            if (_cache.Remove(key))
            {
                _lastTouched.Remove(key);
                RemoveFuzzyMappingsInternal(key);
            }
        }

        private void RemoveFuzzyMappingsInternal(K key)
        {
            if (_reverseFuzzyLookup.TryGetValue(key, out var fuzzyKeys))
            {
                foreach (var fuzzyKey in fuzzyKeys)
                {
                    if (_fuzzyLookup.TryGetValue(fuzzyKey, out var set))
                    {
                        set.Remove(key);
                        if (set.Count == 0)
                        {
                            _fuzzyLookup.Remove(fuzzyKey);
                        }
                    }
                }

                _reverseFuzzyLookup.Remove(key);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _lock.Dispose();
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(TemporalCache<K, T>));
            }
        }
    }
}