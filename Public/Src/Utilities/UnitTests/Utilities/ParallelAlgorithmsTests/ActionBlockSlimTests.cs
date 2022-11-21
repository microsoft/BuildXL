// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.Utilities.ParallelAlgorithmsTests.ParallelAlgorithmsHelper;

namespace Test.BuildXL.Utilities.ParallelAlgorithmsTests
{
    public class ActionBlockSlimTests
    {
        private readonly ITestOutputHelper m_helper;

        /// <nodoc />
        public ActionBlockSlimTests(ITestOutputHelper helper)
        {
            m_helper = helper;
        }

        // Tests that cover cancellation
        [Fact]
        public async Task External_Cancellation_Cancels_Completion()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var actionBlock = ActionBlockSlim.Create<int>(degreeOfParallelism: 1, _ => { }, cancellationToken: cts.Token);
            
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => actionBlock.Completion.WithTimeoutAsync(TimeSpan.FromSeconds(1)));
            actionBlock.Completion.Status.Should().Be(TaskStatus.Canceled);
        }
        
        [Fact]
        public async Task External_Cancellation_Allows_Callbacks_To_Finish()
        {
            bool cancellationIsChecked = false;
            var cts = new CancellationTokenSource();
            var actionBlock = ActionBlockSlim.Create<int>(
                degreeOfParallelism: 1,
                async item =>
                {
                    await WaitUntilOrFailAsync(() => cancellationIsChecked);
                },
                cancellationToken: cts.Token);

            actionBlock.Post(42);
            
            // Waiting for the item to be picked up.
            await WaitUntilOrFailAsync(() => actionBlock.ProcessingWorkItems == 1);

            cts.Cancel();

            // The cancellation is not eager. It should happen only when all the callbacks are done!
            actionBlock.Completion.IsCanceled.Should().BeFalse();
            cancellationIsChecked = true;

            await WaitUntilOrFailAsync(() => actionBlock.PendingWorkItems == 0);

            // Completion property should change its state to 'Canceled'
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => actionBlock.Completion.WithTimeoutAsync(TimeSpan.FromSeconds(1)));
        }
        
        [Fact]
        public async Task CancelPending_TriggersCancellationToken_InCallback()
        {
            await Task.Yield();
            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(
                1,
                async (input, token) =>
                {
                    await token.ToAwaitable().CompletionTask;
                },
                capacityLimit: 1,
                cancellationToken: CancellationToken.None);

            actionBlock.Post(42);

            // Waiting for the block to pull the first item.
            await WaitUntilOrFailAsync(() => actionBlock.PendingWorkItems == 0);

            // Adding an item that should not be processed because of the cancellation.
            actionBlock.Post(42);

            actionBlock.Complete(cancelPending: true);

            // Now the block should be done but because we canceled pending items the Completion property should complete successfully.
            await actionBlock.Completion;
        }

        [Fact]
        public void Unbounded_SingleReader_Block_Should_Not_Crash_On_Getting_PendingItems()
        {
            // We used to have an issue that Channel.Count property was failing when degreeOfParallelism is 1 and the channel is unbounded.
            // Checking that this option works.
            var actionBlock = ActionBlockSlim.Create<int>(degreeOfParallelism: 1, _ => { }, singleProducedConstrained: false);
            
            actionBlock.PendingWorkItems.Should().Be(0);
        }

        [Fact]
        public Task No_UnobservedExceptions_When_Callback_Fails()
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

        [Fact]
        public async Task Completion_Fails_Fast_If_Configured()
        {
            await Task.Yield();
            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(
                4,
                input =>
                {
                    throw new ArgumentException($"Invalid input: {input}");
                }, failFast: true);

            actionBlock.Post(42);

            // if fail fast flag is passed, the 'Completion' task should fail as soon as the error occurs.
            await Assert.ThrowsAsync<ArgumentException>(() => actionBlock.Completion);
        }

        [Fact]
        public async Task Completion_Aggregates_Exceptions()
        {
            var tcs = new TaskCompletionSource<object>();
            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(
                4,
                async input =>
                {
                    await tcs.Task;
                    if (input % 2 == 0)
                    {
                        throw new ArgumentException("I don't like even numbers");
                    }
                });

            for (var i = 0; i < 4; i++)
            {
                actionBlock.Post(i);
            }

            tcs.SetResult(0);

            actionBlock.Complete();

            var exception = await Assert.ThrowsAsync<AggregateException>(() => actionBlock.Completion);
            XAssert.AreEqual(2, exception.InnerExceptions.Count);
            XAssert.IsTrue(exception.InnerExceptions.All(e => e is ArgumentException));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Exception_Must_Be_Propagated_Back_If_Configured(bool propagateExceptions)
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

        [Fact]
        public async Task Exception_Is_Thrown_When_The_Block_Is_Full_And_Configured()
        {
            var tcs = new TaskCompletionSource<object>();
            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(1, n => tcs.Task, capacityLimit: 1);
            actionBlock.Post(42);

            // 'awaiting' when the item is picked up.
            await WaitUntilOrFailAsync(() => actionBlock.PendingWorkItems == 0);

            Assert.Equal(1, actionBlock.ProcessingWorkItems);

            actionBlock.Post(43);
            Assert.Equal(1, actionBlock.PendingWorkItems);

            Assert.Throws<ActionBlockIsFullException>(() => actionBlock.Post(1, throwOnFullOrComplete: true));
            Assert.Equal(1, actionBlock.PendingWorkItems);

            tcs.SetResult(null);
            await WaitUntilOrFailAsync(() => actionBlock.PendingWorkItems == 0);
            
            Assert.Equal(0, actionBlock.PendingWorkItems);

            // This should not fail!
            actionBlock.Post(1);
        }

        [Fact]
        public async Task Exception_Is_Not_Thrown_When_The_Block_Is_Full_Or_Complete()
        {
            ConcurrentQueue<int> seenInputs = new();
            var tcs = new TaskCompletionSource<object>();
            var actionBlock = ActionBlockSlim.CreateWithAsyncAction<int>(1, input =>
            {
                seenInputs.Enqueue(input);
                return tcs.Task;
            }, capacityLimit: 1);

            actionBlock.Post(42);
            
            // awaiting until the item is obtained from the queue for processing
            await WaitUntilOrFailAsync(() => actionBlock.PendingWorkItems == 0);
            
            // This one will occupy the only slot of the queue.
            actionBlock.Post(42);

            Assert.False(actionBlock.TryPost(-23, throwOnFullOrComplete: false));
            Assert.Equal(0, actionBlock.ProcessedWorkItems);

            tcs.SetResult(null);
            await WaitUntilOrFailAsync(() => actionBlock.PendingWorkItems == 0);

            Assert.Equal(0, actionBlock.PendingWorkItems);

            // This should not fail!
            actionBlock.Post(23);

            // We don't care about an exception
            actionBlock.Complete(propagateExceptionsFromCallback: false);
            await actionBlock.Completion;

            Assert.False(actionBlock.TryPost(-43, throwOnFullOrComplete: false));

            Assert.Equal(3, seenInputs.Count);

            // Negative inputs denote cases where the item should not be added
            Assert.DoesNotContain(seenInputs, i => i < 0);
        }
        
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CompletionAsync_Succeeded_When_CancelPending(bool cancelPending)
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

            actionBlock.Post(42);
            
            // Waiting for the block to pull the first item.
            await WaitUntilOrFailAsync(() => actionBlock.PendingWorkItems == 0);
            
            // Adding an item that should not be processed because of the cancellation.
            actionBlock.Post(42);

            actionBlock.Complete(cancelPending);

            // The ActionBlock is completed, now we need to unblock the processor.
            tcs.SetResult(null);

            // Now the block should be done
            await actionBlock.Completion;

            // The number of processed items depends on whether we were cancelling pending items or not.
            int expectedProcessedItemsCount = cancelPending ? 1 : 2;
            Assert.Equal(expectedProcessedItemsCount, actionBlock.ProcessedWorkItems);
        }
        
        [Fact]
        public async Task Items_Are_Processed_Once_Completed()
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
        public async Task Completion_Task_Is_Done_When_Completed_Is_Called()
        {
            var actionBlock = ActionBlockSlim.Create<int>(42, n => { });
            var task = actionBlock.Completion;

            Assert.NotEqual(TaskStatus.RanToCompletion, task.Status);

            actionBlock.Complete();
            await task;

            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public async Task Completion_Is_Awaitable_Before_Completed_Is_Called()
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
        public Task Completion_Task_Is_Not_Done_When_Completed_With_0_Concurrency_Is_Called()
        {
            var actionBlock = ActionBlockSlim.Create<int>(0, n => { });
            var task = actionBlock.Completion;
            Assert.NotEqual(TaskStatus.RanToCompletion, task.Status);
            actionBlock.Complete();
            return task;
        }


        [Fact]
        public async Task All_The_Elements_Are_Finished()
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
        public async Task All_The_Elements_Are_Processed_By_1_Thread()
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
        public async Task All_The_Elements_Are_Processed_By_2_Thread()
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
        public async Task Increase_Concurrency()
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
