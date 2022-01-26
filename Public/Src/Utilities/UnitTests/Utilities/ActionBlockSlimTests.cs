// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class ActionBlockSlimTests
    {
        [Fact]
        public async Task WhenQueueIsFull()
        {
            var queue = new ActionQueue(degreeOfParallelism: 2, capacityLimit: 1);
            var tcs = new TaskCompletionSource<object>();

            var t = queue.RunAsync(() => tcs.Task);
            await Assert.ThrowsAsync<ActionBlockIsFullException>(() => queue.RunAsync(() => { }));

            tcs.SetResult(null);
            
            await t;

            // Even though the task 't' is done, it still possible that the internal counter in ActionBlock was not yet decremented.
            // "waiting" until all the items are fully processed before calling 'RunAsync' to avoid 'ActionBlockIsFullException'.
            await WaitUntilAsync(() => queue.PendingWorkItems == 0, TimeSpan.FromMilliseconds(1));

            // should be fine now.
            await queue.RunAsync(() => { });
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

        [Fact]
        public async Task ExceptionIsThrownWhenTheBlockIsFull()
        {
            var tcs = new TaskCompletionSource<object>();
            var actionBlock = ActionBlockSlim<int>.CreateWithAsyncAction(42, n => tcs.Task, capacityLimit: 1);
            actionBlock.Post(42);
            Assert.Equal(1, actionBlock.PendingWorkItems);

            Assert.Throws<ActionBlockIsFullException>(() => actionBlock.Post(1));
            Assert.Equal(1, actionBlock.PendingWorkItems);

            tcs.SetResult(null);
            await Task.Delay(10);
            Assert.Equal(0, actionBlock.PendingWorkItems);
            
            // This should not fail!
            actionBlock.Post(1);
        }

        [Fact]
        public async Task CompletionTaskIsDoneWhenCompletedIsCalled()
        {
            var actionBlock = new ActionBlockSlim<int>(42, n => { });
            var task = actionBlock.CompletionAsync();

            Assert.NotEqual(TaskStatus.RanToCompletion, task.Status);

            actionBlock.Complete();
            await task;

            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public async Task AllTheElementsAreFinished()
        {
            int count = 0;
            var actionBlock = new ActionBlockSlim<int>(
                42,
                n => { Interlocked.Increment(ref count); Thread.Sleep(1); });

            var task = actionBlock.CompletionAsync();
            actionBlock.Post(1);
            actionBlock.Post(2);

            actionBlock.Complete();
            await task;

            Assert.Equal(2, count);
        }

        [Fact]
        public async Task AllTheElementsAreProcessedBy1Thread()
        {
            const int maxCount = 420;
            int count = 0;
            var actionBlock = new ActionBlockSlim<int>(
                1,
                n => { Interlocked.Increment(ref count); });

            for (int i = 0; i < maxCount; i++)
            {
                actionBlock.Post(i);
            }

            actionBlock.Complete();
            await actionBlock.CompletionAsync();

            Assert.Equal(maxCount, count);
        }

        [Fact]
        public async Task AllTheElementsAreProcessedBy2Thread()
        {
            const int maxCount = 420;
            int count = 0;
            var actionBlock = new ActionBlockSlim<int>(
                2,
                n => { Interlocked.Increment(ref count); });

            for (int i = 0; i < maxCount; i++)
            {
                actionBlock.Post(i);
            }

            actionBlock.Complete();
            await actionBlock.CompletionAsync();

            Assert.Equal(maxCount, count);
        }

        [Fact]
        public async Task IncreaseConcurrency()
        {
            int count = 0;

            var waitForFirstTwoItems = TaskSourceSlim.Create<object>();
            // Event that will hold first two workers.
            var mre = new ManualResetEventSlim(false);
            var actionBlock = new ActionBlockSlim<int>(
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
                });

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
