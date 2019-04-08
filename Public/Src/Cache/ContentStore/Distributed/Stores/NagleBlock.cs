// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Stores;

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// Posts action to BatchBlock and pipes to provided TargetBlock(s).
    /// </summary>
    [Obsolete("Use NagleQueue<T> instead.")]
    public class NagleBlock<T> : INagleBlock<T>
    {
        private const int DefaultNumberOfPosts = 200;
        private readonly TimeSpan _timerInterval = TimeSpan.FromMinutes(1);
        private readonly BatchBlock<T> _batchBlock;
        private readonly Timer _intervalTimer;

        /// <summary>
        /// Initializes a new instance of the <see cref="NagleBlock{T}"/> class.
        /// </summary>
        /// <param name="numberOfPosts">Number of posts to bundle for each batch</param>
        public NagleBlock(int? numberOfPosts = null)
        {
            _batchBlock = new BatchBlock<T>(numberOfPosts ?? DefaultNumberOfPosts);
            _intervalTimer = new Timer(SendIncompleteBatch, null, _timerInterval, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Send batch on timer interval.
        /// </summary>
        /// <param name="obj">Timer</param>
        private void SendIncompleteBatch(object obj)
        {
            ResetTimer();
            _batchBlock.TriggerBatch();
        }

        /// <inheritdoc />
        public void Complete()
        {
            _intervalTimer.Dispose();
            _batchBlock.Complete();
        }

        /// <inheritdoc />
        public void Fault(Exception exception)
        {
            ((IPropagatorBlock<T, T[]>)_batchBlock).Fault(exception);
        }

        /// <inheritdoc />
        public Task Completion => _batchBlock.Completion;

        /// <inheritdoc />
        public DataflowMessageStatus OfferMessage(
            DataflowMessageHeader messageHeader, T messageValue, ISourceBlock<T> source, bool consumeToAccept)
        {
            ResetTimer();
            return ((IPropagatorBlock<T, T[]>)_batchBlock).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        /// <inheritdoc />
        public IDisposable LinkTo(ITargetBlock<T[]> target, DataflowLinkOptions linkOptions)
        {
            return _batchBlock.LinkTo(target, linkOptions);
        }

        /// <inheritdoc />
        public T[] ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T[]> target, out bool messageConsumed)
        {
            return ((IPropagatorBlock<T, T[]>)_batchBlock).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        /// <inheritdoc />
        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T[]> target)
        {
            return ((IPropagatorBlock<T, T[]>)_batchBlock).ReserveMessage(messageHeader, target);
        }

        /// <inheritdoc />
        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T[]> target)
        {
            ((IPropagatorBlock<T, T[]>)_batchBlock).ReleaseReservation(messageHeader, target);
        }

        private void ResetTimer()
        {
            _intervalTimer.Change(_timerInterval, Timeout.InfiniteTimeSpan);
        }
    }
}
