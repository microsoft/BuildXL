// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Extensions;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Nagling queue for processing data in batches based on the size or a time interval.
    /// </summary>
    public class NagleQueue<T> : IDisposable
    {
        private bool _disposed;
        private Func<T[], Task> _processBatch;
        private readonly TimeSpan _timerInterval;
        private readonly BatchBlock<T> _batchBlock;
        private readonly ActionBlock<T[]> _actionBlock;
        private readonly Timer _intervalTimer;

        // Move into SuspendableNagleQueue?
        private bool _eventsSuspended;
        private readonly ConcurrentQueue<T> _suspendedEvents = new ConcurrentQueue<T>();

        /// <summary>
        /// Creates an instance of a nagle queue.
        /// </summary>
        protected NagleQueue(int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            Contract.Requires(maxDegreeOfParallelism > 0);
            Contract.Requires(batchSize > 0);

            _timerInterval = interval;

            _batchBlock = new BatchBlock<T>(batchSize);
            _actionBlock = new ActionBlock<T[]>(ProcessBatchAsync, new ExecutionDataflowBlockOptions()
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            });

            _intervalTimer = new Timer(SendIncompleteBatch, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Creates an instance of a nagle queue.
        /// </summary>
        private NagleQueue(Func<T[], Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
            : this(maxDegreeOfParallelism, interval, batchSize)
        {
            Contract.Requires(processBatch != null);
        }

        /// <summary>
        /// Creates a unstarted nagle queue which is not started until <see cref="Start(Func{T[], Task})"/> is called.
        /// </summary>
        public static NagleQueue<T> CreateUnstarted(int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            if (maxDegreeOfParallelism == 1 && batchSize == 1)
            {
                return new SynchronousNagleQueue<T>(maxDegreeOfParallelism, interval, batchSize);
            }

            return new NagleQueue<T>(maxDegreeOfParallelism, interval, batchSize);
        }

        /// <summary>
        /// Creates a fully functioning nagle queue.
        /// </summary>
        public static NagleQueue<T> Create(Func<T[], Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            var queue = CreateUnstarted(maxDegreeOfParallelism, interval, batchSize);
            queue.Start(processBatch);
            return queue;
        }

        /// <summary>
        /// Suspends the processing of new elements and returns a <see cref="IDisposable"/> instance that will resume the processing on <see cref="IDisposable.Dispose"/> call.
        /// </summary>
        public IDisposable Suspend()
        {
            // Intentionally not checking _disposed flag here because
            // it is possible to call this method when the disposal is already started.
            // In this case we still return ResumeBlockDisposable that will do nothing at the end.

            _eventsSuspended = true;
            return new ResumeBlockDisposable(this);
        }

        private void Resume()
        {
            _eventsSuspended = false;

            // We should not be processing suspended events if Dispose method was already called.
            while (!_eventsSuspended && !_disposed)
            {
                if (_suspendedEvents.TryDequeue(out var item))
                {
                    Enqueue(item);
                }
                else
                {
                    break;
                }
            }
        }

        private class ResumeBlockDisposable : IDisposable
        {
            private readonly NagleQueue<T> _nagleQueue;

            public ResumeBlockDisposable(NagleQueue<T> nagleQueue) => _nagleQueue = nagleQueue;

            public void Dispose() => _nagleQueue.Resume();
        }

        /// <summary>
        /// Starts the unstarted nagle queue created with <see cref="CreateUnstarted"/> using the given batch processing operation.
        /// </summary>
        public virtual void Start(Func<T[], Task> processBatch)
        {
            Contract.Requires(processBatch != null);
            ThrowIfDisposed();

            var originalValue = Interlocked.CompareExchange(ref _processBatch, processBatch, null);
            Contract.Assert(originalValue == null, "Nagle queue already started");

            _batchBlock.LinkTo(_actionBlock);
            ResetTimer();
        }

        /// <summary>
        /// Add an item for asynchronous processing.
        /// </summary>
        public void Enqueue(T item)
        {
            ThrowIfDisposed();
            if (_eventsSuspended)
            {
                _suspendedEvents.Enqueue(item);
            }
            else
            {
                EnqueueCore(item);
            }
        }

        /// <nodoc />
        protected virtual void EnqueueCore(T item)
        {
            var result = _batchBlock.Post(item);
            Contract.Assert(result);
        }

        /// <summary>
        /// Adds items for asynchronous processing.
        /// </summary>
        public void EnqueueAll(IReadOnlyList<T> items)
        {
            Contract.Requires(items != null);

            ThrowIfDisposed();
            _batchBlock.PostAll(items);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _batchBlock.TriggerBatch();
            _batchBlock.Complete();

            _batchBlock.Completion.GetAwaiter().GetResult();

            _actionBlock.Complete();
            _actionBlock.Completion.GetAwaiter().GetResult();

            _intervalTimer.Dispose();
        }

        private async Task ProcessBatchAsync(T[] batch)
        {
            try
            {
                SuspendTimer();
                await _processBatch(batch);
            }
            finally
            {
                ResetTimer();
            }
        }

        private void SendIncompleteBatch(object obj)
        {
            // Even though the callback (ProcessBatchAsync) resets the timer,
            // we still need to reset the timer in the case when the batch is empty now.
            // in this case _batchBlock.TriggerBatch won't trigger the callback at all.
            ResetTimer();

            _batchBlock.TriggerBatch();
        }

        private void SuspendTimer()
        {
            _intervalTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        private void ResetTimer()
        {
            _intervalTimer.Change(_timerInterval, Timeout.InfiniteTimeSpan);
        }

        /// <nodoc />
        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NagleQueue<T>));
            }
        }
    }
}
