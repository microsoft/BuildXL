// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;

#nullable enable

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// A queue that batches requests for processing. This is intended for batching requests to a remote service by key.
    /// </summary>
    /// <remarks>
    /// The keys stay around FOREVER. Therefore, it's important that the set of keys be bounded.
    /// </remarks>
    public class BatchingQueue<TKey, TRequest, TResult> : IDisposable, IAsyncDisposable
        where TKey : notnull
    {
        private readonly Func<TKey, IReadOnlyList<Item>, CancellationToken, Task> _process;

        private readonly TimeSpan _interval;
        private readonly int _maximumBatchSize;
        private readonly int _maxDegreeOfParallelism;

        /// <summary>
        /// An item that's part of a batch.
        /// </summary>
        public record Item
        {
            private TaskSourceSlim<TResult> TaskSourceSlim { get; } = new();

            /// <summary>
            /// The task that represents the completion of the request as part of a batch.
            /// </summary>
            public Task<TResult> Task => TaskSourceSlim.Task;

            /// <summary>
            /// The request this item represents.
            /// </summary>
            public required TRequest Value { get; init; }

            /// <summary>
            /// Mark the promise as succeeded.
            /// </summary>
            /// <remarks>
            /// This unblocks the any task that's waiting on this item to be processed as part of a given batch.
            /// </remarks>
            public void Succeed(TResult result)
            {
                if (Task.IsCompleted)
                {
                    return;
                }

                TaskSourceSlim.TrySetResult(result);
            }

            /// <summary>
            /// Mark the promise as failed.
            /// </summary>
            /// <remarks>
            /// This unblocks the any task that's waiting on this item to be processed as part of a given batch.
            /// </remarks>
            public void Fail(Exception exception)
            {
                if (Task.IsCompleted)
                {
                    return;
                }

                TaskSourceSlim.TrySetException(exception);
            }

            /// <summary>
            /// Mark the promise as cancelled.
            /// </summary>
            /// <remarks>
            /// This unblocks the any task that's waiting on this item to be processed as part of a given batch.
            /// </remarks>
            public void Cancel()
            {
                if (Task.IsCompleted)
                {
                    return;
                }

                TaskSourceSlim.TrySetCanceled();
            }
        }

        private record Batch
        {
            /// <nodoc />
            public required TKey Key { get; init; }

            /// <summary>
            /// A queue of items to be processed for a given key.
            /// </summary>
            public ConcurrentQueue<Item> Items { get; } = new();

            public int Waiting = 0;
        }

        /// <summary>
        /// Set of keys that are tracked for batching.
        /// </summary>
        /// <remarks>
        /// This is periodically polled by <see cref="DispatchAsync(CancellationToken)"/> to process the batches.
        /// 
        /// Batches can also sent off to be processed by <see cref="Enqueue(TKey, TRequest)"/> when they have reached
        /// the maximum batch size.
        ///
        /// The processing of a batch happens in <see cref="PerformAsync(Batch, CancellationToken)"/>. This function
        /// will get called in parallel for different keys, and sometimes even for the same key.
        /// </remarks>
        private readonly ConcurrentDictionary<TKey, Batch> _batches = new();

        private readonly ActionBlockSlim<Batch> _processor;
        private readonly Exception _exception = new InvalidOperationException("The process batch function did not complete the promises.");
        private readonly ObjectPool<List<Item>> _pool;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Task _dispatch;

        private bool _disposed;

        public BatchingQueue(Func<TKey, IReadOnlyList<Item>, CancellationToken, Task> process, TimeSpan interval, int maxBatchSize, int maxDegreeOfParallelism)
        {
            Contract.Requires(maxBatchSize > 0);
            Contract.Requires(maxDegreeOfParallelism > 0);
            Contract.Requires(process != null);

            _process = process;

            _interval = interval;
            _maximumBatchSize = maxBatchSize;
            _maxDegreeOfParallelism = maxDegreeOfParallelism;

            _pool = new(() => new(capacity: _maximumBatchSize), list => list.Clear());
            _processor = ActionBlockSlim.CreateWithAsyncAction<Batch>(_maxDegreeOfParallelism, PerformAsync, cancellationToken: _cancellationTokenSource.Token);
            _dispatch = DispatchAsync(_cancellationTokenSource.Token);
        }

        private async Task DispatchAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    foreach (var key in _batches.Keys)
                    {
                        if (_batches.TryGetValue(key, out var batch))
                        {
                            if (batch.Items.IsEmpty)
                            {
                                continue;
                            }

                            await TryPostAsync(batch, cancellationToken);
                        }
                    }

                    await Task.Delay(_interval, cancellationToken);
                }
                catch (Exception)
                {
                    // This exception is swallowed on purpose, because it should never ever happen, but if it does it
                    // would hang the processing.
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                    continue;
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
                }
            }
        }

        private async Task PerformAsync(Batch batch, CancellationToken cancellationToken)
        {
            Contract.Assert(batch.Waiting == 1, "The waiting flag should be set to 1.");

            try
            {
                // Limit the number of iterations to ensure fairness.
                for (var iteration = 0; iteration < 5; iteration++)
                {
                    // If we have done at least one iteration and the batch is sufficiently empty, we can stop. This
                    // ensures some amount of fairness.
                    if (iteration >= 1 && batch.Items.Count < _maximumBatchSize / 4)
                    {
                        break;
                    }

                    using var items = _pool.GetInstance();

                    while (items.Instance.Count < _maximumBatchSize && batch.Items.TryDequeue(out var promise))
                    {
                        items.Instance.Add(promise);
                    }

                    try
                    {
                        await _process.Invoke(batch.Key, items.Instance, cancellationToken);

                        // Here an in the below cases, we need to make sure that we complete the promises, because tasks
                        // could hang otherwise. All of these actions (Fail / Cancel, etc) are idempotent, so it's safe to
                        // call them. Only the first call will have an effect. So, if any of the promises are not completed
                        // by the processing function, we complete them here.
                        foreach (var item in items.Instance)
                        {
                            item.Fail(_exception);
                        }
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
                    {
                        foreach (var item in items.Instance)
                        {
                            item.Cancel();
                        }
                    }
                    catch (Exception ex)
                    {
                        foreach (var item in items.Instance)
                        {
                            item.Fail(ex);
                        }
                    }
                }
            }
            finally
            {
                // Reset the waiting flag to 0, so that the batch can be posted again.
                Interlocked.Exchange(ref batch.Waiting, 0);
            }
        }

        public async Task<TResult> Enqueue(TKey key, TRequest value, CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BatchingQueue<TKey, TRequest, TResult>));
            }

            var promise = new Item { Value = value };

            var currentPromise = _batches.AddOrUpdate(key,
                addValueFactory: key =>
                {
                    var set = new Batch() { Key = key };
                    set.Items.Enqueue(promise);
                    return set;
                }, updateValueFactory: (key, current) =>
                {
                    current.Items.Enqueue(promise);
                    return current;
                });

            if (currentPromise.Items.Count >= _maximumBatchSize)
            {
                await TryPostAsync(currentPromise, cancellationToken);
            }

            return await TaskUtilities.AwaitWithCancellationAsync(promise.Task, cancellationToken);
        }

        private async Task<bool> TryPostAsync(Batch batch, CancellationToken cancellationToken)
        {
            // The CompareExchange is used to ensure the batch gets posted only once to the processor. This ensures
            // that we don't end up with multiple parallel processors looking at the same batch of items at the same
            // time.
            if (Interlocked.CompareExchange(ref batch.Waiting, 1, 0) != 0)
            {
                return false;
            }

            try
            {
                var posted = await _processor.PostAsync(batch, cancellationToken);
                Contract.Assert(posted, "Failed to post batch to processor");
                return posted;
            }
            catch
            {
                Interlocked.Exchange(ref batch.Waiting, 0);
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // Dispose managed state (managed objects)
                _cancellationTokenSource.Cancel();
                _processor.Complete(cancelPending: true);
                _pool.Clear();
                _cancellationTokenSource.Dispose();
            }

            foreach (var key in _batches.Keys)
            {
                if (_batches.TryRemove(key, out var batch))
                {
                    foreach (var promise in batch.Items)
                    {
                        promise.Cancel();
                    }
                }
            }

            _disposed = true;
        }

        public async ValueTask DisposeAsync()
        {
            // Perform async cleanup
            await DisposeAsyncCore();

            // Dispose of synchronous resources
            Dispose(disposing: false);
            GC.SuppressFinalize(this);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (_disposed)
            {
                return;
            }

#if NET8_0_OR_GREATER
            await _cancellationTokenSource.CancelAsync();
#else
            _cancellationTokenSource.Cancel();
#endif

            try
            {
                await _dispatch;
            }
            catch
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            {
                // Intentionally ignore exceptions in async disposal
            }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler

            _processor.Complete(cancelPending: true);
            try
            {
                await _processor.Completion;
            }
            catch
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            {
                // Intentionally ignore exceptions in async disposal
            }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler

            _pool.Clear();
            _cancellationTokenSource.Dispose();
        }
    }
}
