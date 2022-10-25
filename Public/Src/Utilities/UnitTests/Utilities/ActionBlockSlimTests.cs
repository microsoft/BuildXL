// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

using Xunit;
using BuildXL.Utilities.ParallelAlgorithms;
using Test.BuildXL.TestUtilities.Xunit;
using System.Collections.Concurrent;
using BuildXL.ToolSupport;

namespace Test.BuildXL.Utilities
{
    public class ActionBlockSlimTests
    {
        [Fact]
        public Task NoUnobservedExceptionWhenCallbackFails()
        {
            return UnobservedTaskExceptionHelper.RunAsync(
                async () =>
                {
                    var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(
                        1,
                        async n =>
                        {
                            await Task.Yield();
                            throw new InvalidOperationException("1");
                        },
                        capacityLimit: 1);
                    actionBlock.Post(42);

                    actionBlock.Complete();
                    await Assert.ThrowsAsync<InvalidOperationException>(() => actionBlock.Completion);
                });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ExceptionMustBePropagatedBack(bool propagateExceptions)
        {
            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(
                1,
                async n =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("1");
                },
                capacityLimit: 1);
            actionBlock.Post(42);

            actionBlock.Complete(propagateExceptionsFromCallback: propagateExceptions);

            if (propagateExceptions)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => actionBlock.Completion);
            }
            else
            {
                await actionBlock.Completion;
            }
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public async Task ExceptionIsThrownWhenTheBlockIsFull()
        {
            var tcs = new TaskCompletionSource<object>();
            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(1, n => tcs.Task, capacityLimit: 1);
            actionBlock.Post(42);

            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.Equal(0, actionBlock.PendingWorkItems);
            Assert.Equal(1, actionBlock.ProcessingWorkItems);

            actionBlock.Post(43);
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

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
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
            Assert.Equal(0, actionBlock.ProcessedWorkItems);

            Assert.False(actionBlock.TryPost(-23, throwOnFullOrComplete: false));
            Assert.Equal(0, actionBlock.ProcessedWorkItems);

            tcs.SetResult(null);
            var waitSucceeded = await ParallelAlgorithms.WaitUntilAsync(
                () => actionBlock.PendingWorkItems == 0, 
                TimeSpan.FromMilliseconds(1),
                timeout: TimeSpan.FromSeconds(5));
            Assert.True(waitSucceeded);

            Assert.Equal(0, actionBlock.PendingWorkItems);

            // This should not fail!
            actionBlock.Post(23);

            // We don't care about an exception
            actionBlock.Complete(propagateExceptionsFromCallback: false);
            await actionBlock.Completion;

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
            await actionBlock.Completion;
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
            await actionBlock.Completion;
        }

        [Fact]
        public async Task ItemsAreProcessedOnceCompleted()
        {
            int trulyProcessed = 0;
            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(2,
                async n => 
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)); 
                    Interlocked.Increment(ref trulyProcessed); 
                });
            actionBlock.Post(1);
            actionBlock.Post(1);
            actionBlock.Post(1);
            actionBlock.Complete();
            await actionBlock.Completion;
            Assert.Equal(3, trulyProcessed);
            Assert.Equal(3, actionBlock.ProcessedWorkItems);
            Assert.Equal(0, actionBlock.PendingWorkItems);
            Assert.Equal(0, actionBlock.ProcessingWorkItems);
        }

        [Fact]
        public async Task CompletionTaskIsDoneWhenCompletedIsCalled()
        {
            var actionBlock = ActionBlockSlim.Create<int>(42, n => { });
            var task = actionBlock.Completion;

            Assert.NotEqual(TaskStatus.RanToCompletion, task.Status);

            actionBlock.Complete();
            await task;

            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public async Task CompletionIsAwaitableBeforeCompletedIsCalled()
        {
            var actionBlock = ActionBlockSlim.Create<int>(0, n => { });
            var task = actionBlock.Completion;

            bool completed = false;
            var concurrentWaiter = Task.Run(async () =>
            {
                await task;
                completed = true;
            });

            Assert.False(completed);

            actionBlock.IncreaseConcurrencyTo(10);
            actionBlock.Complete();

            await actionBlock.Completion;
            await concurrentWaiter;

            Assert.True(completed);
        }

        [Fact]
        public Task CompletionTaskIsNotDoneWhenCompletedWith0ConcurrencyIsCalled()
        {
            var actionBlock = ActionBlockSlim.Create<int>(0, n => { });
            var task = actionBlock.Completion;
            Assert.NotEqual(TaskStatus.RanToCompletion, task.Status);
            actionBlock.Complete();
            return task;
        }


        [Fact]
        public async Task AllTheElementsAreFinished()
        {
            int count = 0;
            var actionBlock = ActionBlockSlim.Create<int>(
                42,
                n => { Interlocked.Increment(ref count); Thread.Sleep(1); });

            var task = actionBlock.Completion;
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
            await actionBlock.Completion;

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
            await actionBlock.Completion;

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

            var task = actionBlock.Completion;

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
