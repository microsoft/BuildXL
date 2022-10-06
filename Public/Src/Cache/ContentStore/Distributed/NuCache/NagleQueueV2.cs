// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <nodoc />
    public static class NagleQueueFactory
    {
        /// <summary>
        /// Gets or sets a global flag that controls which version of <see cref="INagleQueue{T}"/> to use.
        /// </summary>
        public static bool UseV2ByDefault = false;

        /// <nodoc />
        public static INagleQueue<T> Create<T>(Func<IReadOnlyList<T>, Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            return UseV2ByDefault
                ? NagleQueue<T>.Create(items => processBatch(items), maxDegreeOfParallelism, interval, batchSize)
                : NagleQueueV2<T>.Create(items => processBatch(items), maxDegreeOfParallelism, interval, batchSize);
        }

        /// <nodoc />
        public static INagleQueue<T> CreateUnstarted<T>(Func<IReadOnlyList<T>, Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            return UseV2ByDefault
                ? NagleQueue<T>.CreateUnstarted(items => processBatch(items), maxDegreeOfParallelism, interval, batchSize)
                : NagleQueueV2<T>.CreateUnstarted(items => processBatch(items), maxDegreeOfParallelism, interval, batchSize);
        }
    }

    /// <summary>
    /// A new version of nagle queue that is not based on TPL dataflow.
    /// </summary>
    public class NagleQueueV2<T> : INagleQueue<T>
    {
        private bool _disposed;
        private readonly TimeSpan _timerInterval;

        private readonly int _batchSize;
        private readonly ObjectPool<List<T>> _itemListPool;

        private readonly ActionBlockSlim<List<T>> _actionBlock;

        private List<T> _items;
        private readonly object _itemsLock = new object();
        private readonly Timer _intervalTimer;

        private bool _eventsSuspended;
        private ConcurrentQueue<T>? _suspendedEvents;

        /// <nodoc />
        private NagleQueueV2(Func<List<T>, Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            _timerInterval = interval;
            _batchSize = batchSize;
            _itemListPool = new ObjectPool<List<T>>(() => new List<T>(capacity: batchSize), list => list.Clear());

            _actionBlock = ActionBlockSlim.CreateWithAsyncAction<List<T>>(
                degreeOfParallelism: maxDegreeOfParallelism,
                async items =>
                {
                    try
                    {
                        await processBatch(items);
                    }
                    finally
                    {
                        _itemListPool.PutInstance(items);
                    }
                });

            // The timer is essentially off until the Start method is called.
            _intervalTimer = new Timer(SendIncompleteBatch, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _items = _itemListPool.GetInstance().Instance;
        }

        /// <summary>
        /// Creates a unstarted nagle queue which is not started until <see cref="Start()"/> is called.
        /// </summary>
        public static NagleQueueV2<T> CreateUnstarted(Func<List<T>, Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            if (maxDegreeOfParallelism == 1 && batchSize == 1)
            {
                return new SynchronousNagleQueueV2<T>(processBatch, maxDegreeOfParallelism, interval, batchSize);
            }

            return new NagleQueueV2<T>(processBatch, maxDegreeOfParallelism, interval, batchSize);
        }

        /// <summary>
        /// Creates a fully functioning nagle queue.
        /// </summary>
        public static NagleQueueV2<T> Create(Func<List<T>, Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            var queue = CreateUnstarted(processBatch, maxDegreeOfParallelism, interval, batchSize);
            queue.Start();
            return queue;
        }

        /// <inheritdoc />
        public void Start()
        {
            ThrowIfDisposed();

            ResetTimer();
        }

        /// <inheritdoc />
        public void Enqueue(T item)
        {
            ThrowIfDisposed();

            if (_eventsSuspended)
            {
                _suspendedEvents!.Enqueue(item);
            }
            else
            {
                EnqueueCore(item);
            }
        }

        protected virtual void EnqueueCore(T item)
        {
            List<T>? itemsToProcess = null;
            lock (_itemsLock)
            {
                _items.Add(item);

                if (_items.Count >= _batchSize)
                {
                    itemsToProcess = _items;
                    _items = _itemListPool.GetInstance().Instance;
                }
            }

            if (itemsToProcess != null)
            {
                // Its possible that the block is completed because the Dispose method was already called.
                // Ignoring this case here because we know that the block can't be full (its configured to be unbounded).
                _actionBlock.Post(itemsToProcess, throwOnFullOrComplete: false);
            }
        }

        /// <inheritdoc />
        public void EnqueueAll(IEnumerable<T> items)
        {
            Contract.Requires(items != null);

            ThrowIfDisposed();

            lock (_itemsLock)
            {
                foreach (var item in items)
                {
                    _items.Add(item);

                    if (_items.Count >= _batchSize)
                    {
                        _actionBlock.Post(_items);
                        _items = _itemListPool.GetInstance().Instance;
                    }
                }
            }
        }

        /// <summary>
        /// Suspends the processing of new elements and returns a <see cref="IDisposable"/> instance that will resume the processing on <see cref="IDisposable.Dispose"/> call.
        /// </summary>
        public IDisposable Suspend()
        {
            // Intentionally not checking _disposed flag here because
            // it is possible to call this method when the disposal is already started.
            // In this case we still return ResumeBlockDisposable that will do nothing at the end.

            _suspendedEvents ??= new ConcurrentQueue<T>();
            _eventsSuspended = true;
            return new DisposeAction<NagleQueueV2<T>>(this, @this => @this.Resume());
        }

        private void Resume()
        {
            _eventsSuspended = false;
            var suspendedEvents = _suspendedEvents;
            if (suspendedEvents == null)
            {
                return;
            }

            // We should not be processing suspended events if Dispose method was already called.
            while (!_eventsSuspended && !_disposed)
            {
                if (suspendedEvents.TryDequeue(out var item))
                {
                    EnqueueCore(item);
                }
                else
                {
                    break;
                }
            }
        }

        private void SendIncompleteBatch(object? obj)
        {
            // Even though the callback (ProcessBatchAsync) resets the timer,
            // we still need to reset the timer in the case when the batch is empty now.
            // in this case _batchBlock.TriggerBatch won't trigger the callback at all.
            ResetTimer();

            TriggerBatch();
        }

        private void TriggerBatch()
        {
            List<T>? itemsToProcess;
            lock (_itemsLock)
            {
                itemsToProcess = _items;
                _items = _itemListPool.GetInstance().Instance;
            }

            if (itemsToProcess.Count != 0)
            {
                _actionBlock.Post(itemsToProcess);
            }
        }

        private void ResetTimer()
        {
            // The callback method can be called even when the _intervalTimer.Dispose method is called.
            // We do our best and not change the timer if the whole instance is disposed, but
            // the race is still possible so to avoid potential crashes we still have to swallow ObjectDisposedException here.
            if (!_disposed)
            {
                try { _intervalTimer.Change(_timerInterval, Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException) { }
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Stopping the timer to avoid triggering it when the items are being processed.
            _intervalTimer.Dispose();

            TriggerBatch();
            _actionBlock.Complete(propagateExceptionsFromCallback: true);

            await _actionBlock.Completion;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

        /// <nodoc />
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NagleQueue<T>));
            }
        }

        internal class SynchronousNagleQueueV2<T2> : NagleQueueV2<T2>
        {
            private readonly Func<List<T2>, Task> _processBatch;

            internal SynchronousNagleQueueV2(Func<List<T2>, Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
                : base(processBatch, maxDegreeOfParallelism, interval, batchSize)
            {
                _processBatch = processBatch;
            }

            protected override void EnqueueCore(T2 item)
            {
                _processBatch(new List<T2>() { item }).GetAwaiter().GetResult();
            }
        }
    }
}
