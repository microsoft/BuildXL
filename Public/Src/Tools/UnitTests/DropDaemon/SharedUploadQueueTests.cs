// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.ParallelAlgorithms;
using Tool.DropDaemon;
using Xunit;

namespace Test.Tool.DropDaemon
{
    /// <nodoc/>
    public sealed class SharedUploadQueueTests
    {
        /// <nodoc/>
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task BatchQueueInterfaceFlushesAndDrainsPendingWork(bool shared, bool synchronousDispose)
        {
            using var owner = new SharedUploadQueue(maxParallelUploads: 1);
            var started = NewSignal();
            var release = NewSignal();
            async Task Process(List<int> batch)
            {
                Assert.Equal(42, Assert.Single(batch));
                started.SetResult(true);
                await release.Task;
            }

            IWorkQueue<int> queue = shared
                ? owner.CreateBatcher<int>(Process, batchSize: 2, interval: Timeout.InfiniteTimeSpan)
                : NagleQueue<int>.Create(Process, maxDegreeOfParallelism: 1, interval: Timeout.InfiniteTimeSpan, batchSize: 2);
            queue.Enqueue(42);
            Assert.False(started.Task.IsCompleted);

            Task drain = synchronousDispose ? Task.Run(queue.Dispose) : queue.DisposeAsync().AsTask();
            try
            {
                await started.Task;
                Assert.False(drain.IsCompleted);
            }
            finally
            {
                release.TrySetResult(true);
                await drain;
            }

            queue.Dispose();
            Assert.Throws<ObjectDisposedException>(() => queue.Enqueue(0));
        }

        /// <nodoc/>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void NonPositiveConcurrencyIsRejected(int limit)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SharedUploadQueue(limit));
        }

        /// <nodoc/>
        [Theory]
        [InlineData(-2L)]
        [InlineData((long)uint.MaxValue)]
        public void InvalidTimerIntervalIsRejectedBeforeEnqueue(long intervalMillis)
        {
            using var queue = new SharedUploadQueue(maxParallelUploads: 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.CreateBatcher<int>(
                _ => Task.CompletedTask, batchSize: 2, interval: TimeSpan.FromMilliseconds(intervalMillis)));
        }

