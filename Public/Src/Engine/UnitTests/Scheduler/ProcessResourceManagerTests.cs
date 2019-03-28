// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

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
            ProcessResourceManager resourceManager = new ProcessResourceManager();

            var workItem1 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1);

            // Highest RAM usage over estimate
            var workItem2 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1002);

            // Higest overall RAM usage, second most recently executed (cancelled second)
            var workItem3 = CreateWorkItem(resourceManager, estimatedRamUsage: 1000, reportedRamUsage: 2000);

            // Most recently executed (Cancelled second)
            var workItem4 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1);

            var workItem4Cancelled = workItem4.WaitForCancellation();
            resourceManager.TryFreeResources(requiredRamMb: 1);

            // Ensure only work item 4 was cancelled since that is all that is required to free necessary RAM
            XAssert.IsTrue(await workItem4Cancelled);
            workItem1.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem2.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem3.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem4.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);

            var workItem3Cancelled = workItem3.WaitForCancellation();
            resourceManager.TryFreeResources(requiredRamMb: 1000);

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
            ProcessResourceManager resourceManager = new ProcessResourceManager();

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
            resourceManager.TryFreeResources(requiredRamMb: 20000);

            // Ensure only work item 2 AND 3 were cancelled since that is all that is required to free necessary RAM
            XAssert.IsTrue(await workItem2Cancelled);
            XAssert.IsTrue(await workItem3Cancelled);
            XAssert.IsTrue(await workItem4Cancelled);
            workItem1.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem2.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
            workItem3.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
            workItem4.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
        }

        private ResourceManagerWorkItemTracker CreateWorkItem(ProcessResourceManager resourceManager, int estimatedRamUsage = 0, bool allowCancellation = true, int reportedRamUsage = 1)
        {
            return new ResourceManagerWorkItemTracker(resourceManager, (uint)Interlocked.Increment(ref m_nextId), estimatedRamUsage, allowCancellation) { RamUsage = reportedRamUsage };
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
            private readonly Func<Task<int>> m_execute;

            public int RamUsage { get; set; } = 1;

            public ResourceManagerWorkItemTracker(ProcessResourceManager resourceManager, uint id, int estimatedRamUsage, bool allowCancellation)
            {
                ExecutionCompletionSource = TaskSourceSlim.Create<int>();
                m_cancellationCompletionSource = TaskSourceSlim.Create<Unit>();
                m_execute = () => ExecutionTask = resourceManager.ExecuteWithResources(
                    OperationContext.CreateUntracked(Events.StaticContext),
                    new PipId(id),
                    estimatedRamUsage,
                    allowCancellation,
                    async (cancellationToken, registerQueryRamUsage) =>
                {
                    m_startSemaphore.Release();
                    registerQueryRamUsage(() => RamUsage);

                    ExecutionCount++;
                    var currrentCancellationCompletionSource = m_cancellationCompletionSource;
                    cancellationToken.Register(() =>
                    {
                        XAssert.IsTrue(m_startSemaphore.Wait(millisecondsTimeout: 100000));

                        CancellationCount++;
                        m_cancellationCompletionSource = TaskSourceSlim.Create<Unit>();
                        currrentCancellationCompletionSource.TrySetResult(Unit.Void);
                    });

                    var result = await Task.WhenAny(currrentCancellationCompletionSource.Task, ExecutionCompletionSource.Task);

                    cancellationToken.ThrowIfCancellationRequested();

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
