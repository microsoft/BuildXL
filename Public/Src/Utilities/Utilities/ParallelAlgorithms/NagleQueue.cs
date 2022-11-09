// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;

#nullable enable

namespace BuildXL.Utilities.ParallelAlgorithms
{
    /// <summary>
    /// Interface for batch processing items based on time or a number of items.
    /// </summary>
    public interface INagleQueue<T> : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Suspends processing data until the resulting object is disposed.
        /// </summary>
        IDisposable Suspend();

        /// <summary>
        /// Starts the processing.
        /// </summary>
        void Start();

        /// <summary>
        /// Adds an <paramref name="item"/> for asynchronous processing.
        /// </summary>
        void Enqueue(T item);

        /// <summary>
        /// Adds <paramref name="items"/> for asynchronous processing.
        /// </summary>
        void EnqueueAll(IEnumerable<T> items);
    }

    /// <summary>
    /// Factory for creating <see cref="NagleQueue{T}"/>.
    /// </summary>
    public static class NagleQueue
    {
        /// <summary>
        /// Creates a fully functioning nagle queue.
        /// </summary>
        public static NagleQueue<T> Create<T>(Func<List<T>, Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            return NagleQueue<T>.Create(processBatch, maxDegreeOfParallelism, interval, batchSize);
        }
    }

    /// <summary>
    /// Nagling queue for processing data in batches based on the size or a time interval.
    /// </summary>
    public class NagleQueue<T> : INagleQueue<T>
    {
        private bool m_disposed;
        private readonly TimeSpan m_timerInterval;

        private readonly int m_batchSize;
        private readonly ObjectPool<List<T>> m_itemListPool;

        private readonly ActionBlockSlim<List<T>> m_actionBlock;

        private List<T> m_items;
        private readonly object m_itemsLock = new object();
        private readonly Timer m_intervalTimer;

        private bool m_eventsSuspended;
        private ConcurrentQueue<T>? m_suspendedEvents;

        /// <summary>
        /// Gets the batch size of the nagle queue.
        /// </summary>
        public int BatchSize => m_batchSize;

        /// <nodoc />
        protected NagleQueue(Func<List<T>, Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            m_timerInterval = interval;
            m_batchSize = batchSize;
            m_itemListPool = new ObjectPool<List<T>>(() => new List<T>(capacity: batchSize), list => list.Clear());

            m_actionBlock = ActionBlockSlim.CreateWithAsyncAction<List<T>>(
                degreeOfParallelism: maxDegreeOfParallelism,
                async items =>
                {
                    try
                    {
                        await processBatch(items);
                    }
                    finally
                    {
                        m_itemListPool.PutInstance(items);
                    }
                });

            // The timer is essentially off until the Start method is called.
            m_intervalTimer = new Timer(SendIncompleteBatch, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            m_items = m_itemListPool.GetInstance().Instance;
        }

        /// <summary>
        /// Creates a unstarted nagle queue which is not started until <see cref="Start()"/> is called.
        /// </summary>
        public static NagleQueue<T> CreateUnstarted(Func<List<T>, Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            if (maxDegreeOfParallelism == 1 && batchSize == 1)
            {
                return new SynchronousNagleQueue<T>(processBatch, maxDegreeOfParallelism, interval, batchSize);
            }

            return new NagleQueue<T>(processBatch, maxDegreeOfParallelism, interval, batchSize);
        }

        /// <summary>
        /// Creates a fully functioning nagle queue.
        /// </summary>
        public static NagleQueue<T> Create(Func<List<T>, Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
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

            if (m_eventsSuspended)
            {
                m_suspendedEvents!.Enqueue(item);
            }
            else
            {
                EnqueueCore(item);
            }
        }

        /// <nodoc />
        protected virtual void EnqueueCore(T item)
        {
            List<T>? itemsToProcess = null;
            lock (m_itemsLock)
            {
                m_items.Add(item);

                if (m_items.Count >= m_batchSize)
                {
                    itemsToProcess = m_items;
                    m_items = m_itemListPool.GetInstance().Instance;
                }
            }

            if (itemsToProcess != null)
            {
                // Its possible that the block is completed because the Dispose method was already called.
                // Ignoring this case here because we know that the block can't be full (its configured to be unbounded).
                m_actionBlock.Post(itemsToProcess, throwOnFullOrComplete: false);
            }
        }

        /// <inheritdoc />
        public void EnqueueAll(IEnumerable<T> items)
        {
            Contract.Requires(items != null);

            ThrowIfDisposed();

            lock (m_itemsLock)
            {
                foreach (var item in items)
                {
                    m_items.Add(item);

                    if (m_items.Count >= m_batchSize)
                    {
                        m_actionBlock.Post(m_items);
                        m_items = m_itemListPool.GetInstance().Instance;
                    }
                }
            }
        }

        /// <summary>
        /// Suspends the processing of new elements and returns a <see cref="IDisposable"/> instance that will resume the processing on <see cref="IDisposable.Dispose"/> call.
        /// </summary>
        public IDisposable Suspend()
        {
            // Intentionally not checking m_disposed flag here because
            // it is possible to call this method when the disposal is already started.
            // In this case we still return ResumeBlockDisposable that will do nothing at the end.

            m_suspendedEvents ??= new ConcurrentQueue<T>();
            m_eventsSuspended = true;
            return new DisposeAction<NagleQueue<T>>(this, @this => @this.Resume());
        }

        private void Resume()
        {
            m_eventsSuspended = false;
            var suspendedEvents = m_suspendedEvents;
            if (suspendedEvents == null)
            {
                return;
            }

            // We should not be processing suspended events if Dispose method was already called.
            while (!m_eventsSuspended && !m_disposed)
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
            lock (m_itemsLock)
            {
                itemsToProcess = m_items;
                m_items = m_itemListPool.GetInstance().Instance;
            }

            if (itemsToProcess.Count != 0)
            {
                m_actionBlock.Post(itemsToProcess);
            }
        }

        private void ResetTimer()
        {
            // The callback method can be called even when the m_intervalTimer.Dispose method is called.
            // We do our best and not change the timer if the whole instance is disposed, but
            // the race is still possible so to avoid potential crashes we still have to swallow ObjectDisposedException here.
            if (!m_disposed)
            {
                try { m_intervalTimer.Change(m_timerInterval, Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException) { }
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;

            // Stopping the timer to avoid triggering it when the items are being processed.
            m_intervalTimer.Dispose();

            TriggerBatch();
            m_actionBlock.Complete(propagateExceptionsFromCallback: true);

            await m_actionBlock.Completion;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

        /// <nodoc />
        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(NagleQueue<T>));
            }
        }

        internal class SynchronousNagleQueue<T2> : NagleQueue<T2>
        {
            private readonly Func<List<T2>, Task> m_processBatch;

            internal SynchronousNagleQueue(Func<List<T2>, Task> processBatch, int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
                : base(processBatch, maxDegreeOfParallelism, interval, batchSize)
            {
                m_processBatch = processBatch;
            }

            protected override void EnqueueCore(T2 item)
            {
                m_processBatch(new List<T2>() { item }).GetAwaiter().GetResult();
            }
        }
    }
}
