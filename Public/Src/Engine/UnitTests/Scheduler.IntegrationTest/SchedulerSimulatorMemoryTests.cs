// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace IntegrationTest.BuildXL.Scheduler
{
    [TestClassIfSupported(requiresSandbox: true)]
    public sealed class SchedulerSimulatorMemoryTests : SchedulerIntegrationTestBase
    {
        public SchedulerSimulatorMemoryTests(ITestOutputHelper output) : base(output)
        {
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(false)]
        [InlineData(true)]
        public void RecordsMeasuredMemoryForSimulation(bool useDynamicScheduler)
        {
            Configuration.Engine.UnsafeEnableDynamicGraph = useDynamicScheduler;
            Configuration.Cache.CacheGraph = !useDynamicScheduler;
            var pips = new[] { 0, 3000 }.Select(duration => CreateAndSchedulePipBuilder(new[]
            {
                Operation.Sleep(duration),
                Operation.WriteFile(CreateOutputFileArtifact()),
            })).ToArray();
            var measuredMemory = new int[pips.Length];
            var simulatorMemory = new int[pips.Length];
            using var collector = new PerformanceCollector(TimeSpan.FromMilliseconds(100));
            RunScheduler(performanceCollector: collector, updateStatusTimerEnabled: true, verifySchedulerPostRun: scheduler =>
            {
                var history = useDynamicScheduler
                    ? ((TestDynamicScheduler)scheduler).HistoricPerfDataTable
                    : ((TestScheduler)scheduler).HistoricPerfDataTable;
                for (int i = 0; i < pips.Length; i++)
                {
                    var process = pips[i].Process;
                    measuredMemory[i] = history[process.SemiStableHash].MemoryCounters.AverageWorkingSetMb;
                    simulatorMemory[i] = scheduler.RunData.RunnablePipPerformanceInfos[process.PipId].ActualAverageWorkingSetMb;
                }
            }).AssertSuccess().AssertCacheMiss(pips.Select(pip => pip.Process.PipId).ToArray());

            Assert.True(measuredMemory[1] > 0, "The long-running process must span working-set samples.");
            Assert.Equal(measuredMemory, simulatorMemory);

            var cached = RunScheduler().AssertSuccess().AssertCacheHit(pips.Select(pip => pip.Process.PipId).ToArray());
            foreach (var pip in pips)
            {
                Assert.Equal(0, cached.RunData.RunnablePipPerformanceInfos[pip.Process.PipId].ActualAverageWorkingSetMb);
            }
        }
    }
}
