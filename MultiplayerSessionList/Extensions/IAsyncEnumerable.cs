using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Extensions
{
    public static class IAsyncEnumerableExtensions
    {
        public static async IAsyncEnumerable<TItem> DelayAsync<TItem>(this IAsyncEnumerable<TItem> source, int delay)
        {
            await foreach (var item in source)
            {
                if (delay > 0)
                    await Task.Delay(delay);
                yield return item;
            }
        }

        /// <summary>
        /// Starts all inner IAsyncEnumerable and returns items from all of them in order in which they come.
        /// </summary>
        public static async IAsyncEnumerable<TItem> SelectManyAsync<TItem>(
            this IEnumerable<IAsyncEnumerable<TItem>> sources,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var enumerators = sources.Select(x => x.GetAsyncEnumerator(cancellationToken)).ToList();
            var runningTasks = new List<Task<(IAsyncEnumerator<TItem>, bool, Exception)>>();

            try
            {
                foreach (var enumerator in enumerators)
                    runningTasks.Add(MoveNextWrapped(enumerator, cancellationToken));

                while (runningTasks.Any())
                {
                    var finishedTask = await Task.WhenAny(runningTasks);
                    runningTasks.Remove(finishedTask);

                    var (enumerator, hasItem, ex) = await finishedTask;

                    if (ex != null)
                    {
                        // Optionally log or handle the exception per stream
                        continue;
                    }

                    if (hasItem)
                    {
                        yield return enumerator.Current;
                        runningTasks.Add(MoveNextWrapped(enumerator, cancellationToken));
                    }
                }
            }
            finally
            {
                foreach (var asyncEnumerator in enumerators)
                {
                    await asyncEnumerator.DisposeAsync();
                }
            }
        }

        /// <summary>
        /// Helper method that returns Task with tuple of IAsyncEnumerable and it's result of MoveNextAsync.
        /// </summary>
        private static async Task<(IAsyncEnumerator<TItem>, bool, Exception)> MoveNextWrapped<TItem>(
            IAsyncEnumerator<TItem> enumerator,
            CancellationToken cancellationToken)
        {
            try
            {
                var hasItem = await enumerator.MoveNextAsync();
                return (enumerator, hasItem, null);
            }
            catch (Exception ex)
            {
                return (enumerator, false, ex);
            }
        }
    }
}
