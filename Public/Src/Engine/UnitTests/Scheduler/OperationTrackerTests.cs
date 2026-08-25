// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.ParallelAlgorithms;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    using Scheduler = global::BuildXL.Scheduler.Scheduler;

    /// <summary>
    /// Tests for OperationTracker
    /// </summary>
    [Trait("Category", "OperationTrackerTests")]
    public class OperationTrackerTests : XunitBuildXLTest
    {
        public OperationTrackerTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestOperations()
        {
            TestOperationsHelper(parallel: false);
        }

        [Fact]
        public void TestOperationsParallel()
        {
            Environment.SetEnvironmentVariable("BuildXLTraceOperation", "1");
            TestOperationsHelper(parallel: true);
        }

        [Fact]
        public void AssociatedOperationsRetainTopUniqueOperations()
        {
            var tracker = new OperationTracker(new LoggingContext("test"));
            OperationKind kind = PipExecutorCounter.ExecutePipStepDuration;
            var aggregateCounter = new OperationTracker.Counter(kind);
            var counter = new OperationTracker.StackCounter(kind, parent: null, aggregateCounter);
            var operations = Enumerable.Range(0, OperationTracker.MaxTopOperations + 2)
                .Select(_ => new OperationTracker.RootOperation(tracker))
                .ToArray();

            Assert.Equal(0, counter.AssociatedOperations.Capacity);

            for (int i = 0; i < operations.Length; i++)
            {
                counter.AddAssociatedOperation(CreateCapturedOperation(operations[i], i + 1));
            }

            var retainedOperation = operations[OperationTracker.MaxTopOperations + 1];
            counter.AddAssociatedOperation(CreateCapturedOperation(retainedOperation, 1));
            Assert.Equal(
                OperationTracker.MaxTopOperations + 2L,
                counter.AssociatedOperations.Single(operation => ReferenceEquals(operation.Operation, retainedOperation)).Duration.Ticks);

            counter.AddAssociatedOperation(
                CreateCapturedOperation(retainedOperation, OperationTracker.MaxTopOperations + 5));
            Assert.Equal(
                OperationTracker.MaxTopOperations + 5L,
                counter.AssociatedOperations.Single(operation => ReferenceEquals(operation.Operation, retainedOperation)).Duration.Ticks);

            // An operation that was previously evicted can re-enter the retained top operations.
            counter.AddAssociatedOperation(CreateCapturedOperation(operations[0], OperationTracker.MaxTopOperations + 6));
            counter.SortAssociatedOperations();

            var expectedDurations = Enumerable.Range(0, OperationTracker.MaxTopOperations)
                .Select(index => index == 0
                    ? OperationTracker.MaxTopOperations + 6L
                    : index == 1
                        ? OperationTracker.MaxTopOperations + 5L
                        : OperationTracker.MaxTopOperations + 3L - index);

            Assert.Equal(OperationTracker.MaxTopOperations, counter.AssociatedOperations.Count);
            Assert.Equal(OperationTracker.MaxTopOperations, counter.AssociatedOperations.Capacity);
            Assert.Equal(expectedDurations, counter.AssociatedOperations.Select(operation => operation.Duration.Ticks));
            Assert.Equal(
                OperationTracker.MaxTopOperations,
                counter.AssociatedOperations.Select(operation => operation.Operation).Distinct().Count());
        }

        [Fact]
        public void RetainsOnlyActiveOperationsAndCapsPools()
        {
            var tracker = new OperationTracker(new LoggingContext("test"));
            var root = new OperationTracker.RootOperation(tracker);
            root.Initialize(
                PipId.Invalid,
                PipType.Process,
                PipExecutorCounter.PipRunningStateDuration,
                onOperationCompleted: null);

            int operationCount = OperationTracker.MaxPooledOperationsPerType + 2;
            var threads = Enumerable.Range(0, operationCount)
                .Select(_ => root.StartThread(PipExecutorCounter.ExecutePipStepDuration, default, details: null))
                .ToArray();

            Assert.Equal(operationCount, root.GetChildOperationCountsForTesting().Active);

            foreach (var thread in threads)
            {
                thread.Complete();
            }

            var counts = root.GetChildOperationCountsForTesting();
            Assert.Equal(0, counts.Active);
            Assert.Equal(OperationTracker.MaxPooledOperationsPerType, counts.PooledThreads);

            var nestedOperations = new List<OperationTracker.Operation>();
            for (int i = 0; i < operationCount; i++)
            {
                nestedOperations.Add(root.StartNestedOperation(PipExecutorCounter.ExecutePipStepDuration, default, details: null));
            }

            Assert.Equal(operationCount, root.GetChildOperationCountsForTesting().Active);

            for (int i = nestedOperations.Count - 1; i >= 0; i--)
            {
                nestedOperations[i].Complete();
            }

            counts = root.GetChildOperationCountsForTesting();
            Assert.Equal(0, counts.Active);
            Assert.Equal(OperationTracker.MaxPooledOperationsPerType, counts.PooledNested);
        }

        [Fact]
        public void DefersRootPoolingUntilAsyncOperationsReturn()
        {
            var tracker = new OperationTracker(new LoggingContext("test"));

            for (int i = 0; i < 1000; i++)
            {
                var root = new OperationTracker.RootOperation(tracker);
                root.Initialize(
                    PipId.Invalid,
                    PipType.Process,
                    PipExecutorCounter.PipRunningStateDuration,
                    onOperationCompleted: null);

                var thread = root.StartThread(PipExecutorCounter.ExecutePipStepDuration, default, details: null);
                Parallel.Invoke(root.Complete, thread.Complete);

                var counts = root.GetChildOperationCountsForTesting();
                Assert.Equal(0, counts.Active);
                Assert.Equal(0, counts.Returning);
                Assert.False(counts.RootPoolReturnPending);
                Assert.Equal(1, counts.PooledThreads);
            }
        }

        public void TestOperationsHelper(bool parallel)
        {
            LoggingContext log = new LoggingContext("op");
            var operationTracker = new OperationTracker(new LoggingContext("test"));
            int length = 100000;

            var outerPipId = new PipId(15234);
            PipSemitableHash outerHash = HashCodeHelper.Combine(outerPipId.Value, 1L);
            var outerHashHex = outerHash.ToHex();
            using (var globalContext = operationTracker.StartOperation(PipExecutorCounter.PipRunningStateDuration, outerPipId, PipType.Process, log))
            using (var subContext = globalContext.StartOperation(PipExecutorCounter.ExecutePipStepDuration))
            {
                For(length, i =>
                {
                    using (var context = operationTracker.StartOperation(PipExecutorCounter.PipRunningStateDuration, log))
                    using (context.StartOperation(PipExecutorCounter.ExecutePipStepDuration))
                    using (var outerContext = context.StartAsyncOperation(PipExecutorCounter.FileContentManagerTryMaterializeOuterDuration))
                    using (outerContext.StartOperation(PipExecutorCounter.FileContentManagerTryMaterializeDuration))
                    {
                    }

                    Assert.Null(CacheActivityRegistry.GetContextActivityId());

                    Guid? cacheLookupId1 = null;

                    using (PipExecutionStep.CacheLookup.RegisterPipStepCacheActivity(outerHash))
                    using (var context = globalContext.StartAsyncOperation(PipExecutionStep.CacheLookup))
                    {
                        cacheLookupId1 = CacheActivityRegistry.GetContextActivityId();
                        Assert.NotNull(cacheLookupId1);

                        Assert.StartsWith(outerHashHex + PipExecutionStep.CacheLookup.AsCode().ToString("X2"), cacheLookupId1?.ToString("N").ToUpper());

                        using (context.StartOperation(PipExecutorCounter.FileContentManagerTryMaterializeDuration))
                        {
                            Task.Run(() =>
                            {
                                Assert.Equal(cacheLookupId1, CacheActivityRegistry.GetContextActivityId());
                            }).GetAwaiter().GetResult();
                        }

                        Assert.Equal(cacheLookupId1, CacheActivityRegistry.GetContextActivityId());
                    }

                    Assert.Null(CacheActivityRegistry.GetContextActivityId());

                    Guid? materializeInputsId = null;
                    using (PipExecutionStep.MaterializeInputs.RegisterPipStepCacheActivity(outerHash))
                    {
                        materializeInputsId = CacheActivityRegistry.GetContextActivityId();
                        Assert.NotNull(materializeInputsId);

                        Assert.StartsWith(outerHashHex + PipExecutionStep.MaterializeInputs.AsCode().ToString("X2"), materializeInputsId?.ToString("N").ToUpper());

                        using (subContext.StartAsyncOperation(PipExecutorCounter.FileContentManagerTryMaterializeDuration))
                        {
                            Assert.Equal(materializeInputsId, CacheActivityRegistry.GetContextActivityId());
                        }

                        Assert.Equal(materializeInputsId, CacheActivityRegistry.GetContextActivityId());
                    }

                    using (PipExecutionStep.CacheLookup.RegisterPipStepCacheActivity(outerHash))
                    using(var context = globalContext.StartAsyncOperation(PipExecutionStep.CacheLookup))
                    {
                        var cacheLookupId2 = CacheActivityRegistry.GetContextActivityId();
                        Assert.NotNull(cacheLookupId2);
                        Assert.StartsWith(outerHashHex + PipExecutionStep.CacheLookup.AsCode().ToString("X2"), cacheLookupId2?.ToString("N").ToUpper());

                        Assert.NotEqual(cacheLookupId1, cacheLookupId2);
                    }

                    Assert.Null(CacheActivityRegistry.GetContextActivityId());

                    using (var outerContext = globalContext.StartAsyncOperation(PipExecutorCounter.FileContentManagerTryMaterializeOuterDuration))
                    using (outerContext.StartOperation(PipExecutorCounter.FileContentManagerTryMaterializeDuration))
                    {
                    }
                }, parallel);
            }
        }

        [Theory]
        [InlineData("C:\\dir\\", "C:\\\\dir\\\\")]
        [InlineData("{configuration:\"debug\"}", "{configuration:\\\"debug\\\"}")]
        [InlineData("{configuration:'debug'}", "{configuration:\'debug\'}")]
        public void TestSanitizeForJSON(string oldValue, string expectValue)
        {
            var sanitizedDescription = OperationTracker.SanitizeForJSON(oldValue);
            XAssert.AreEqual(expectValue, sanitizedDescription);
        }

        private static void For(int count, Action<int> action, bool parallel)
        {
            if (parallel)
            {
                Parallel.For(0, count, action);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    action(i);
                }
            }
        }

        private static OperationTracker.CapturedOperationInfo CreateCapturedOperation(OperationTracker.Operation operation, long durationTicks)
        {
            return new OperationTracker.CapturedOperationInfo
            {
                Duration = TimeSpan.FromTicks(durationTicks),
                Operation = operation,
            };
        }

    }
}
