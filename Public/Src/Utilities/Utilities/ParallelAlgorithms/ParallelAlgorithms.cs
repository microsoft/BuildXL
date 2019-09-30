// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities.ParallelAlgorithms
{
    /// <summary>
    ///     Collections of parallel algorithms.
    /// </summary>
    /// <remarks>
    ///     Taken/adapted from: https://code.msdn.microsoft.com/samples-for-parallel-b4b76364
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1724:TypeNamesShouldNotMatchNamespaces")]
    public static class ParallelAlgorithms
    {
        /// <summary>Processes data in parallel, allowing the processing function to add more data to be processed.</summary>
        /// <typeparam name="T">Specifies the type of data being processed.</typeparam>
        /// <param name="initialValues">The initial set of data to be processed.</param>
        /// <param name="body">The operation to execute for each value.</param>
        public static void WhileNotEmpty<T>(IEnumerable<T> initialValues, Action<T, Action<T>> body)
        {
            WhileNotEmpty(new ParallelOptions(), initialValues, body);
        }

        /// <summary>Processes data in parallel, allowing the processing function to add more data to be processed.</summary>
        /// <typeparam name="T">Specifies the type of data being processed.</typeparam>
        /// <param name="parallelOptions">A ParallelOptions instance that configures the behavior of this operation.</param>
        /// <param name="initialValues">The initial set of data to be processed.</param>
        /// <param name="body">The operation to execute for each value.</param>
        public static void WhileNotEmpty<T>(
            ParallelOptions parallelOptions,
            IEnumerable<T> initialValues,
            Action<T, Action<T>> body)
        {
            // Create two lists to alternate between as source and destination.
            var lists = new[] { new ConcurrentStack<T>(initialValues), new ConcurrentStack<T>() };

            // Iterate until no more items to be processed.
            for (var i = 0; !parallelOptions.CancellationToken.IsCancellationRequested; i++)
            {
                // Determine which list is the source and which is the destination.
                var fromIndex = i % 2;
                var from = lists[fromIndex];
                var to = lists[fromIndex ^ 1];

                // If the source is empty, we're done.
                if (from.IsEmpty)
                {
                    break;
                }

                // Otherwise, process all source items in parallel, adding any new items into the destination.
                Action<T> adder = to.Push;
                Parallel.ForEach(from, parallelOptions, e => body(e, adder));

                // Clear out the source as it's now been fully processed.
                from.Clear();
            }
        }

        /// <summary>
        /// Process the <paramref name="source"/> in parallel by calling <paramref name="mapFn"/> for each element.
        /// </summary>
        public static IReadOnlyList<TResult> ParallelSelect<TElement, TResult>(IList<TElement> source, Func<TElement, TResult> mapFn, int degreeOfParallelism, CancellationToken cancellationToken)
        {
            var results = new TResult[source.Count];

            Parallel.ForEach(
                source.Select((elem, idx) => Tuple.Create(elem, idx)).ToList(),
                new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism, CancellationToken = cancellationToken },
                body: (tuple) =>
                {
                    results[tuple.Item2] = mapFn(tuple.Item1);
                });

            return results;
        }

        /// <summary>
        /// Delegate for adding more items for processing by <see cref="ParallelAlgorithms.WhenDoneAsync{T}"/>.
        /// </summary>
        public delegate void ScheduleItem<T>(T item);

        /// <summary>
        /// Sync wrapper around <see cref="WhenDoneAsync{T}(int, CancellationToken, Func{ScheduleItem{T}, T, Task}, T[])"/>
        /// </summary>
        public static void WhenDone<T>(int degreeOfParallelism, CancellationToken cancellationToken, Action<ScheduleItem<T>, T> action, params T[] items)
        {
            var task = WhenDoneAsync<T>(
                degreeOfParallelism,
                cancellationToken,
                (scheduleItem, item) =>
                {
                    action(scheduleItem, item);
                    return Task.FromResult(false);
                },
                items);

            task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Process the <paramref name="items"/> in parallel by calling <paramref name="action"/> on each element.
        /// Each call back can schedule more work by calling a <see cref="ScheduleItem{T}"/> delegate.
        /// </summary>
        /// <remarks>
        /// Unlike <seealso cref="WhileNotEmpty{T}(System.Collections.Generic.IEnumerable{T},System.Action{T,System.Action{T}})"/> 
        /// this method is suitable for producing-consuming scnarios.
        /// The callback function can discover new work and can call a given call back to schedule more work.
        /// </remarks>
        public static async Task WhenDoneAsync<T>(int degreeOfParallelism, CancellationToken cancellationToken, Func<ScheduleItem<T>, T, Task> action, params T[] items)
        {
            if (items.Length == 0)
            {
                return;
            }

            var queue = new ConcurrentQueue<T>(items);
            int pending = queue.Count;

            // Semaphore count should equal the queue count to ensure completion (except on completion
            // when release degreeOfParallelism count)
            var semaphore = new SemaphoreSlim(pending, int.MaxValue);

            // The number of pending items is increased via 'scheduleItem' delegate and
            // decreased when the call back is finished.
            // This is very important, because decreasing the number of items too early can lead
            // to a race condition.
            ScheduleItem<T> scheduleItem = item =>
            {
                Interlocked.Increment(ref pending);

                // NOTE: Enqueue MUST happen before releasing the semaphore
                // to ensure WaitAsync below never returns when there is not
                // a corresponding item in the queue to be dequeued. The only
                // exception is on completion of all items.
                queue.Enqueue(item);
                semaphore.Release();
            };

            var tasks = new Task[degreeOfParallelism];
            for (int i = 0; i < degreeOfParallelism; i++)
            {
                tasks[i] = Task.Run(
                    async () =>
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            await semaphore.WaitAsync(cancellationToken);

                            try
                            {
                                if (queue.TryDequeue(out var item))
                                {
                                    await action(scheduleItem, item);
                                }
                                else
                                {
                                    return;
                                }
                            }
                            finally
                            {
                                if (Interlocked.Decrement(ref pending) == 0)
                                {
                                    // Ensure all tasks are unblocked and can gracefully
                                    // finish since there are at most degreeOfParallelism - 1 tasks
                                    // waiting at this point
                                    semaphore.Release(degreeOfParallelism);
                                }
                            }
                        }
                    },
                    cancellationToken);
            }

            await Task.WhenAll(tasks);
        }
    }
}
