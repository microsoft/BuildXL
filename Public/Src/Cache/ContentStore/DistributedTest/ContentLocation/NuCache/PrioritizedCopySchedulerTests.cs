// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class PrioritizedCopySchedulerTests
    {
        [Fact]
        public Task SingleCopyShouldGetScheduled()
        {
            return RunTest(async (context, scheduler) =>
               {
                  // This only schedules the task, but it won't run until the cycle happens.
                  var resultTask = scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(CopyReason.Pin, context, 0, _ => Task.FromResult(new CopyFileResult())));

                   var cycleResult = scheduler.SchedulerCycle(context, 1).ShouldBeSuccess();
                   var result = await resultTask;

                   result.ShouldBeSuccess();
                   result.Value.ShouldBeSuccess();
               });
        }

        [Fact]
        public Task SchedulingIsOrderedAndDoesntOverschedule()
        {
            return RunTest(async (context, scheduler) =>
             {
                 var firstTaskQueueTime = TimeSpan.MinValue;
                 var secondTaskQueueTime = TimeSpan.MinValue;

                 var firstResultTask = scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(CopyReason.Pin, context, 0, arguments =>
                 {
                     firstTaskQueueTime = arguments.Summary.QueueWait;

                     arguments.Summary.PriorityQueueLength.Should().Be(1);
                     return Task.FromResult(new CopyFileResult());
                 }));

                 var secondResultTask = scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(CopyReason.Pin, context, 0, arguments =>
                 {
                     secondTaskQueueTime = arguments.Summary.QueueWait;
                     return Task.FromResult(new CopyFileResult());
                 }));

                 var cycleResult = scheduler.SchedulerCycle(context, 1).ShouldBeSuccess();

                 var firstTaskResult = await firstResultTask;
                 firstTaskResult.ShouldBeSuccess();
                 firstTaskResult.Value.ShouldBeSuccess();

                 secondTaskQueueTime.Should().Be(TimeSpan.MinValue);

                 var secondCycleResult = scheduler.SchedulerCycle(context, 1).ShouldBeSuccess();
                 var secondTaskResult = await secondResultTask;
                 secondTaskResult.ShouldBeSuccess();
                 secondTaskResult.Value.ShouldBeSuccess();

                 secondTaskQueueTime.Should().BeGreaterOrEqualTo(firstTaskQueueTime);
             });
        }

        [Fact]
        public Task SchedulingIsOrderedAcrossPriorities()
        {
            return RunTest(async (context, scheduler) =>
             {
                 var lowPriorityTaskQueueTime = TimeSpan.MinValue;
                 var highPriorityTaskQueueTime = TimeSpan.MinValue;

                // Schedule a lower priority copy followed by a higher priority one. Then check that the higher
                // priority one happens before the lower priority
                var lowPriorityResultTask = scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(CopyReason.Pin, context, 1, arguments =>
                 {
                     lowPriorityTaskQueueTime = arguments.Summary.QueueWait;
                     return Task.FromResult(new CopyFileResult());
                 }));

                 var highPriorityResultTask = scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(CopyReason.Pin, context, 0, arguments =>
                 {
                     highPriorityTaskQueueTime = arguments.Summary.QueueWait;
                     return Task.FromResult(new CopyFileResult());
                 }));

                 var cycleResult = scheduler.SchedulerCycle(context, 1).ShouldBeSuccess();

                 var highPriorityTaskResult = await highPriorityResultTask;
                 highPriorityTaskResult.ShouldBeSuccess();
                 highPriorityTaskResult.Value.ShouldBeSuccess();

                // Low priority task shouldn't have run
                lowPriorityTaskQueueTime.Should().Be(TimeSpan.MinValue);

                 var secondCycleResult = scheduler.SchedulerCycle(context, 1).ShouldBeSuccess();

                 var lowPriorityTaskResult = await lowPriorityResultTask;
                 lowPriorityTaskResult.ShouldBeSuccess();
                 lowPriorityTaskResult.Value.ShouldBeSuccess();

                // Higher priority task's queue time should reflect that it waited less
                highPriorityTaskQueueTime.Should().BeLessOrEqualTo(lowPriorityTaskQueueTime);
             });
        }

        [Fact]
        public Task ThrowingCallbackDoesntMessWithScheduling()
        {
            return RunTest(async (context, scheduler) =>
             {
                 var brokenCopyTask = scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(CopyReason.Pin, context, 1, _ => throw new NotImplementedException()));

                 var workingCopyTask = scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(CopyReason.Pin, context, 1, _ => Task.FromResult(new CopyFileResult())));

                 // Allowing for 4 copies to ensure that quota allows both copies to run simultaneously. We'd need a new cycle otherwise
                 var cycleResult = scheduler.SchedulerCycle(context, 4).ShouldBeSuccess();

                 var goodCopyResult = await workingCopyTask;
                 goodCopyResult.ShouldBeSuccess();
                 goodCopyResult.Value.ShouldBeSuccess();

                 try
                 {
                     var copyResult = await brokenCopyTask;

                     // This should never happen
                     true.Should().BeFalse();
                 }
                 catch (NotImplementedException)
                 {
                 }
             });
        }

        [Fact]
        public Task CycleWithEnoughQuotaShouldScheduleFromEveryPriorityClass()
        {
            return RunTest(async (context, scheduler) =>
            {
                var reasons = Enum.GetValues(typeof(CopyReason))
                                  .Cast<CopyReason>()
                                  .ToArray();
                var attempts = new[] { 0, 1 };
                var sources = Enum.GetValues(typeof(ProactiveCopyLocationSource))
                                  .Cast<ProactiveCopyLocationSource>()
                                  .ToArray();

                var outboundPullTasks =
                    (from reason in reasons
                     from attempt in attempts
                     select scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(reason, context, attempt, _ => Task.FromResult(new CopyFileResult()))))
                    .ToArray();

                var outboundPushTasks =
                    (from reason in reasons
                     from attempt in attempts
                     from source in sources
                     select scheduler.ScheduleOutboundPushAsync(new OutboundPushCopy<CopyFileResult>(reason, context, source, attempt, _ => Task.FromResult(new CopyFileResult()))))
                    .ToArray();

                // High cycle quota, so we can schedule from all priority classes
                var cycleResult = scheduler.SchedulerCycle(context, 2 * (outboundPullTasks.Length + outboundPushTasks.Length)).ShouldBeSuccess();

                // All copies should have completed successfully
                foreach (var request in outboundPullTasks.Concat(outboundPushTasks))
                {
                    var result = await request;
                    result.ShouldBeSuccess();
                    result.Value.ShouldBeSuccess();
                }
            });
        }

        [Fact]
        public Task AsyncSlownessDoesntBlockScheduler()
        {
            // This test won't ever fail, it will just hang
            return RunTest(async (context, scheduler) =>
            {
                var blocker = new SemaphoreSlim(0);
                var slowCopyFactoryTask = scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(CopyReason.Pin, context, 1, async _ =>
                {
                    await blocker.WaitAsync();
                    return new CopyFileResult();
                }));

                var fastCopyFactoryTask = scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(CopyReason.Pin, context, 1, _ => Task.FromResult(new CopyFileResult())));

                // Allowing for 4 copies to ensure that quota allows both copies to run simultaneously. We'd need a new cycle otherwise
                var cycleResult = scheduler.SchedulerCycle(context, 4).ShouldBeSuccess();

                var fastCopyResult = await fastCopyFactoryTask;
                fastCopyResult.ShouldBeSuccess();
                fastCopyResult.Value.ShouldBeSuccess();

                blocker.Release();

                var slowCopyResult = await slowCopyFactoryTask;
                slowCopyResult.ShouldBeSuccess();
                slowCopyResult.Value.ShouldBeSuccess();
            });
        }

        [Fact]
        public Task SyncSlownessDoesntBlockScheduler()
        {
            // This test won't ever fail, it will just hang
            return RunTest(async (context, scheduler) =>
            {
                var blocker = new SemaphoreSlim(0);
                var slowCopyFactoryTask = scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(CopyReason.Pin, context, 1, _ =>
                {
                    blocker.Wait();
                    return Task.FromResult(new CopyFileResult());
                }));

                var fastCopyFactoryTask = scheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(CopyReason.Pin, context, 1, _ => Task.FromResult(new CopyFileResult())));

                // Allowing for 4 copies to ensure that quota allows both copies to run simultaneously. We'd need a new cycle otherwise
                var cycleResult = scheduler.SchedulerCycle(context, 4).ShouldBeSuccess();

                var fastCopyResult = await fastCopyFactoryTask;
                fastCopyResult.ShouldBeSuccess();
                fastCopyResult.Value.ShouldBeSuccess();

                blocker.Release();

                var slowCopyResult = await slowCopyFactoryTask;
                slowCopyResult.ShouldBeSuccess();
                slowCopyResult.Value.ShouldBeSuccess();
            });
        }

        public async Task RunTest(Func<OperationContext, PrioritizedCopyScheduler, Task> func)
        {
            var logger = TestGlobal.Logger;
            var context = new Context(logger);
            var operationContext = new OperationContext(context);

            var configuration = new CopySchedulerConfiguration()
            {
                Type = CopySchedulerType.Prioritized,
                PrioritizedCopySchedulerConfiguration = new PrioritizedCopySchedulerConfiguration(),
            };

            var scheduler = (configuration.Create(context) as PrioritizedCopyScheduler)!;

            // NOTE: We do not startup the scheduler here in order to avoid launching the background task that
            // effectively schedules copies. This is done only for testing purposes.
            try
            {
                await func(operationContext, scheduler);
            }
            finally
            {
                // We do shut it down, so that any ongoing copies get cancelled
                await scheduler.ShutdownAsync(context).ShouldBeSuccess();
            }

        }
    }
}
