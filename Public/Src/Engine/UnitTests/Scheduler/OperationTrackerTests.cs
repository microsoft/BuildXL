// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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
using Xunit.Abstractions;

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
    }
}
