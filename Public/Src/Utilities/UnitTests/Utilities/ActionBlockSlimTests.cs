// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

using Xunit;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using System.Collections.Concurrent;

namespace Test.BuildXL.Utilities
{
    public class ActionBlockSlimTests
    {
        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public async Task ExceptionIsThrownWhenTheBlockIsFull()
        {
            var tcs = new TaskCompletionSource<object>();
            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(1, n => tcs.Task, capacityLimit: 1);
            actionBlock.Post(42);
            Assert.Equal(1, actionBlock.PendingWorkItems);

            Assert.Throws<ActionBlockIsFullException>(() => actionBlock.Post(1));
            Assert.Equal(1, actionBlock.PendingWorkItems);

            tcs.SetResult(null);
            bool waitSucceeded = await ParallelAlgorithms.WaitUntilAsync(
                () => actionBlock.PendingWorkItems == 0, 
                TimeSpan.FromMilliseconds(1),
                timeout: TimeSpan.FromSeconds(5));
            Assert.True(waitSucceeded);
            
            Assert.Equal(0, actionBlock.PendingWorkItems);

            // This should not fail!
            actionBlock.Post(1);
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public async Task ExceptionIsNotThrownWhenTheBlockIsFullOrComplete()
        {
            ConcurrentQueue<int> seenInputs = new();
            var tcs = new TaskCompletionSource<object>();
            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(1, input =>
            {
                seenInputs.Enqueue(input);
                return tcs.Task;
            }, capacityLimit: 1);

            actionBlock.Post(42);
            Assert.Equal(1, actionBlock.PendingWorkItems);

            Assert.False(actionBlock.TryPost(-23, throwOnFullOrComplete: false));
            Assert.Equal(1, actionBlock.PendingWorkItems);

            tcs.SetResult(null);
            var waitSucceeded = await ParallelAlgorithms.WaitUntilAsync(
                () => actionBlock.PendingWorkItems == 0, 
                TimeSpan.FromMilliseconds(1),
                timeout: TimeSpan.FromSeconds(5));
            Assert.True(waitSucceeded);

            Assert.Equal(0, actionBlock.PendingWorkItems);

            // This should not fail!
            actionBlock.Post(23);

            actionBlock.Complete();
            await actionBlock.CompletionAsync();

            Assert.True(actionBlock.IsComplete);
            Assert.False(actionBlock.TryPost(-43, throwOnFullOrComplete: false));

            Assert.Equal(2, seenInputs.Count);

            // Negative inputs denote cases where the item should not be added
            Assert.DoesNotContain(seenInputs, i => i < 0);
        }
        
        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public async Task CompletionAsync_Succeeded_When_CancellationToken_Is_Canceled()
        {

            await Task.Yield();
            var cts = new CancellationTokenSource();

            var tcs = new TaskCompletionSource<object>();

            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(
                1,
                input =>
                {
                    return tcs.Task;
                },
                capacityLimit: 1,
                cancellationToken: cts.Token);

            cts.Cancel();
            tcs.SetResult(null);

            actionBlock.Complete();
            await actionBlock.CompletionAsync();
        }
        
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task CompletionAsync_Succeeded_When_CancelPending_Is_True()
        {
            await Task.Yield();
            var tcs = new TaskCompletionSource<object>();

            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(
                1,
                input =>
                {
                    return tcs.Task;
                },
                capacityLimit: 1,
                cancellationToken: CancellationToken.None);

            actionBlock.Complete(cancelPending: true);
            await actionBlock.CompletionAsync();
        }

        [Fact]
        public async Task CompletionTaskIsDoneWhenCompletedIsCalled()
        {
            var actionBlock = ActionBlockSlim.Create<int>(42, n => { });
            var task = actionBlock.CompletionAsync();

            Assert.NotEqual(TaskStatus.RanToCompletion, task.Status);

            actionBlock.Complete();
            await task;

            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }
        
        [Fact]
        public void CompletionTaskIsDoneWhenCompletedWith0ConcurrencyIsCalled()
        {
            var actionBlock = ActionBlockSlim.Create<int>(0, n => { });
            var task = actionBlock.CompletionAsync();

            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public async Task AllTheElementsAreFinished()
        {
            int count = 0;
            var actionBlock = ActionBlockSlim.Create<int>(
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
            var actionBlock = ActionBlockSlim.Create<int>(
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
            var actionBlock = ActionBlockSlim.Create<int>(
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
