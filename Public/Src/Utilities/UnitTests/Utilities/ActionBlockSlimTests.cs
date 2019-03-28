// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
