// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Scheduler;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Scheduler.Distribution;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Utilities;
using System.Diagnostics;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class ProcessWeightTests : SchedulerIntegrationTestBase
    {
        public ProcessWeightTests(ITestOutputHelper output) : base(output)
        {
            // Turns on logging for most scheduler stats, including the limiting resource stat looked for by these tests
            ShouldLogSchedulerStats = true;
            // If not explicitly set, max processes is a function of the number of cores on the machine
            // Explicitly limit it so that powerful machines don't spawn a bunch of processes
            Configuration.Schedule.MaxProcesses = 5;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)] // < 0 should be treated as 1
        [InlineData(-1)]
        public void CanRunUpToMaxProcessesInParallel(int weight)
        {
            CreateAndScheduleProcessWithWeight(weight, numProcesses: Configuration.Schedule.MaxProcesses);

            RunScheduler().AssertSuccess();
            AssertProcessConcurrencyWeightLimited(false);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)] // < 0 should be treated as 1
        [InlineData(-1)]
        public void CannotRunGreaterThanMaxProcessesInParallel(int weight)
        {
            // Schedule one over max concurrent processes
            CreateAndScheduleProcessWithWeight(weight, numProcesses: Configuration.Schedule.MaxProcesses + 1);

            RunScheduler().AssertSuccess();
            AssertProcessConcurrencyWeightLimited(true);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(99999999)]
        public void WeightsOutsideOfRangeStillRun(int weight)
        {
            CreateAndScheduleProcessWithWeight(weight);
            RunScheduler().AssertSuccess();
        }

        [Fact(Skip = "TODO(1999710): Need to update the test infrastructure based on the chooseworker logic changes ")]
        public void UseProcessWeightToRunAlone()
        {
            CreateAndScheduleProcessWithWeight(Configuration.Schedule.MaxProcesses);
            CreateAndScheduleProcessWithWeight(1);

            RunScheduler().AssertSuccess();
            AssertProcessConcurrencyWeightLimited(true);
        }

        [Fact]
        public void GreaterThanMaxWeightStillRuns()
        {
            CreateAndScheduleProcessWithWeight(Configuration.Schedule.MaxProcesses * 2);
            CreateAndScheduleProcessWithWeight(1);

            RunScheduler().AssertSuccess();
            AssertProcessConcurrencyWeightLimited(true);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]

        public void TestVaryingWeights(bool shouldOverSchedule)
        {
            int sum = 0;
            int w = 1;
            int limit = 0;

            // Schedules pips with monotonically increasing weights of (e.g. 1, 2, 3, ... n)
            while (limit <= Configuration.Schedule.MaxProcesses)
            {
                CreateAndScheduleProcessWithWeight(w);

                sum += w;
                w++;

                limit = shouldOverSchedule ? sum : sum + w;
            }

            RunScheduler().AssertSuccess();
            AssertProcessConcurrencyWeightLimited(shouldOverSchedule);
        }

        [Fact]
        public void TotalAllowedWeightIsLimitedByConfigurationMaxProcesses()
        {
            var maxProcesses = 1;
            Configuration.Schedule.MaxProcesses = maxProcesses;
            CreateAndScheduleProcessWithWeight(1, numProcesses: maxProcesses + 1);

            RunScheduler().AssertSuccess();
            AssertProcessConcurrencyWeightLimited(true);
        }

        [Fact]
        public void WeightTestInDispatcher()
        {
            Configuration.Schedule.MaxProcesses = 4;
            var waitFile = ScheduleWaitingForFilePips(numberOfPips: 2, weight: 2);

            bool correctSlotsAndPips = false;

            var testHook = new SchedulerTestHooks()
            {
                GenerateSyntheticMachinePerfInfo = (loggingContext, scheduler) =>
                {
                    if (scheduler.MaxExternalProcessesRan == 2)
                    {
                        if (scheduler.PipQueue.GetNumAcquiredSlotsByKind(DispatcherKind.CPU) == 4 &&
                            scheduler.PipQueue.GetNumRunningPipsByKind(DispatcherKind.CPU) == 2)
                        {
                            correctSlotsAndPips = true;
                        }

                        // Ensure that the ongoing processes will now finish
                        WriteSourceFile(waitFile);
                    }

                    return default(PerformanceCollector.MachinePerfInfo);
                }
            };

            var schedulerResult = RunScheduler(testHooks: testHook, updateStatusTimerEnabled: true);

            schedulerResult.AssertSuccess();
            AssertTrue(correctSlotsAndPips, "The number of running pips and slots is not correct.");
        }

        [Fact]
        public void WeightTestInDispatcherOverLimit()
        {
            Configuration.Schedule.MaxProcesses = 3;
           
            CreateAndScheduleProcessWithWeight(numProcesses: 2, weight: 2);

            var schedulerResult = RunScheduler(
                verifySchedulerPostRun: ts =>
                {
                    XAssert.AreEqual(1, ts.MaxExternalProcessesRan);
                });

            schedulerResult.AssertSuccess();
        }

        private void CreateAndScheduleProcessWithWeight(int weight, int numProcesses = 1)
        {
            for (int i = 0; i < numProcesses; ++i)
            {
                var builder = CreatePipBuilder(new Operation[]
                {
                    Operation.WriteFile(CreateOutputFileArtifact())
                });

                builder.Weight = weight;
                SchedulePipBuilder(builder);
            }
        }

        private void AssertProcessConcurrencyWeightLimited(bool isWeightLimited)
        {
            var log = EventListener.GetLog();
            XAssert.AreEqual(isWeightLimited, log.Contains(WorkerResource.AvailableProcessSlots.Name));
        }
    }
}
