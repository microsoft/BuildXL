// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.ParallelAlgorithms;

namespace Tool.DropDaemon
{
    /// <summary>
    /// Daemon-owned FIFO execution queue for destination-specific association/upload batches.
    /// Batchers must be drained before this queue is disposed.
    /// </summary>
    public sealed class SharedUploadQueue : IDisposable
    {
        private readonly ActionBlockSlim<Func<Task>> m_queue;

        /// <summary>
        /// Maximum number of association/upload batches processed concurrently across all destinations.
        /// </summary>
        public int MaxParallelUploads { get; }

        /// <summary>
        /// Creates a shared queue with the specified maximum number of concurrently processed batches.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxParallelUploads"/> is not positive.</exception>
        public SharedUploadQueue(int maxParallelUploads)
        {
            if (maxParallelUploads <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxParallelUploads));
            }

            MaxParallelUploads = maxParallelUploads;
            m_queue = ActionBlockSlim.CreateWithAsyncAction<Func<Task>>(maxParallelUploads, work => work());
        }

        /// <summary>
        /// Creates a destination-specific batcher that submits ready batches to this shared execution queue.
        /// </summary>
        /// <typeparam name="T">The type of items accumulated into each batch.</typeparam>
        /// <param name="processBatch">
        /// Processes a batch. Its list is cleared and returned to the pool after the returned task completes,
        /// and must not be used beyond that point.
        /// </param>
        /// <param name="batchSize">Maximum number of items accumulated before submitting a batch.</param>
        /// <param name="interval">Partial-batch flush interval, or <see cref="Timeout.InfiniteTimeSpan"/> to disable timed flushing.</param>
        public Batcher<T> CreateBatcher<T>(Func<List<T>, Task> processBatch, int batchSize, TimeSpan interval)
        {
            return new Batcher<T>(m_queue, processBatch, batchSize, interval);
        }

        /// <summary>
        /// Stops accepting batches and waits for the shared execution queue to drain.
        /// Destination batchers must be drained before calling this method.
        /// </summary>
        public void Dispose()
        {
            m_queue.Complete(propagateExceptionsFromCallback: true);
            m_queue.Completion.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Accumulates one destination's files without owning an execution queue or workers.
        /// Draining flushes partial batches and waits only for this batcher's work.
        /// </summary>
        /// <typeparam name="T">The type of items accumulated into each batch.</typeparam>
        public sealed class Batcher<T> : IWorkQueue<T>
        {
            private static readonly ObjectPool<List<T>> s_itemListPool = Pools.CreateListPool<T>();

            private readonly Lock m_lock = new Lock();
            private readonly ActionBlockSlim<Func<Task>> m_queue;
            private readonly Func<List<T>, Task> m_processBatch;
            private readonly int m_batchSize;
            private readonly TimeSpan m_interval;
            private readonly Timer m_timer;
            private readonly TaskCompletionSource<bool> m_completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private List<T> m_items;
            private int m_pendingBatches;
            private bool m_closed;
            private Exception m_failure;

            /// <summary>
            /// Creates a destination batcher that submits ready batches to <paramref name="queue"/>.
            /// </summary>
            public Batcher(ActionBlockSlim<Func<Task>> queue, Func<List<T>, Task> processBatch, int batchSize, TimeSpan interval)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

                m_queue = queue;
                m_processBatch = processBatch;
                m_batchSize = batchSize;
                m_interval = interval;
                m_items = RentBatch();
                m_timer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                m_timer.Change(m_interval, Timeout.InfiniteTimeSpan);
            }

            /// <inheritdoc />
            public void Enqueue(T item)
            {
                lock (m_lock)
                {
                    if (m_closed)
                    {
                        throw new ObjectDisposedException(nameof(Batcher<T>));
                    }

                    m_items.Add(item);
                    if (m_items.Count >= m_batchSize)
                    {
                        PublishBatch();
                    }
                }
            }

            /// <inheritdoc />
            public ValueTask DisposeAsync()
            {
                lock (m_lock)
                {
                    if (!m_closed)
                    {
                        m_closed = true;
                        m_timer.Dispose();
                        try
                        {
                            PublishBatch();
                        }
                        finally
                        {
                            CompleteIfDrained();
                        }
                    }

                    return new ValueTask(m_completion.Task);
                }
            }

            /// <inheritdoc />
            public void Dispose()
            {
                DisposeAsync().GetAwaiter().GetResult();
            }

            private void Flush()
            {
                lock (m_lock)
                {
                    if (!m_closed)
                    {
                        PublishBatch();
                        m_timer.Change(m_interval, Timeout.InfiniteTimeSpan);
                    }
                }
            }

            private void PublishBatch()
            {
                if (m_items.Count == 0)
                {
                    return;
                }

                var batch = m_items;
                m_items = m_closed ? null : RentBatch();
                try
                {
                    m_queue.TryPost(async () =>
                    {
                        try
                        {
                            await m_processBatch(batch);
                        }
                        catch (Exception e)
                        {
                            // Surface failures through this batcher's drain without faulting other destinations' work.
                            lock (m_lock)
                            {
                                m_failure = m_failure == null ? e : new AggregateException(m_failure, e);
                            }
                        }
                        finally
                        {
                            lock (m_lock)
                            {
                                ReturnBatch(batch);
                                m_pendingBatches--;
                                CompleteIfDrained();
                            }
                        }
                    }, throwOnFullOrComplete: true);
                    m_pendingBatches++;
                }
                catch
                {
                    ReturnBatch(batch);
                    throw;
                }
            }

            private List<T> RentBatch()
            {
                var batch = s_itemListPool.GetInstance().Instance;
                if (batch.Capacity < m_batchSize)
                {
                    batch.Capacity = m_batchSize;
                }

                return batch;
            }

            private static void ReturnBatch(List<T> batch)
            {
                // Oversized lists can be discarded by the pool before its cleanup callback runs.
                batch.Clear();
                s_itemListPool.PutInstance(batch);
            }

            private void CompleteIfDrained()
            {
                if (m_closed && m_pendingBatches == 0)
                {
                    if (m_items != null)
                    {
                        ReturnBatch(m_items);
                        m_items = null;
                    }

                    if (m_failure != null)
                    {
                        m_completion.TrySetException(m_failure);
                    }
                    else
                    {
                        m_completion.TrySetResult(true);
                    }
                }
            }
        }
    }
}
