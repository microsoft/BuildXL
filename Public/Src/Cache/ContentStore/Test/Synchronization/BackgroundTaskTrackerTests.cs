// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStoreTest.Test;
using BuildXL.Utilities.Tasks;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Synchronization
{
    public class BackgroundTaskTrackerTests : TestBase
    {
        public BackgroundTaskTrackerTests()
            : base(TestGlobal.Logger)
        {
        }

        private static BackgroundTaskTracker CreateBackgroundTaskTracker()
        {
            return new BackgroundTaskTracker("Test", new Context(TestGlobal.Logger));
        }

        [Fact(Skip = "Failed on RunCheckInTests or Rolling build")]
        public async Task IsBackground()
        {
            var context = new Context(Logger);
            using (var runner = CreateBackgroundTaskTracker())
            {
                Assert.True(!runner.InShutdown);
                Stopwatch stopWatch = Stopwatch.StartNew();
                runner.Add(async () => await Task.Delay(TimeSpan.FromMilliseconds(10)));
                Assert.True(stopWatch.Elapsed.TotalSeconds < 1);
                Assert.True(!runner.InShutdown);
                await runner.Synchronize();
                await runner.ShutdownAsync(context);
                Assert.True(runner.InShutdown);
                Assert.True(stopWatch.Elapsed.TotalMilliseconds >= 10, "Should have taken at least as long as the added task");
                Assert.True(stopWatch.Elapsed.TotalSeconds < 30, "Should not have timed out");
            }
        }

        [Fact]
        public async Task NoAddsOnceStopped()
        {
            var context = new Context(Logger);
            using (var runner = CreateBackgroundTaskTracker())
            {
                runner.Add(async () => await Task.Delay(TimeSpan.FromMilliseconds(10)));

                Assert.True(!runner.InShutdown);
                await runner.ShutdownAsync(context);

                Assert.True(runner.InShutdown);

                Exception exception = null;
                try
                {
                    runner.Add(Task.FromResult(0));
                }
                catch (Exception caughtException)
                {
                    Assert.True(runner.InShutdown);
                    exception = caughtException;
                }

                Assert.NotNull(exception);
            }
        }

        [SuppressMessage("AsyncUsage", "AsyncFixer04:DisposableObjectUsedInFireForgetAsyncCall")]
        [Fact]
        public async Task NoAddsOnceStopping()
        {
            var context = new Context(Logger);
            using (var runner = CreateBackgroundTaskTracker())
            {
                var completion = TaskSourceSlim.Create<int>();

                runner.Add(completion.Task);

                Task stopTask = runner.ShutdownAsync(context);

                Exception exception = null;
                try
                {
                    runner.Add(Task.FromResult(0));
                }
                catch (Exception caughtException)
                {
                    Assert.True(runner.InShutdown);
                    exception = caughtException;
                }

                Assert.NotNull(exception);

                Task.Run(() => completion.SetResult(0)).Should().NotBeNull();

                await stopTask;
            }
        }

        [Fact]
        public async Task ThrowingTaskIsCaught()
        {
            var context = new Context(Logger);
            using (var runner = CreateBackgroundTaskTracker())
            {
                runner.Add(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10));
                    throw new Exception();
                });

                await runner.ShutdownAsync(context);
            }
        }

        [SuppressMessage("AsyncUsage", "AsyncFixer02:AwaitShouldBeUsedInsteadOfSyncTaskWait")]
        [SuppressMessage("AsyncUsage", "AsyncFixer04:DisposableObjectUsedInFireForgetAsyncCall")]
        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task SynchronizeWaitsForItemsBeforeCall()
        {
            var context = new Context(Logger);
            using (var runner = CreateBackgroundTaskTracker())
            {
                var completion1 = TaskSourceSlim.Create<int>();
                runner.Add(completion1.Task);

                var syncTask = runner.Synchronize();

                var stopTask = runner.ShutdownAsync(context);

                Assert.False(syncTask.Wait(TimeSpan.FromMilliseconds(10)));

                Task.Run(() => completion1.SetResult(0)).Should().NotBeNull();

                Assert.True(syncTask.Wait(TimeSpan.FromMilliseconds(1000)));
                await stopTask;
            }
        }

        [SuppressMessage("AsyncUsage", "AsyncFixer02:AwaitShouldBeUsedInsteadOfSyncTaskWait")]
        [SuppressMessage("AsyncUsage", "AsyncFixer04:DisposableObjectUsedInFireForgetAsyncCall")]
        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task SynchronizeDoesNotWaitForItemsAfterCall()
        {
            var context = new Context(Logger);
            using (var runner = CreateBackgroundTaskTracker())
            {
                var completion1 = TaskSourceSlim.Create<int>();

                var syncTask = runner.Synchronize();
                runner.Add(completion1.Task);

                var stopTask = runner.ShutdownAsync(context);

                Assert.True(syncTask.Wait(TimeSpan.FromMilliseconds(1000)));

                Task.Run(() => completion1.SetResult(0)).Should().NotBeNull();

                await stopTask;
            }
        }
    }
}
