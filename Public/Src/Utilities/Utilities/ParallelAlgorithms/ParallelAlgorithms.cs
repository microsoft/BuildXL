// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;

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

            // The channel is unbounded, because we don't expect it to grow too much.
            var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions() { SingleReader = false, SingleWriter = false, });
            
            // Adding all the items to the channel.
            foreach (var item in items)
            {
                channel.Writer.TryWrite(item);
            }

            // Using the number of pending items to understand when all the items were processed.
            // Each processing callback can add more items to the processing queue, but if the number of pending
            // items drops to 0, then it means that all the items were processed and the method is done.
            int pending = items.Length;

            ScheduleItem<T> scheduleItem = item =>
            {
                // Need to increment the number of pending items before writing an item to the channel to avoid
                // shutting down the processing due to a lack of item to process.
                Interlocked.Increment(ref pending);
                bool result = channel.Writer.TryWrite(item);
                Contract.Assert(result, "Can't add an item to the channel.");
            };

            var tasks = new Task[degreeOfParallelism];
            for (int i = 0; i < degreeOfParallelism; i++)
            {
                tasks[i] = Task.Run(
                    async () =>
                    {
                        // Using 'WaitToReadOrCanceledAsync' instead of 'channel.Writer.WaitToReadAsync',
                        // because the latter throws 'OperationCanceledException' if the token is triggered,
                        // but we just want to exit the loop in this case.
                        while (await channel.WaitToReadOrCanceledAsync(cancellationToken).ConfigureAwait(false))
                        {
                            while (!cancellationToken.IsCancellationRequested && channel.Reader.TryRead(out var item))
                            {
                                try
                                {
                                    await action(scheduleItem, item);
                                }
                                finally
                                {
                                    if (Interlocked.Decrement(ref pending) == 0)
                                    {
                                        // No more items to process. We can complete the channel to break this loop for all the processing tasks.
                                        channel.Writer.Complete();
                                    }
                                }
                            }
                        }
                    },
                    cancellationToken);
            }

            await TaskUtilities.SafeWhenAll(tasks);
        }

        /// <summary>
        /// Periodically calls the <paramref name="predicate"/> callback until it returns true or <paramref name="timeout"/> occurs.
        /// </summary>
        public static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan pollInterval, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);

            while (true)
            {
                if (predicate())
                {
                    return true;
                }

                try
                {
                    await Task.Delay(pollInterval, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Updates the <paramref name="location"/> with <paramref name="value"/> if the <paramref name="value"/> is greater then the original value of <paramref name="location"/>.
        /// </summary>
        public static int InterlockedMax(ref int location, int value)
        {
            int initialValue, newValue;
            do
            {
                initialValue = location;
                newValue = Math.Max(initialValue, value);
            }
            while (Interlocked.CompareExchange(ref location, newValue, initialValue) != initialValue);
            return initialValue;
        }
    }
}
