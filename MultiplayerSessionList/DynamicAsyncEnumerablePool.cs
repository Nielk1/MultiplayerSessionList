using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Linq;
using MultiplayerSessionList.Extensions;

public class DynamicAsyncEnumerablePool<T>
{
    private readonly ConcurrentQueue<IAsyncEnumerable<T>> _enumerables = new();

    public void Add(IAsyncEnumerable<T> enumerable)
    {
        _enumerables.Enqueue(enumerable);
    }

    public IAsyncEnumerable<T> RunUntilEmptyAsync(CancellationToken cancellationToken = default)
    {
        return _enumerables.ToArray().SelectManyAsync(cancellationToken);
    }
}