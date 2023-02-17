// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.Utilities.ParallelAlgorithmsTests;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class TaskUtilitiesTests
    {
        [Fact]
        public Task ToAwaitable_CompletesWhenCanceled()
        {
            var cts = new CancellationTokenSource();

            using var awaitable = cts.Token.ToAwaitable();
            Assert.False(awaitable.CompletionTask.IsCompleted);

            cts.Cancel();
            return awaitable.CompletionTask;
        }

        [Fact]
        public async Task WhenAllWithCancellationAsync_ShouldNotCauseUnobservedTaskExceptions()
        {
            // This test checks that if the task awaited as part of WhenAllWithCancellationAsync call
            // fails, it should not cause any unobserved task errors.
            await Task.Yield();
            await UnobservedTaskExceptionHelper.RunAsync(
                async () =>
                {
                    using var cts = new CancellationTokenSource();
                    
                    cts.CancelAfter(10);

                    // The task will after a small delay
                    var failure = Task.Run(async () =>
                                           {
                                               await Task.Delay(500);
                                               throw new Exception("1");
                                           });

                    try
                    {
                        await TaskUtilities.WhenAllWithCancellationAsync(new[] { failure }, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {

                    }

                    await Task.Delay(1000);
                });
        }
        
        [Fact]
        public async Task WhenAllWithCancellationAsync_PropagatesSingleExceptionCorrectly()
        {
            var task = Task.Run(() => throw new ApplicationException());

            try
            {
                await TaskUtilities.WhenAllWithCancellationAsync(new[] { task }, CancellationToken.None);
                Assert.True(false, "The method should fail");
            }
            catch (ApplicationException) { }
        }

        [Fact]
        public async Task WhenAllWithCancellationAsync_PropagatesSingleExceptionFromMultipleExceptions()
        {
            var task1 = Task.Run(() => throw new ApplicationException("1"));
            var task2 = Task.Run(() => throw new ApplicationException("2"));

            try
            {
                await TaskUtilities.WhenAllWithCancellationAsync(new[] {task1, task2}, CancellationToken.None);
                Assert.True(false, "The method should fail");
            }
            catch (ApplicationException) { }
        }
        
        [Fact]
        public async Task WhenAllWithCancellationAsync_PropagatesCancellationCorrectly()
        {
            var tcs = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

            var task = Task.Run(async () => await Task.Delay(TimeSpan.FromHours(1)));

            try
            {
                await TaskUtilities.WhenAllWithCancellationAsync(new[] {task}, tcs.Token);
                Assert.True(false, "The method should fail");
            }
            catch (OperationCanceledException) { }
        }

        [Fact]
        public async Task SafeWhenAll_PropagatesBothExceptions()
        {
            var task1 = Task.Run(() => throw new ApplicationException("1"));
            var task2 = Task.Run(() => throw new ApplicationException("2"));

            try
            {
                await TaskUtilities.SafeWhenAll(new[] { task1, task2 });
                Assert.True(false, "The method should fail");
            }
            catch (AggregateException e)
            {
                XAssert.AreEqual(2, e.InnerExceptions.Count);
                XAssert.IsNotNull(e.InnerExceptions.SingleOrDefault(e => e.Message == "1"));
                XAssert.IsNotNull(e.InnerExceptions.SingleOrDefault(e => e.Message == "2"));
            }
        }

        [Fact]
        public Task SafeWhenAll_WrapsSingleExceptionInAggregateException()
        {
            var task1 = Task.Run(() => throw new ApplicationException("1"));

            var task2 = Task.CompletedTask;
            return XAssert.ThrowsAnyAsync<AggregateException>(async () => await TaskUtilities.SafeWhenAll(new[] { task1, task2 }));
        }

        [Fact]
        public async Task WithTimeoutAsync_WorksProperlyWhenCancellationTokenIsTriggered()
        {
            bool cancellationRequested = false;
            var cts = new CancellationTokenSource();

            var task = TaskUtilities.WithTimeoutAsync(
                async token =>
                {
                    try
                    {
                        await Task.Delay(2000, token);
                    }
                    catch(TaskCanceledException) { }

                    // Intentionally ignoring cancellation.
                    cancellationRequested = token.IsCancellationRequested;
                    return 42;
                },
                TimeSpan.FromSeconds(10),
                cts.Token);

            // Given cancellation token should be propagated to the callback and when the cancellation token source
            // is triggered the callback should be able to see that signal.
            cts.Cancel();

            // The task should not fail with timeout here.
            await task;

            XAssert.IsTrue(cancellationRequested);
        }

        [Fact]
        public async Task WithTimeoutAsync_ShouldTriggerGivenCancellationTokenAndFailWithTimeoutException()
        {
            bool cancellationRequested = false;
            var task = TaskUtilities.WithTimeoutAsync(
                async token =>
                {
                    using var registration = token.Register(() => { cancellationRequested = true; });
                    await Task.Delay(/* milliseconds */200000);

                    return 42;
                },
                TimeSpan.FromMilliseconds(10),
                default);

            // Timeout should happen here, and the callback should see that a given cancellation token is triggered
            await Assert.ThrowsAsync<TimeoutException>(async () => await task);
            XAssert.IsTrue(cancellationRequested);
        }

        [Fact]
        public async Task WithTimeoutAsync_ShouldFailWithTimeoutEvenWhenTheOriginalTokenIsCanceled()
        {
            bool methodIsFinished = false;
            var cts = new CancellationTokenSource();

            var task = TaskUtilities.WithTimeoutAsync(
                async token =>
                {
                    await Task.Delay(20000);
                    methodIsFinished = true;
                    return 42;
                },
                TimeSpan.FromMilliseconds(10),
                cts.Token);

            // The callback ignores the cancellation token, but the task still should fail with timeout
            // and methodIsFinished variable still should be false.
            await Assert.ThrowsAsync<TimeoutException>(async () => await task);
            XAssert.IsFalse(methodIsFinished, "The callback should not be finished or interrupted.");
        }

        [Fact]
        public async Task FromException_ReturnsTheRightException()
        {
            var toThrow = new BuildXLException("Got exception?");
            try
            {
                await TaskUtilities.FromException<int>(toThrow);
            }
            catch (BuildXLException ex)
            {
                XAssert.AreSame(toThrow, ex);
                return;
            }

            XAssert.Fail("Expected an exception");
        }

        private int ThrowNull()
        {
            throw new NullReferenceException();
        }

        private static int ThrowDivideByZero()
        {
            throw new DivideByZeroException();
        }

        [Fact]
        public async Task SafeWhenAll_PropagatesAllExceptions()
        {
            try
            {
                await TaskUtilities.SafeWhenAll(
                    new[]
                    {
                        (Task)Task.Run(() => ThrowNull()),
                        (Task)Task.Run(() => ThrowDivideByZero())
                    });
            }
            catch (AggregateException aggregateException)
            {
                XAssert.IsNotNull(aggregateException.InnerExceptions.OfType<NullReferenceException>().FirstOrDefault());
                XAssert.IsNotNull(aggregateException.InnerExceptions.OfType<DivideByZeroException>().FirstOrDefault());
            }
        }

        [Fact]
        public async Task SafeWhenAll_Generic()
        {
            try
            {
                await TaskUtilities.SafeWhenAll<int>(
                    new[]
                    {
                        Task.Run(() => ThrowNull()),
                        Task.Run(() => ThrowDivideByZero())
                    });
            }
            catch (AggregateException aggregateException)
            {
                XAssert.IsNotNull(aggregateException.InnerExceptions.OfType<NullReferenceException>().FirstOrDefault());
                XAssert.IsNotNull(aggregateException.InnerExceptions.OfType<DivideByZeroException>().FirstOrDefault());
            }
        }

        [Fact]
        public async Task Test_WhenDoneAsync_RespectsDegreeOfParallelism()
        {
            await Task.Yield();

            // Making sure that WhenDoneAsync starts all the tasks
            int degreeOfParallelism = 42;
            int[] input = Enumerable.Range(0, degreeOfParallelism + 1).ToArray();

            await ParallelAlgorithms.WhenDoneAsync(
                degreeOfParallelism: 42,
                cancellationToken: CancellationToken.None,
                action: async (scheduleItem, item) =>
                        {
                            
                            await Task.Yield();
                        },
                input);
        }

        [Fact]
        public async Task Test_WhenDoneAsync_KeepsTheParallelism()
        {
            // This test makes sure that the 'WhenDoneAsync' runs all the correct degree of parallelism.
            int degreeOfParallelism = 3;

            int pendingCallbacks = 0;
            int maxPendingCallbacks = 0;

            var allItemsWereAddedTaskSource = TaskSourceSlim.Create<object>();

            int warmUpLimit = 1000;

            var random = new Random(42);

            await ParallelAlgorithms.WhenDoneAsync(
                degreeOfParallelism: degreeOfParallelism,
                cancellationToken: default,
                action: async (scheduler, item) =>
                        {
                            if (item == 42)
                            {
                                // Once we process some amount of items we want to add more items that taking longer time to process
                                // in order to make sure all the processors are busy.
                                for (int i = 0; i < degreeOfParallelism * 10; i++)
                                {
                                    // Scheduling a bunch of new work to process with the offset of 'warmUpLimit' to separate new work from the original work.
                                    scheduler(warmUpLimit + i);
                                }

                                // Now we wait for all the other processors to get stuck and wait on the task completion source
                                await ParallelAlgorithmsHelper.WaitUntilOrFailAsync(
                                    () => pendingCallbacks == degreeOfParallelism - 1);

                                // Waking up all the processors regardless of the wait result.
                                // The test will fail in the assert section.
                                allItemsWereAddedTaskSource.SetResult(null);
                            }
                            else
                            {
                                if (item < warmUpLimit)
                                {
                                    // This was an original item. We process it quickly without any other extra steps.
                                    await Task.Delay(random.Next(minValue: 0, maxValue: 5));
                                    return;
                                }

                                // Incrementing the number of pending callbacks to notify the producer about it.
                                var currentPendingCallbacks = Interlocked.Increment(ref pendingCallbacks);

                                ParallelAlgorithms.InterlockedMax(ref maxPendingCallbacks, currentPendingCallbacks);
                                
                                // Waiting for the signal that all the new items were added.
                                await allItemsWereAddedTaskSource.Task;

                                Interlocked.Decrement(ref pendingCallbacks);
                            }
                        },
                items: Enumerable.Range(0, 50).ToArray());

            // The maxPendingCallbacks can be 'degreeOfParallelism - 1' or to be equal to 'degreeOfParallelism'
            // because we do add more work from a callback, meaning that one of the working tasks is occupying a slat as well.
            Assert.InRange(maxPendingCallbacks, low: degreeOfParallelism - 1, high: degreeOfParallelism);
        }
        
        [Fact]
        public async Task Test_WhenDoneAsync_RespectsCancellation()
        {
            // cancel after 2 seconds
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            // run something that never ends in parallel 
            await ParallelAlgorithms.WhenDoneAsync(
                degreeOfParallelism: 20,
                cts.Token,
                action: (scheduleItem, item) =>
                {
                    // keep rescheduling the same item forever
                    scheduleItem(item);
                    return Task.Delay(TimeSpan.FromMilliseconds(10));
                },
                items: Enumerable.Range(0, 1000));

            XAssert.IsTrue(cts.IsCancellationRequested);
        }
    }
}
