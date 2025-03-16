using System.Collections.Generic;
using System.Linq;
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
        public static async IAsyncEnumerable<TItem> SelectManyAsync<TItem>(this IEnumerable<IAsyncEnumerable<TItem>> source)
        {
            // Get enumerators from all inner IAsyncEnumerable
            var enumerators = source.Select(x => x.GetAsyncEnumerator()).ToList();
            var runningTasks = new List<Task<(IAsyncEnumerator<TItem>, bool)>>();

            try
            {
                // Start all enumerators
                foreach (var enumerator in enumerators)
                    runningTasks.Add(MoveNextWrapped(enumerator));

                // Process items as they arrive
                while (runningTasks.Any())
                {
                    var finishedTask = await Task.WhenAny(runningTasks);
                    runningTasks.Remove(finishedTask);

                    var (enumerator, hasItem) = await finishedTask;

                    if (hasItem)
                    {
                        yield return enumerator.Current;
                        runningTasks.Add(MoveNextWrapped(enumerator));
                    }
                }
            }
            finally
            {
                // Ensure enumerators are disposed
                foreach (var asyncEnumerator in enumerators)
                {
                    await asyncEnumerator.DisposeAsync();
                }
            }
        }

        /// <summary>
        /// Helper method that returns Task with tuple of IAsyncEnumerable and it's result of MoveNextAsync.
        /// </summary>
        private static async Task<(IAsyncEnumerator<TItem>, bool)> MoveNextWrapped<TItem>(IAsyncEnumerator<TItem> enumerator)
        {
            return (enumerator, await enumerator.MoveNextAsync());
        }
    }
}
