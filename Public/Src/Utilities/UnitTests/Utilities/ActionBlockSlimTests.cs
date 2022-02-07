// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

using Xunit;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;

namespace Test.BuildXL.Utilities
{
    public class ActionBlockSlimTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ExceptionIsThrownWhenTheBlockIsFull(bool useChannelBasedImpl)
        {
            var tcs = new TaskCompletionSource<object>();
            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(1, n => tcs.Task, capacityLimit: 1, useChannelBasedImpl);
            actionBlock.Post(42);
            Assert.Equal(1, actionBlock.PendingWorkItems);

            Assert.Throws<ActionBlockIsFullException>(() => actionBlock.Post(1));
            Assert.Equal(1, actionBlock.PendingWorkItems);

            tcs.SetResult(null);
            await WaitUntilAsync(() => actionBlock.PendingWorkItems == 0, TimeSpan.FromMilliseconds(1)).WithTimeoutAsync(TimeSpan.FromSeconds(5));
            
            Assert.Equal(0, actionBlock.PendingWorkItems);

            // This should not fail!
            actionBlock.Post(1);
        }

        internal static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan waitInterval)
        {
            while (true)
            {
                if (predicate())
                {
                    break;
                }

                await Task.Delay(waitInterval);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CompletionTaskIsDoneWhenCompletedIsCalled(bool useChannelBasedImpl)
        {
            var actionBlock = ActionBlockSlim.Create<int>(42, n => { }, useChannelBasedImpl: useChannelBasedImpl);
            var task = actionBlock.CompletionAsync();

            Assert.NotEqual(TaskStatus.RanToCompletion, task.Status);

            actionBlock.Complete();
            await task;

            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }
        
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CompletionTaskIsDoneWhenCompletedWith0ConcurrencyIsCalled(bool useChannelBasedImpl)
        {
            var actionBlock = ActionBlockSlim.Create<int>(0, n => { }, useChannelBasedImpl: useChannelBasedImpl);
            var task = actionBlock.CompletionAsync();

            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task AllTheElementsAreFinished(bool useChannelBasedImpl)
        {
            int count = 0;
            var actionBlock = ActionBlockSlim.Create<int>(
                42,
                n => { Interlocked.Increment(ref count); Thread.Sleep(1); },
                useChannelBasedImpl: useChannelBasedImpl);

            var task = actionBlock.CompletionAsync();
            actionBlock.Post(1);
            actionBlock.Post(2);

            actionBlock.Complete();
            await task;

            Assert.Equal(2, count);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task AllTheElementsAreProcessedBy1Thread(bool useChannelBasedImpl)
        {
            const int maxCount = 420;
            int count = 0;
            var actionBlock = ActionBlockSlim.Create<int>(
                1,
                n => { Interlocked.Increment(ref count); },
                useChannelBasedImpl: useChannelBasedImpl);

            for (int i = 0; i < maxCount; i++)
            {
                actionBlock.Post(i);
            }

            actionBlock.Complete();
            await actionBlock.CompletionAsync();

            Assert.Equal(maxCount, count);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task AllTheElementsAreProcessedBy2Thread(bool useChannelBasedImpl)
        {
            const int maxCount = 420;
            int count = 0;
            var actionBlock = ActionBlockSlim.Create<int>(
                2,
                n => { Interlocked.Increment(ref count); },
                useChannelBasedImpl: useChannelBasedImpl);

            for (int i = 0; i < maxCount; i++)
            {
                actionBlock.Post(i);
            }

            actionBlock.Complete();
            await actionBlock.CompletionAsync();

            Assert.Equal(maxCount, count);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task IncreaseConcurrency(bool useChannelBasedImpl)
        {
            int count = 0;

            var waitForFirstTwoItems = new TaskCompletionSource<object>();
            // Event that will hold first two workers.
            var mre = new ManualResetEventSlim(false);
            var actionBlock = ActionBlockSlim.Create<int>(
                2,
                n =>
                {
                    var currentCount = Interlocked.Increment(ref count);
                    if (currentCount == 2)
                    {
                        // Notify the test that 2 items are processed.
                        waitForFirstTwoItems.SetResult(null);
                    }

                    if (currentCount <= 2)
                    {
                        // This is the first or the second thread that should be blocked before we increase the number of threads.
                        mre.Wait(TimeSpan.FromSeconds(100));
                    }

                    Thread.Sleep(1);
                },
                useChannelBasedImpl: useChannelBasedImpl);

            // Schedule work
            actionBlock.Post(1);
            actionBlock.Post(2);

            await waitForFirstTwoItems.Task;

            // The first 2 threads should be blocked in the callback in the action block,
            // but the count should be incremented
            Assert.Equal(2, count);

            var task = actionBlock.CompletionAsync();

            // The task should not be completed yet!
            Assert.NotEqual(TaskStatus.RanToCompletion, task.Status);

            // This will cause another thread to spawn
            actionBlock.IncreaseConcurrencyTo(3);

            // Add more work
            actionBlock.Post(3);

            actionBlock.Complete();

            // Release the first 2 threads
            mre.Set();

            // Waiting for completion
            await task;

            // The new thread should run and increment the count
            Assert.Equal(3, count);
        }
    }
}
