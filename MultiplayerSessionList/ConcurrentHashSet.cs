using System.Collections.Concurrent;
using System.Collections.Generic;

public class ConcurrentHashSet<T>
{
    private readonly ConcurrentDictionary<T, bool> _dict = new();

    public bool Add(T item) => _dict.TryAdd(item, true);

    public bool Remove(T item) => _dict.TryRemove(item, out _);

    public bool Contains(T item) => _dict.ContainsKey(item);

    public void Clear() => _dict.Clear();

    public int Count => _dict.Count;

    public IEnumerable<T> Items => _dict.Keys;
}