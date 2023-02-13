// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Utilities.Tasks;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// Wrapper for SemaphoreSlim which has deterministic order.
    /// </summary>
    internal class OrderedSemaphore
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly SemaphoreSlim _collectionCount = new SemaphoreSlim(initialCount: 0);
        private readonly IProducerConsumerCollection<TaskSourceSlim<bool>> _collection;
        private readonly SemaphoreOrder _order;

        public int ConcurrencyLimit { get; }

        /// <nodoc />
        public OrderedSemaphore(int concurrencyLimit, SemaphoreOrder order, Context context)
        {
            ConcurrencyLimit = concurrencyLimit;
            _semaphore = new SemaphoreSlim(initialCount: concurrencyLimit);
            _order = order;
            _collection = order == SemaphoreOrder.FIFO
                ? (IProducerConsumerCollection<TaskSourceSlim<bool>>)new ConcurrentQueue<TaskSourceSlim<bool>>()
                : new ConcurrentStack<TaskSourceSlim<bool>>();

            // Non-deterministic means to skip this as a wrapper and use the underlying Semaphore, so a main loop is not needed.
            if (order != SemaphoreOrder.NonDeterministic)
            {
                Task.Run(MainLoopAsync).FireAndForget(context, failureSeverity: Severity.Fatal, failFast: true);
            }
        }

        /// <summary>
        /// Asynchronously waits to enter the <see cref="OrderedSemaphore"/>, using a <see
        /// cref="TimeSpan"/> to measure the time interval.
        /// </summary>
        /// <param name="timeout">
        /// A <see cref="TimeSpan"/> that represents the maximum time to wait.
        /// </param>
        /// <param name="token">
        /// The <see cref="CancellationToken"/> token to observe.
        /// </param>
        /// <returns>
        /// A task that will complete with a result of true if the current thread successfully entered
        /// the <see cref="OrderedSemaphore"/>, otherwise with a result of false. Timing out results in false.
        /// </returns>
        /// <exception cref="TaskCanceledException">
        /// The token was cancelled.
        /// </exception>
        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken token)
        {
            if (_order == SemaphoreOrder.NonDeterministic)
            {
                // Forcing the same exception to be propagated for non-deterministic case as well.
                // SemaphoreSlim.WaitAsync throws OperationCanceledException but this method
                // should throw TaskCanceledException instead.
                try
                {
                    return await _semaphore.WaitAsync(timeout, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw new TaskCanceledException();
                }
            }

            var item = TaskSourceSlim.Create<bool>();

            var added = _collection.TryAdd(item);
            Contract.Assert(added, "Since collection is unbounded, it should always be able to add more items.");

            // Increment collection count
            _collectionCount.Release();

            using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var delay = Task.Delay(timeout, delayCancellation.Token);
            var task = item.Task;

            if (delay == await Task.WhenAny(delay, task))
            {
                if (token.IsCancellationRequested)
                {
                    // Was unable to complete because the timeout was actually cancelled via the token passed in to WaitAsync,
                    // not because we actually timed out.
                    item.TrySetCanceled();
                }
                else
                {
                    // Timed out.
                    item.TrySetResult(false);
                }
            }
            else
            {
                // Clean up the delay task.
                delayCancellation.Cancel();
            }

            return await task;
        }

        /// <nodoc />
        public int Release() => _semaphore.Release();

        private async Task MainLoopAsync()
        {
            while (true)
            {
                // We don't want to use a BlockingCollection because it blocks the thread, so wait for count to be at least one.
                await _collectionCount.WaitAsync();

                await _semaphore.WaitAsync();

                var tookFromCollection = _collection.TryTake(out var item);
                Contract.Assert(tookFromCollection, "Should always be able to pull an item from the collection");

                if (!item.TrySetResult(true))
                {
                    // Only release the semaphore if the item was cancelled or timed out, since the caller won't do it for us.
                    _semaphore.Release();
                }
            }
        }

        /// <summary>
        /// Convenience method to allow tracing the time it takes to Wait a semaphore 
        /// </summary>
        public async Task<T> GatedOperationAsync<T>(
            Func<(TimeSpan semaphoreWaitTime, int semaphoreCount), Task<T>> operation,
            CancellationToken token = default,
            TimeSpan? timeout = null,
            Func<TimeSpan, T>? onTimeout = null)
        {
            var sw = Stopwatch.StartNew();
            var acquired = await WaitAsync(timeout ?? Timeout.InfiniteTimeSpan, token);

            if (!acquired)
            {
                if (onTimeout is null)
                {
                    throw new TimeoutException($"IO gate timed out after `{sw.Elapsed}` (timeout is `{timeout}`)");
                }
                else
                {
                    return onTimeout(sw.Elapsed);
                }
            }

            try
            {
                var currentCount = _semaphore.CurrentCount;
                return await operation((sw.Elapsed, currentCount));
            }
            finally
            {
                Release();
            }
        }
    }
}
