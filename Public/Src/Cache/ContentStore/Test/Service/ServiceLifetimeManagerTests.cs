// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Service
{
    public class ServiceLifetimeManagerTests : TestBase
    {
        private const string InterruptableServiceId = "Interruptable";
        private const string InterrupterServiceId = "Interrupter";
        private static readonly TimeSpan LifetimeManagerPollInterval = TimeSpan.FromMilliseconds(1);
        public ServiceLifetimeManagerTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task TestTriggerShutdown()
        {
            using var testDirectory = new DisposableDirectory(_fileSystem.Value);
            var manager = Create(testDirectory.Path, out var context);

            var interruptableServiceTask = manager.RunInterruptableServiceAsync(context, InterruptableServiceId, async token =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return false;
                }
                catch (TaskCanceledException)
                {
                    return true;
                }
            });

            await Task.Delay(5);

            await manager.ShutdownServiceAsync(context, InterruptableServiceId);

            // It is possible that the manager shuts down before the service recognizes interruption.
            // Waiting a few polling intervals to make sure the service stopped.
            await Task.Delay(LifetimeManagerPollInterval.Multiply(50));
            interruptableServiceTask.IsCompleted.Should().BeTrue();
            var interruptableServiceResult = await interruptableServiceTask;
            interruptableServiceResult.Should().BeTrue("Service should be completed due to cancellation caused by trigger shutdown");
        }

        private enum ServiceResult
        {
            Completed,
            Cancelled
        }

        [Fact]
        public async Task TestInterruption()
        {
            using var testDirectory = new DisposableDirectory(_fileSystem.Value);
            var manager = Create(testDirectory.Path, out var context);

            var interruptableServiceTask = manager.RunInterruptableServiceAsync(context, InterruptableServiceId, async token =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return ServiceResult.Completed;
                }
                catch (TaskCanceledException)
                {
                    return ServiceResult.Cancelled;
                }
            });

            await Task.Delay(5);

            interruptableServiceTask.IsCompleted.Should().BeFalse();

            bool isInterruptorCanceled = false;

            var interruptorStart = new TaskCompletionSource<Unit>();
            var interruptorCompletion = new TaskCompletionSource<ServiceResult>();

            var interruptorTask = manager.RunInterrupterServiceAsync(context, InterrupterServiceId, InterruptableServiceId, async token =>
            {
                interruptableServiceTask.IsCompleted.Should().BeTrue();
                var interruptableServiceResult = await interruptableServiceTask;
                interruptableServiceResult.Should().Be(ServiceResult.Cancelled, "Service should be completed due to cancellation caused by interrupter");
                interruptorStart.SetResult(Unit.Void);

                try
                {
                    using var registration = token.Register(() =>
                    {
                        isInterruptorCanceled = true;
                        interruptorCompletion.SetCanceled();
                    });
                    return await interruptorCompletion.Task;
                }
                catch (TaskCanceledException)
                {
                    return ServiceResult.Cancelled;
                }
            });


            // Wait for start of interruptor before launching second interruptable. This ensure the first interruptable
            // has shutdown and that logic to prevent startup of interruptable has already been activated
            await interruptorStart.Task;

            // Start a second interruptable to verify it does not start until interruptor is completed
            var secondInterruptableStart = new TaskCompletionSource<Unit>();

            var secondInterruptableTask = manager.RunInterruptableServiceAsync(context, InterruptableServiceId, async token =>
            {
                try
                {
                    secondInterruptableStart.SetResult(Unit.Void);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return false;
                }
                catch (TaskCanceledException)
                {
                    return true;
                }
            });

            await Task.Delay(5);
            secondInterruptableTask.IsCompleted.Should().BeFalse("Interruptor should not be completed until signaled");
            secondInterruptableStart.Task.IsCompleted.Should().BeFalse("Interruptable should not start until interruptor is completed");
            interruptorCompletion.SetResult(ServiceResult.Completed);
            var interruptorResult = await interruptorTask;
            interruptorResult.Should().Be(ServiceResult.Completed, "Interruptor service should not be cancelled");

            await secondInterruptableStart.Task;

            // Check that cancellation was never triggered on the interruptor (either before or after
            // the completion is set).
            isInterruptorCanceled.Should().BeFalse("Interruptor service should not be cancelled");
        }

        private ServiceLifetimeManager Create(AbsolutePath testDirectoryPath, out OperationContext context)
        {
            // Don't allow tests to run over 30 seconds
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            context = new OperationContext(new Context(Logger), cts.Token);
            return new ServiceLifetimeManager(testDirectoryPath, pollingInterval: LifetimeManagerPollInterval);
        }
    }
}