        /// <nodoc/>
        [Fact]
        public async Task EmptyBatcherCanBeDrainedRepeatedly()
        {
            using var queue = new SharedUploadQueue(maxParallelUploads: 1);
            var batcher = queue.CreateBatcher<int>(_ => throw new InvalidOperationException("No batch should run."), batchSize: 1, interval: Timeout.InfiniteTimeSpan);
            await batcher.DisposeAsync();
            await batcher.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => batcher.Enqueue(0));
        }

        /// <nodoc/>
        [Fact]
        public async Task PostingToACompletedOwnerFailsInsteadOfStrandingDrain()
        {
            var queue = new SharedUploadQueue(maxParallelUploads: 1);
            var batcher = queue.CreateBatcher<int>(_ => Task.CompletedTask, batchSize: 1, interval: Timeout.InfiniteTimeSpan);
            queue.Dispose();
            Assert.Throws<ActionBlockIsFullException>(() => batcher.Enqueue(0));
            await batcher.DisposeAsync();
        }

        /// <nodoc/>
        [Fact]
        public async Task RejectedPartialBatchDoesNotStrandRepeatedDrain()
        {
            var queue = new SharedUploadQueue(maxParallelUploads: 1);
            var batcher = queue.CreateBatcher<int>(_ => Task.CompletedTask, batchSize: 2, interval: Timeout.InfiniteTimeSpan);
            batcher.Enqueue(0);
            queue.Dispose();

            Assert.Throws<ActionBlockIsFullException>(() => batcher.DisposeAsync());
            await batcher.DisposeAsync();
        }

        /// <nodoc/>
        [Fact]
        public async Task CompletedListsAreClearedAndReusedAcrossBatchersWithDifferentBatchSizes()
        {
            using var queue = new SharedUploadQueue(maxParallelUploads: 1);
            var firstItem = new PooledItem();
            List<PooledItem> firstBatch = null;
            var first = queue.CreateBatcher<PooledItem>(batch =>
            {
                firstBatch = batch;
                Assert.Same(firstItem, Assert.Single(batch));
                return Task.CompletedTask;
            }, batchSize: 2, interval: Timeout.InfiniteTimeSpan);
            var empty = queue.CreateBatcher<PooledItem>(_ => throw new InvalidOperationException("No batch should run."), batchSize: 1, interval: Timeout.InfiniteTimeSpan);
            first.Enqueue(firstItem);
            await first.DisposeAsync();
            Assert.Empty(firstBatch);

            // Draining another destination must not clear the shared pool.
            await empty.DisposeAsync();
            var secondItems = Enumerable.Range(0, 4).Select(_ => new PooledItem()).ToArray();
            var second = queue.CreateBatcher<PooledItem>(batch =>
            {
                Assert.Same(firstBatch, batch);
                Assert.Equal(secondItems, batch);
                return Task.CompletedTask;
            }, batchSize: secondItems.Length, interval: Timeout.InfiniteTimeSpan);
            foreach (var item in secondItems)
            {
                second.Enqueue(item);
            }

            await second.DisposeAsync();
            Assert.Empty(firstBatch);
        }

        /// <nodoc/>
        [Theory]
        [InlineData(1, false)]
        [InlineData(3, false)]
        [InlineData(3, true)]
        [InlineData(null, true)]
        public async Task ConcurrencyIsSharedAndAvailableToASingleDrop(int? maxParallelUploads, bool multipleDrops)
        {
            var serviceConfig = new DropServiceConfig(useSharedConcurrencyLimiter: true, maxParallelUploads: maxParallelUploads);
            int limit = maxParallelUploads ?? DropConfig.DefaultMaxParallelUploads;
            using var queue = new SharedUploadQueue(serviceConfig.MaxParallelUploads);
            var release = NewSignal();
            var saturated = NewSignal();
            int active = 0;
            int peak = 0;
            int started = 0;
            var sync = new object();
            async Task Process(List<int> batch)
            {
                lock (sync)
                {
                    active++;
                    started++;
                    peak = Math.Max(peak, active);
                    if (active == limit)
                    {
                        saturated.TrySetResult(true);
                    }
                }

                await release.Task;
                lock (sync)
                {
                    active--;
                }
            }

            var first = queue.CreateBatcher<int>(Process, batchSize: 1, interval: Timeout.InfiniteTimeSpan);
            var second = queue.CreateBatcher<int>(Process, batchSize: 1, interval: Timeout.InfiniteTimeSpan);
            try
            {
                // More work than the shared budget, with all callbacks blocked until saturation is observed.
                for (int i = 0; i < limit * 4; i++)
                {
                    (multipleDrops && i % 2 != 0 ? second : first).Enqueue(i);
                }

                await saturated.Task;
                lock (sync)
                {
                    Assert.Equal(limit, active);
                    Assert.Equal(limit, started);
                }
            }
            finally
            {
                release.TrySetResult(true);
                await Task.WhenAll(first.DisposeAsync().AsTask(), second.DisposeAsync().AsTask());
            }

            Assert.Equal(limit, peak);
            Assert.Equal(limit * 4, started);
        }

        /// <nodoc/>
        [Fact]
        public async Task ReadyBatchesFromDifferentDropsAreDequeuedInArrivalOrder()
        {
            using var queue = new SharedUploadQueue(maxParallelUploads: 1);
            var release = NewSignal();
            var started = NewSignal();
            var order = new List<int>();
            async Task Process(List<int> batch)
            {
                order.Add(Assert.Single(batch));
                started.TrySetResult(true);
                await release.Task;
            }

            var first = queue.CreateBatcher<int>(Process, batchSize: 1, interval: Timeout.InfiniteTimeSpan);
            var second = queue.CreateBatcher<int>(Process, batchSize: 1, interval: Timeout.InfiniteTimeSpan);
            first.Enqueue(1);
            await started.Task;
            second.Enqueue(2);
            first.Enqueue(3);
            second.Enqueue(4);
            first.Enqueue(5);
            var drains = Task.WhenAll(first.DisposeAsync().AsTask(), second.DisposeAsync().AsTask());
            release.SetResult(true);
            await drains;

            Assert.Equal(Enumerable.Range(1, 5), order);
        }

        /// <nodoc/>
        [Fact]
        public async Task DrainFlushesPartialBatchAndWaitsOnlyForItsOwnWork()
        {
            using var queue = new SharedUploadQueue(maxParallelUploads: 1);
            var initialStarted = NewSignal();
            var releaseInitial = NewSignal();
            var ownStarted = NewSignal();
            var releaseOwn = NewSignal();
            var tailStarted = NewSignal();
            var releaseTail = NewSignal();
            var first = queue.CreateBatcher<int>(async batch =>
            {
                Assert.Equal(1, Assert.Single(batch));
                ownStarted.SetResult(true);
                await releaseOwn.Task;
            }, batchSize: 2, interval: Timeout.InfiniteTimeSpan);
            var second = queue.CreateBatcher<int>(async batch =>
            {
                if (Assert.Single(batch) == 0)
                {
                    initialStarted.SetResult(true);
                    await releaseInitial.Task;
                }
                else
                {
                    tailStarted.TrySetResult(true);
                    await releaseTail.Task;
                }
            }, batchSize: 1, interval: Timeout.InfiniteTimeSpan);

            try
            {
                second.Enqueue(0);
                await initialStarted.Task;
                first.Enqueue(1);
                Task ownDrain = first.DisposeAsync().AsTask();
                Assert.False(ownDrain.IsCompleted);
                second.Enqueue(2);
                releaseInitial.SetResult(true);
                await ownStarted.Task;
                Assert.False(ownDrain.IsCompleted);
                releaseOwn.SetResult(true);
                await tailStarted.Task;
                await ownDrain;
                Assert.Throws<ObjectDisposedException>(() => first.Enqueue(3));

                // A drained destination must not complete/dispose the shared executor.
                second.Enqueue(3);
                Task otherDrain = second.DisposeAsync().AsTask();
                Assert.False(otherDrain.IsCompleted);
                releaseTail.SetResult(true);
                await otherDrain;
                await first.DisposeAsync();
            }
            finally
            {
                releaseInitial.TrySetResult(true);
                releaseOwn.TrySetResult(true);
                releaseTail.TrySetResult(true);
                await Task.WhenAll(first.DisposeAsync().AsTask(), second.DisposeAsync().AsTask());
            }
        }

        /// <nodoc/>
        [Fact]
        public async Task TimerFlushesPartialBatchAndCompletedBatchReleasesItems()
        {
            using var queue = new SharedUploadQueue(maxParallelUploads: 1);
            var processed = NewSignal();
            List<object> processedBatch = null;
            object item = new object();
            var batcher = queue.CreateBatcher<object>(batch =>
            {
                Assert.Same(item, Assert.Single(batch));
                processedBatch = batch;
                processed.SetResult(true);
                return Task.CompletedTask;
            }, batchSize: 2, interval: TimeSpan.FromMilliseconds(1));

            batcher.Enqueue(item);
            await processed.Task;
            await batcher.DisposeAsync();
            Assert.Empty(processedBatch);
        }

        /// <nodoc/>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task TimerRearmsWhileEarlierBatchIsStillProcessing(int intervalMillis)
        {
            using var queue = new SharedUploadQueue(maxParallelUploads: 2);
            var firstStarted = NewSignal();
            var secondStarted = NewSignal();
            var release = NewSignal();
            var batcher = queue.CreateBatcher<int>(async batch =>
            {
                int item = Assert.Single(batch);
                if (item == 1)
                {
                    firstStarted.SetResult(true);
                }
                else
                {
                    Assert.Equal(2, item);
                    secondStarted.SetResult(true);
                }

                await release.Task;
            }, batchSize: 2, interval: TimeSpan.FromMilliseconds(intervalMillis));

            try
            {
                batcher.Enqueue(1);
                await firstStarted.Task;
                batcher.Enqueue(2);
                await secondStarted.Task;

                Task drain = batcher.DisposeAsync().AsTask();
                Assert.False(drain.IsCompleted);
                release.SetResult(true);
                await drain;
            }
            finally
            {
                release.TrySetResult(true);
                await batcher.DisposeAsync();
            }
        }

        /// <nodoc/>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task TimerIsArmedOnlyWhileAPartialBatchIsBuffered(int intervalMillis)
        {
            var executor = ActionBlockSlim.CreateWithAsyncAction<Func<Task>>(1, work => work());
            var timeProvider = new ManualTimeProvider();
            var release = NewSignal();
            var batches = new List<int[]>();
            var batcher = new SharedUploadQueue.Batcher<int>(executor, async batch =>
            {
                await release.Task;
                batches.Add(batch.ToArray());
            }, batchSize: 2, interval: TimeSpan.FromMilliseconds(intervalMillis), timeProvider);

            try
            {
                Assert.False(timeProvider.Timer.IsArmed);
                batcher.Enqueue(1);
                Assert.True(timeProvider.Timer.IsArmed);
                timeProvider.Timer.Fire();
                Assert.False(timeProvider.Timer.IsArmed);

                batcher.Enqueue(2);
                Assert.True(timeProvider.Timer.IsArmed);
                batcher.Enqueue(3);
                Assert.False(timeProvider.Timer.IsArmed);

                batcher.Enqueue(4);
                Assert.True(timeProvider.Timer.IsArmed);
                timeProvider.Timer.Fire();
                Assert.False(timeProvider.Timer.IsArmed);

                Task drain = batcher.DisposeAsync().AsTask();
                Assert.True(timeProvider.Timer.IsDisposed);
                Assert.False(drain.IsCompleted);
                release.SetResult(true);
                await drain;
                Assert.Equal(new[] { 1 }, batches[0]);
                Assert.Equal(new[] { 2, 3 }, batches[1]);
                Assert.Equal(new[] { 4 }, batches[2]);
                Assert.Equal(3, batches.Count);
            }
            finally
            {
                release.TrySetResult(true);
                await batcher.DisposeAsync();
                executor.Complete();
                await executor.Completion;
            }
        }

        /// <nodoc/>
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task FailedOrCancelledBatchDoesNotBlockOtherDrops(bool cancelled, bool oversized)
        {
            using var queue = new SharedUploadQueue(maxParallelUploads: 1);
            var failure = new InvalidOperationException("batch failed");
            List<int> failedBatch = null;
            var failed = queue.CreateBatcher<int>(
                batch =>
                {
                    failedBatch = batch;
                    if (oversized)
                    {
                        batch.Capacity = ObjectPool<List<int>>.DefaultMaximumRetainedSize + 1;
                    }

                    return cancelled ? Task.FromCanceled(new CancellationToken(canceled: true)) : Task.FromException(failure);
                },
                batchSize: 1,
                interval: Timeout.InfiniteTimeSpan);
            var succeeded = NewSignal();
            var healthy = queue.CreateBatcher<int>(_ =>
            {
                succeeded.SetResult(true);
                return Task.CompletedTask;
            }, batchSize: 1, interval: Timeout.InfiniteTimeSpan);
            failed.Enqueue(0);
            healthy.Enqueue(1);

            Exception observed = await Record.ExceptionAsync(() => failed.DisposeAsync().AsTask());
            if (cancelled)
            {
                Assert.IsAssignableFrom<OperationCanceledException>(observed);
            }
            else
            {
                Assert.Same(failure, observed);
            }
            Assert.Empty(failedBatch);
            await succeeded.Task;
            await healthy.DisposeAsync();
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            public ManualTimer Timer { get; private set; }

            public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
            {
                Timer = new ManualTimer(callback, state);
                Timer.Change(dueTime, period);
                return Timer;
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly TimerCallback m_callback;
            private readonly object m_state;

            public bool IsArmed { get; private set; }

            public bool IsDisposed { get; private set; }

            public ManualTimer(TimerCallback callback, object state)
            {
                m_callback = callback;
                m_state = state;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.False(IsDisposed);
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                IsArmed = dueTime != Timeout.InfiniteTimeSpan;
                return true;
            }

            public void Fire()
            {
                Assert.True(IsArmed);
                IsArmed = false;
                m_callback(m_state);
            }

            public void Dispose()
            {
                IsDisposed = true;
                IsArmed = false;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }
        }

        private sealed class PooledItem
        {
        }
    }
}
