using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

public class DynamicTaskPool<T>
{
    private readonly ConcurrentQueue<Task<T>> _tasks = new();
    private readonly Channel<Task<T>> _newTasks = Channel.CreateUnbounded<Task<T>>();

    public void Add(Task<T> task)
    {
        _newTasks.Writer.TryWrite(task);
    }

    public IAsyncEnumerable<T> RunUntilEmptyAsync(CancellationToken cancellationToken = default)
    {
        return InternalRunAsync(true, cancellationToken);
    }

    public IAsyncEnumerable<T> RunAsync(CancellationToken cancellationToken = default)
    {
        return InternalRunAsync(false, cancellationToken);
    }

    private async IAsyncEnumerable<T> InternalRunAsync(bool stopWhenEmpty, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var running = new List<Task<T>>();

        // Initial drain of any tasks already in the queue
        while (_tasks.TryDequeue(out var t))
            running.Add(t);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Add any new tasks that have been enqueued
            while (_newTasks.Reader.TryRead(out var newTask))
                running.Add(newTask);

            if (running.Count == 0)
            {
                if (stopWhenEmpty) yield break;
                // Wait for a new task to be added
                var newTask = await _newTasks.Reader.ReadAsync(cancellationToken);
                running.Add(newTask);
                continue;
            }

            var completed = await Task.WhenAny(running);
            running.Remove(completed);

            // Yield the result
            yield return await completed;

            // After yielding, check for more new tasks before next loop
        }
    }
}