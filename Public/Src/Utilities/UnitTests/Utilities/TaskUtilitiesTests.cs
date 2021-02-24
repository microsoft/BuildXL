// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class TaskUtilitiesTests
    {
        [Fact]
        public async Task WhenAllWithCancellationShouldNotCauseUnobservedTaskExceptions()
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
        public async Task WhenAllWithCancellationPropagatesSingleExceptionCorrectly()
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
        public async Task WhenAllWithCancellationPropagatesSingleExceptionFromMultipleExceptions()
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
        public async Task WhenAllWithCancellationPropagatesCancellationCorrectly()
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

        [Theory]
        [InlineData(true)]
        [InlineData(true)]
        public async Task SafeWhenAllPropagatesBothExceptions(bool wrapSingleException)
        {
            var task1 = Task.Run(() => throw new ApplicationException("1"));
            var task2 = Task.Run(() => throw new ApplicationException("2"));

            try
            {
                await TaskUtilities.SafeWhenAll(new[] {task1, task2}, wrapSingleException);
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
        public async Task SafeWhenAllPropagatesSingleExceptionIfWrapSingleExceptionIsFalse()
        {
            var task1 = Task.Run(() => throw new ApplicationException("1"));

            var task2 = Task.CompletedTask;
            await XAssert.ThrowsAnyAsync<AggregateException>(async() => await TaskUtilities.SafeWhenAll(new[] { task1, task2 }, wrapSingleException: true));
            await XAssert.ThrowsAnyAsync<ApplicationException>(async () => await TaskUtilities.SafeWhenAll(new[] { task1, task2 }, wrapSingleException: false));
        }

        [Fact]
        public async Task WithTimeoutWorksProperlyWhenCancellationTokenIsTriggered()
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
        public async Task WithTimeoutShouldTriggerGivenCancellationTokenAndFailWithTimeoutException()
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
        public async Task WithTimeoutShouldFailWithTimeoutEvenWhenTheOriginalTokenIsCanceled()
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
        public async Task FromException()
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
        public async Task SafeWhenAll()
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
        public async Task SafeWhenAllGeneric()
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
        public async Task TestParallelAlgorithmsCancellationTokenAsync()
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
