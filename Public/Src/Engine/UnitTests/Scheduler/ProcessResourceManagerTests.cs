// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public sealed class ProcessResourceManagerTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        private int m_nextId;

        public ProcessResourceManagerTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public async Task TestResourceManagerCancellationPreference()
        {
            ProcessResourceManager resourceManager = new ProcessResourceManager(null);

            var workItem1 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1);

            // Highest RAM usage over estimate
            var workItem2 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1002);

            // Higest overall RAM usage, second most recently executed (cancelled second)
            var workItem3 = CreateWorkItem(resourceManager, estimatedRamUsage: 1000, reportedRamUsage: 2000);

            // Most recently executed (Cancelled second)
            var workItem4 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1);

            var workItem4Cancelled = workItem4.WaitForCancellation();
            RunResourceManager(resourceManager, requiredSizeMb: 1, mode: ManageMemoryMode.CancellationRam);

            // Ensure only work item 4 was cancelled since that is all that is required to free necessary RAM
            XAssert.IsTrue(await workItem4Cancelled);
            workItem1.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem2.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem3.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem4.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);

            var workItem3Cancelled = workItem3.WaitForCancellation();
            RunResourceManager(resourceManager, requiredSizeMb: 1000, mode: ManageMemoryMode.CancellationRam);

            // Ensure only work item 3 was cancelled since that is all that is required to free necessary RAM
            XAssert.IsTrue(await workItem3Cancelled);
            workItem1.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem2.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem3.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
            workItem4.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
        }

        [Fact]
        public async Task TestResourceManagerCancellationPreferenceMultiple()
        {
            ProcessResourceManager resourceManager = new ProcessResourceManager(null);

            // Highest RAM, but oldest pip so it is not cancelled
            var workItem1 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 10000);

            var workItem2 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1002);

            var workItem3 = CreateWorkItem(resourceManager, estimatedRamUsage: 1000, reportedRamUsage: 2000);

            var workItem4 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1);

            var workItem3Cancelled = workItem3.WaitForCancellation();
            var workItem2Cancelled = workItem2.WaitForCancellation();
            var workItem4Cancelled = workItem4.WaitForCancellation();

            // Attempt to free more RAM than occupied by all work items (oldest pips should be retained even though
            // it must be freed to attempt to meet resource requirements)
            RunResourceManager(resourceManager, requiredSizeMb: 20000, mode: ManageMemoryMode.CancellationRam);

            // Ensure only work item 2 AND 3 were cancelled since that is all that is required to free necessary RAM
            XAssert.IsTrue(await workItem2Cancelled);
            XAssert.IsTrue(await workItem3Cancelled);
            XAssert.IsTrue(await workItem4Cancelled);
            workItem1.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem2.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
            workItem3.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
            workItem4.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
        }

        [Theory]
        [InlineData(1, ServicePipKind.None)]
        [InlineData(3, ServicePipKind.ServiceClient)]
        [InlineData(1, ServicePipKind.ServiceShutdown)]
        [InlineData(1, ServicePipKind.ServiceFinalization)]
        public async Task TestSuspendedProcessRecoveryIgnoresServices(int suspendedCount, ServicePipKind activeKind)
        {
            var resourceManager = new ProcessResourceManager(LoggingContext);
            var service = CreateWorkItem(resourceManager, allowCancellation: false, serviceKind: ServicePipKind.Service);
            var active = CreateWorkItem(
                resourceManager,
                allowCancellation: false,
                serviceKind: activeKind,
                servicePipDependency: service.Scope.PipId);
            var suspended = new List<ResourceManagerWorkItemTracker>();

            try
            {
                for (int i = 0; i < suspendedCount; i++)
                {
                    var workItem = CreateWorkItem(resourceManager, reportedRamUsage: 10);
                    workItem.Scope.RefreshMemoryCounters();
                    workItem.Scope.IsSuspended = true;
                    suspended.Add(workItem);
                }

                resourceManager.RefreshMemoryCounters();
                XAssert.AreEqual(1, resourceManager.NumActive);
                XAssert.IsFalse(resourceManager.ShouldCancelSuspendedPipForProgress());

                active.ExecutionCompletionSource.TrySetResult(0);
                await active.ExecutionTask;

                resourceManager.RefreshMemoryCounters();
                XAssert.AreEqual(0, resourceManager.NumActive);
                var cancellations = suspended.Select(item => item.WaitForCancellation(timeoutMs: 10000)).ToArray();

                XAssert.IsTrue(resourceManager.ShouldCancelSuspendedPipForProgress());
                resourceManager.TryManageResources(1, ManageMemoryMode.CancellationRam);
                var cancellation = await Task.WhenAny(cancellations);
                XAssert.IsTrue(await cancellation);
                XAssert.AreEqual(1, suspended.Sum(item => item.CancellationCount));
                service.Verify(expectedCancellationCount: 0);
                active.Verify(expectedCancellationCount: 0, expectedCompleted: true);

                resourceManager.RefreshMemoryCounters();
                XAssert.AreEqual(suspendedCount - 1, resourceManager.NumSuspended);
            }
            finally
            {
                var workItems = suspended.Concat(new[] { active, service }).ToArray();
                foreach (var workItem in workItems)
                {
                    workItem.ExecutionCompletionSource.TrySetResult(0);
                }

                foreach (var workItem in workItems)
                {
                    try
                    {
                        await workItem.ExecutionTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Recovery cancels one of these tasks.
                    }
                }
            }
        }

        private void RunResourceManager(ProcessResourceManager resourceManager, int requiredSizeMb, ManageMemoryMode mode)
        {
            resourceManager.RefreshMemoryCounters();
            resourceManager.TryManageResources(requiredSizeMb, mode);
        }

        private ResourceManagerWorkItemTracker CreateWorkItem(
            ProcessResourceManager resourceManager,
            int estimatedRamUsage = 0,
            bool allowCancellation = true,
            int reportedRamUsage = 1,
            ServicePipKind serviceKind = ServicePipKind.None,
            PipId servicePipDependency = default)
        {
            return new ResourceManagerWorkItemTracker(
                LoggingContext, 
                resourceManager, 
                (uint)Interlocked.Increment(ref m_nextId), 
                estimatedRamUsage, 
                allowCancellation,
                serviceKind,
                servicePipDependency)
            { 
                RamUsage = ProcessMemoryCountersSnapshot.CreateFromMB(reportedRamUsage, reportedRamUsage, reportedRamUsage)
            };
        }

        private class ResourceManagerWorkItemTracker
        {
            public const int CancelledResult = -1;

            public int ExecutionCount { get; private set; }
            public int CancellationCount { get; private set; }
            public readonly TaskSourceSlim<int> ExecutionCompletionSource;
            private TaskSourceSlim<Unit> m_cancellationCompletionSource;
            private readonly SemaphoreSlim m_startSemaphore = new SemaphoreSlim(0, 1);
            public Task<int> ExecutionTask { get; private set; }
            public ProcessResourceManager.ResourceScope Scope { get; private set; }
            private readonly Func<Task<int>> m_execute;

            public ProcessMemoryCountersSnapshot RamUsage = ProcessMemoryCountersSnapshot.CreateFromMB(1, 1, 1);

            public ResourceManagerWorkItemTracker(
                LoggingContext loggingContext,
                ProcessResourceManager resourceManager,
                uint id,
                int estimatedRamUsage,
                bool allowCancellation,
                ServicePipKind serviceKind,
                PipId servicePipDependency)
            {
                ExecutionCompletionSource = TaskSourceSlim.Create<int>();
                m_cancellationCompletionSource = TaskSourceSlim.Create<Unit>();
                var context = BuildXLContext.CreateInstanceForTesting();
                var process = SchedulerTest.CreateDummyProcess(context, new PipId(id));
                if (serviceKind != ServicePipKind.None)
                {
                    process = process.Override(serviceInfo: new ServiceInfo(
                        serviceKind,
                        servicePipDependencies: serviceKind == ServicePipKind.ServiceClient
                            ? ReadOnlyArray<PipId>.From(new[] { servicePipDependency })
                            : ReadOnlyArray<PipId>.Empty,
                        shutdownProcessPipId: serviceKind == ServicePipKind.Service ? new PipId(id + 1) : PipId.Invalid,
                        monikerId: serviceKind == ServicePipKind.Service
                            ? StringId.Create(context.StringTable, "service")
                            : StringId.Invalid));
                    process.PipId = new PipId(id);
                }

                m_execute = () => ExecutionTask = resourceManager.ExecuteWithResourcesAsync(
                    OperationContext.CreateUntracked(loggingContext),
                    process,
                    ProcessMemoryCounters.CreateFromMb(estimatedRamUsage, estimatedRamUsage),
                    allowCancellation,
                    async (resourceScope) =>
                {
                    Scope = resourceScope;
                    m_startSemaphore.Release();
                    resourceScope.RegisterQueryRamUsageMb(() => RamUsage);

                    ExecutionCount++;
                    var currrentCancellationCompletionSource = m_cancellationCompletionSource;
                    using var registration = resourceScope.Token.Register(() =>
                    {
                        XAssert.IsTrue(m_startSemaphore.Wait(millisecondsTimeout: 100000));

                        CancellationCount++;
                        m_cancellationCompletionSource = TaskSourceSlim.Create<Unit>();
                        currrentCancellationCompletionSource.TrySetResult(Unit.Void);
                    });

                    var result = await Task.WhenAny(currrentCancellationCompletionSource.Task, ExecutionCompletionSource.Task);

                    resourceScope.Token.ThrowIfCancellationRequested();

                    XAssert.IsTrue(ExecutionCompletionSource.Task.IsCompleted, "This task should be completed since the cancellation task implies the cancellation token would throw in the preceding line of code.");

                    return ExecutionCompletionSource.Task.Result;
                });

                StartExecute();
            }

            public void StartExecute()
            {
                Analysis.IgnoreResult(
                    m_execute(),
                    justification: "Fire and Forget"
                );
            }

            public async Task WaitAndVerifyRestarted(int timeoutMs = 100000)
            {
                var result = await m_startSemaphore.WaitAsync(millisecondsTimeout: timeoutMs);
                XAssert.IsTrue(result);

                // Immediately release. We just needed to verify that the semaphore could be acquired
                m_startSemaphore.Release();
            }

            public async Task<bool> WaitForCancellation(int timeoutMs = 100000)
            {
                var cancellationCompletionSource = m_cancellationCompletionSource;
                Analysis.IgnoreResult(
                    await Task.WhenAny(Task.Delay(timeoutMs), cancellationCompletionSource.Task)
                );

                return cancellationCompletionSource.Task.IsCompleted;
            }

            public void Verify(int? expectedCancellationCount = null, int? expectedExecutionCount = null, bool? expectedCompleted = null)
            {
                if (expectedCancellationCount != null)
                {
                    XAssert.AreEqual(expectedCancellationCount.Value, CancellationCount);
                }

                if (expectedExecutionCount != null)
                {
                    XAssert.AreEqual(expectedExecutionCount.Value, ExecutionCount);
                }

                if (expectedCompleted != null)
                {
                    XAssert.AreEqual(expectedCompleted.Value, ExecutionTask.Status == TaskStatus.RanToCompletion);
                }
            }
        }
    }
}
