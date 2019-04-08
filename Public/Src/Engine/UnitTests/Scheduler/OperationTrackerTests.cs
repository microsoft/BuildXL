// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
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

            using (var globalContext = operationTracker.StartOperation(PipExecutorCounter.PipRunningStateDuration, new PipId(15234), PipType.Process, log))
            using (globalContext.StartOperation(PipExecutorCounter.ExecutePipStepDuration))
            {
                For(length, i =>
                {
                    using (var context = operationTracker.StartOperation(PipExecutorCounter.PipRunningStateDuration, log))
                    using (context.StartOperation(PipExecutorCounter.ExecutePipStepDuration))
                    using (var outerContext = context.StartAsyncOperation(PipExecutorCounter.FileContentManagerTryMaterializeOuterDuration))
                    using (outerContext.StartOperation(PipExecutorCounter.FileContentManagerTryMaterializeDuration))
                    {
                    }

                    using (var outerContext = globalContext.StartAsyncOperation(PipExecutorCounter.FileContentManagerTryMaterializeOuterDuration))
                    using (outerContext.StartOperation(PipExecutorCounter.FileContentManagerTryMaterializeDuration))
                    {
                    }
                }, parallel);
            }
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
