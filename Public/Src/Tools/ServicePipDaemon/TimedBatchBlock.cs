// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    /// A combination of dataflow blocks that process items in batches as well as at timed intervals.
    /// </summary>
    public sealed class TimedBatchBlock<T> : IDisposable
    {
        private readonly BatchBlock<T> m_batchBlock;
        private readonly BufferBlock<T[]> m_bufferBlock;
        private readonly ActionBlock<T[]> m_actionBlock;
        private readonly Timer m_batchTimer;
        private readonly TimeSpan m_nagleInterval;
        private readonly Func<T[], Task> m_batchProcessor;

        private long m_lastTimeBlockTriggered = DateTime.UtcNow.Ticks;

        /// <nodoc/>
        public Task Completion => m_actionBlock.Completion;

        /// <nodoc/>
        public TimedBatchBlock(int maxDegreeOfParallelism, int batchSize, TimeSpan nagleInterval, Func<T[], Task> batchProcessor)
        {
            var groupingOptions = new GroupingDataflowBlockOptions { Greedy = true };
            var actionOptions = new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism };
            var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

            m_batchProcessor = batchProcessor;

            m_batchBlock = new BatchBlock<T>(batchSize, groupingOptions);
            // per http://blog.stephencleary.com/2012/11/async-producerconsumer-queue-using.html, good to have buffer when throttling
            m_bufferBlock = new BufferBlock<T[]>(); 
            m_actionBlock = new ActionBlock<T[]>(ProcessSingleBatchAsync, actionOptions);
            m_batchBlock.LinkTo(m_bufferBlock, linkOptions);
            m_bufferBlock.LinkTo(m_actionBlock, linkOptions);

            // create and set up timer for triggering the batch block
            m_nagleInterval = nagleInterval;
            m_batchTimer = new Timer(FlushBatchBlock, null, nagleInterval, nagleInterval);
        }

        /// <summary>
        /// Offers an item to the block.
        /// </summary>
        /// <returns>Whether an item was accepted.</returns>
        public Task<bool> SendAsync(T item)
        {
            return m_batchBlock.SendAsync(item);
        }

        /// <nodoc/>
        public void Complete()
        {
            m_batchBlock.Complete();
        }

        private Task ProcessSingleBatchAsync(T[] batch)
        {
            Interlocked.Exchange(ref m_lastTimeBlockTriggered, DateTime.UtcNow.Ticks);

            return m_batchProcessor(batch);
        }

        private void FlushBatchBlock(object state)
        {
            var elapsedTicks = DateTime.UtcNow.Ticks - Interlocked.Read(ref m_lastTimeBlockTriggered);
            if (elapsedTicks > m_nagleInterval.Ticks)
            {
                m_batchBlock.TriggerBatch();
            }
        }

        /// <nodoc/>
        public void Dispose()
        {
            m_batchTimer.Dispose();
        }
    }
}
