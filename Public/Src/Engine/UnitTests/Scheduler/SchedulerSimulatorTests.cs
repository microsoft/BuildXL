// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using PipInfo = BuildXL.Scheduler.SchedulerSimulator.PipInfo;
using static BuildXL.Scheduler.SchedulerSimulator;

namespace Test.BuildXL.Scheduler
{
    public class SchedulerSimulatorTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(50, 1)]
        [InlineData(100, 1)]
        [InlineData(149, 1)]
        [InlineData(150, 2)]
        [InlineData(250, 2)]
        [InlineData(800, 8)]
        [InlineData(1600, 16)]
        public void CpuWeightMatchesLiveScheduler(int cpuUsageInPercent, int expectedWeight)
        {
            if (OperatingSystemHelper.IsLinuxOS)
            {
                expectedWeight = Math.Min(expectedWeight, 10);
            }

            Assert.Equal(expectedWeight, ProcessPipHistoricPerfData.ComputeCpuWeight(cpuUsageInPercent));
            Assert.Equal(expectedWeight, new ProcessPipInfo(cpuUsageInPercent).CpuWeight);
        }

        [Theory]
        [InlineData(50, 4, 1000)]
        [InlineData(100, 4, 1000)]
        [InlineData(200, 4, 2000)]
        [InlineData(800, 4, 4000)]
        [InlineData(800, 16, 2000)]
        [InlineData(1600, 4, 4000)]
        public void CpuDemandLimitsConcurrency(int cpuUsageInPercent, int executeSlots, int expectedDurationMs)
        {
            var (graph, pips) = CreateProcesses(Enumerable.Repeat(cpuUsageInPercent, 4).ToArray());

            double durationMin = RunSimulation(pips, graph, workerCount: 1, memoryLimitMb: 100, executeSlots, cacheLookupSlots: 4);

            Assert.Equal(expectedDurationMs / 60000.0, durationMin);
            AssertResourcesReleased(pips);
        }

        [Fact]
        public void WeightedProcessesCanShareWorkerCapacity()
        {
            var (graph, pips) = CreateProcesses(300, 100, 200);

            double durationMin = RunSimulation(pips, graph, workerCount: 1, memoryLimitMb: 100, executeSlots: 4, cacheLookupSlots: 3);

            Assert.Equal(2000 / 60000.0, durationMin);
            Assert.Equal(1000, pips[1].EndTime);
            Assert.Equal(1000, pips[2].EndTime);
            Assert.Equal(2000, pips[3].EndTime);
            AssertResourcesReleased(pips);
        }

        [Theory]
        [InlineData(20, 1000)]
        [InlineData(10, 2000)]
        [InlineData(5, 2000)]
        [InlineData(0, 2000)]
        public void MemoryDemandLimitsConcurrencyAndOversizedPipsMakeProgress(int memoryLimitMb, int expectedDurationMs)
        {
            var (graph, pips) = CreateProcesses(100, 100);

            double durationMin = RunSimulation(pips, graph, workerCount: 1, memoryLimitMb, executeSlots: 4, cacheLookupSlots: 2);

            Assert.Equal(expectedDurationMs / 60000.0, durationMin);
            AssertResourcesReleased(pips);
        }

        [Fact]
        public void OversizedMemoryWaitsForIdleWorkerAndReleasesFullReservation()
        {
            var worker = new Worker(memoryLimitMb: 10, executeSlots: 4, cacheLookupSlots: 1);
            var small = new ProcessPipInfo { Phase = PipPhase.Executable, MemoryUsageMb = 1 };
            var large = new ProcessPipInfo { Phase = PipPhase.Executable, MemoryUsageMb = 20 };
            var zeroMemory = new ProcessPipInfo { Phase = PipPhase.Executable, MemoryUsageMb = 0 };

            Assert.True(worker.TryAcquireWorker(small));
            Assert.False(worker.TryAcquireWorker(large));
            Assert.Equal(9, worker.AvailableMemory);
            worker.ReleaseExecutionResources(small);
            Assert.True(worker.TryAcquireWorker(large));
            Assert.Equal(-10, worker.AvailableMemory);
            Assert.False(worker.TryAcquireWorker(zeroMemory));
            worker.ReleaseExecutionResources(large);
            Assert.Equal(10, worker.AvailableMemory);
            Assert.Equal(4, worker.AvailableExecuteSlots);
            Assert.True(worker.TryAcquireWorker(small));
            worker.ReleaseExecutionResources(small);
        }

        [Fact]
        public void OversizedMemoryCanUseMultipleWorkersAcrossReusedSimulations()
        {
            var (graph, source) = CreateProcesses(100, 100);
            foreach (var process in source.OfType<ProcessPipInfo>())
            {
                process.MemoryUsageMb = 100;
            }

            var pips = ClonePips(source);
            Assert.All(pips.OfType<ProcessPipInfo>(), process => Assert.Equal(100, process.MemoryUsageMb));
            Assert.Equal(1000 / 60000.0, RunSimulation(pips, graph, workerCount: 2, memoryLimitMb: 10, executeSlots: 4, cacheLookupSlots: 2));
            AssertResourcesReleased(pips);

            ResetPipStates(pips);
            Assert.Equal(2000 / 60000.0, RunSimulation(pips, graph, workerCount: 1, memoryLimitMb: 10, executeSlots: 4, cacheLookupSlots: 2));
            AssertResourcesReleased(pips);
        }

        [Fact]
        public void WeightedProcessesCanUseMultipleWorkers()
        {
            var (graph, pips) = CreateProcesses(800, 800, 800, 800);

            double durationMin = RunSimulation(pips, graph, workerCount: 2, memoryLimitMb: 100, executeSlots: 4, cacheLookupSlots: 4);

            Assert.Equal(2000 / 60000.0, durationMin);
            Assert.Equal(2, pips.OfType<ProcessPipInfo>().Select(pip => pip.Worker).Distinct().Count());
            AssertResourcesReleased(pips);
        }

        [Fact]
        public void FailedAcquisitionDoesNotReservePartialResources()
        {
            var worker = new Worker(memoryLimitMb: 100, executeSlots: 4, cacheLookupSlots: 1);
            var running = new ProcessPipInfo(300) { Phase = PipPhase.Executable, MemoryUsageMb = 10 };
            var waiting = new ProcessPipInfo(200) { Phase = PipPhase.Executable, MemoryUsageMb = 10 };
            var memoryLimited = new ProcessPipInfo { Phase = PipPhase.Executable, MemoryUsageMb = 100 };

            Assert.True(worker.TryAcquireWorker(running));
            Assert.False(worker.TryAcquireWorker(waiting));
            Assert.False(worker.TryAcquireWorker(memoryLimited));
            Assert.Equal(1, worker.AvailableExecuteSlots);
            Assert.Equal(90, worker.AvailableMemory);
            Assert.Equal(3, running.AcquiredExecuteSlots);
            Assert.Equal(0, waiting.AcquiredExecuteSlots);
            Assert.Null(waiting.Worker);
            Assert.Equal(0, memoryLimited.AcquiredExecuteSlots);
            Assert.Null(memoryLimited.Worker);
        }

        [Fact]
        public void CacheHitsDoNotReserveExecuteSlotsOrConsumeCpuTime()
        {
            var (graph, pips) = CreateProcesses(800);
            var process = (ProcessPipInfo)pips[1];
            process.IsCacheHit = true;
            process.CacheLookupDurationMs = 1000;
            var worker = new Worker(memoryLimitMb: 100, executeSlots: 4, cacheLookupSlots: 1);

            Assert.True(worker.TryAcquireWorker(process));
            Assert.Equal(4, worker.AvailableExecuteSlots);
            Assert.Equal(100, worker.AvailableMemory);
            Assert.Equal(0, worker.AvailableCacheLookupSlots);
            Assert.Equal(0, process.AcquiredExecuteSlots);
            Assert.Equal(0, process.EstimatedCpuTimeMs);

            ResetPipStates(pips);
            double durationMin = RunSimulation(pips, graph, workerCount: 1, memoryLimitMb: 100, executeSlots: 4, cacheLookupSlots: 1);

            Assert.Equal(1000 / 60000.0, durationMin);
            AssertResourcesReleased(pips);
        }

        [Fact]
        public void WeightedExecutionPreservesDependencies()
        {
            var (graph, pips) = CreateProcesses(800, 800);
            graph.AddEdge(pips[1].NodeId, pips[2].NodeId);
            pips[2].NumIncomingEdges = 1;

            double durationMin = RunSimulation(pips, graph, workerCount: 2, memoryLimitMb: 100, executeSlots: 8, cacheLookupSlots: 2);

            Assert.Equal(2000 / 60000.0, durationMin);
            Assert.Equal(pips[1].EndTime, pips[2].StartTime);
            AssertResourcesReleased(pips);
        }

        [Fact]
        public void ClonedAndReusedPipsPreserveCpuDemandAcrossConfigurations()
        {
            var (graph, source) = CreateProcesses(200, 200, 200);
            var pips = ClonePips(source);

            Assert.NotSame(source[1], pips[1]);
            Assert.Equal(200, ((ProcessPipInfo)pips[1]).CpuUsageInPercent);
            Assert.Equal(3000 / 60000.0, RunSimulation(pips, graph, workerCount: 1, memoryLimitMb: 100, executeSlots: 2, cacheLookupSlots: 3));
            AssertResourcesReleased(pips);

            ResetPipStates(pips);

            foreach (var process in pips.OfType<ProcessPipInfo>())
            {
                Assert.Equal(PipPhase.CacheLookup, process.Phase);
                Assert.Equal(200, process.CpuUsageInPercent);
                Assert.Equal(0, process.AcquiredExecuteSlots);
                Assert.Null(process.Worker);
            }

            Assert.Equal(2000 / 60000.0, RunSimulation(pips, graph, workerCount: 1, memoryLimitMb: 100, executeSlots: 4, cacheLookupSlots: 3));
            AssertResourcesReleased(pips);
            Assert.All(source.OfType<ProcessPipInfo>(), process => Assert.Equal(PipPhase.CacheLookup, process.Phase));
        }

        [Theory]
        [InlineData(50, 500, 2, 25)]
        [InlineData(100, 1000, 2, 50)]
        [InlineData(200, 2000, 2, 100)]
        [InlineData(800, 8000, 8, 100)]
        public void UtilizationUsesCpuTimeRatherThanRoundedSlotWeight(int cpuUsageInPercent, double expectedCpuTimeMs, int cpus, int expectedUtilization)
        {
            var process = new ProcessPipInfo(cpuUsageInPercent) { ExecuteDurationMs = 1000 };

            Assert.Equal(expectedCpuTimeMs, process.EstimatedCpuTimeMs);
            Assert.Equal(expectedUtilization, CalculateCoreUtilization(process.EstimatedCpuTimeMs, durationMin: 1000 / 60000.0, workerCount: 1, cpus));
            Assert.Equal(0, CalculateCoreUtilization(process.EstimatedCpuTimeMs, durationMin: 0, workerCount: 1, cpus));
        }

        [Theory]
        [InlineData(50, 0.5)]
        [InlineData(150, 1.5)]
        [InlineData(1000, 10)]
        [InlineData(1600, 16)]
        [InlineData(65535, 655.35)]
        public void CpuTimeAndWeightUseSharedCoreEstimate(int cpuUsageInPercent, double expectedCores)
        {
            if (OperatingSystemHelper.IsLinuxOS)
            {
                expectedCores = Math.Min(expectedCores, 10);
            }

            var process = new ProcessPipInfo(cpuUsageInPercent) { ExecuteDurationMs = 1000 };

            Assert.Equal(expectedCores, ProcessPipHistoricPerfData.ComputeCpuUsageInCores(cpuUsageInPercent));
            Assert.Equal(expectedCores * 1000, process.EstimatedCpuTimeMs);
            Assert.Equal(Math.Max(1, (int)Math.Round(expectedCores)), process.CpuWeight);
        }

        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public void LinuxCpuTimeAndWeightApplyTheSameMeasurementCap()
        {
            var capped = new ProcessPipInfo(1000) { ExecuteDurationMs = 1000 };
            var excessive = new ProcessPipInfo(1600) { ExecuteDurationMs = 1000 };

            Assert.Equal(10, excessive.CpuWeight);
            Assert.Equal(capped.CpuWeight, excessive.CpuWeight);
            Assert.Equal(10000, excessive.EstimatedCpuTimeMs);
            Assert.Equal(capped.EstimatedCpuTimeMs, excessive.EstimatedCpuTimeMs);
            Assert.Equal(100, CalculateCoreUtilization(excessive.EstimatedCpuTimeMs, durationMin: 1000 / 60000.0, workerCount: 1, cpus: 10));
        }

        [Fact]
        public void DefaultCpuEstimatePreservesSingleCoreBehavior()
        {
            var process = new ProcessPipInfo { ExecuteDurationMs = 1000 };

            Assert.Equal(1, process.CpuWeight);
            Assert.Equal(1000, process.EstimatedCpuTimeMs);
        }

        [Fact]
        public void ZeroHistoricalCpuUsageFallsBackToSingleCore()
        {
            var process = new ProcessPipInfo(cpuUsageInPercent: 0) { ExecuteDurationMs = 1000 };

            Assert.Equal(100, process.CpuUsageInPercent);
            Assert.Equal(1, process.CpuWeight);
            Assert.Equal(1000, process.EstimatedCpuTimeMs);
        }

        private static (MutableDirectedGraph Graph, PipInfo[] Pips) CreateProcesses(params int[] cpuUsageInPercent)
        {
            var graph = new MutableDirectedGraph();
            var pips = new PipInfo[cpuUsageInPercent.Length + 1];
            for (int i = 0; i < cpuUsageInPercent.Length; i++)
            {
                var node = graph.CreateNode();
                pips[node.Value] = new ProcessPipInfo(cpuUsageInPercent[i])
                {
                    NodeId = node,
                    ExecuteDurationMs = 1000,
                    MemoryUsageMb = 10,
                };
            }

            return (graph, pips);
        }

        private static void AssertResourcesReleased(PipInfo[] pips)
        {
            foreach (var process in pips.OfType<ProcessPipInfo>())
            {
                Assert.Equal(PipPhase.Completed, process.Phase);
                Assert.Equal(0, process.AcquiredExecuteSlots);
                Assert.Equal(process.Worker.TotalExecuteSlots, process.Worker.AvailableExecuteSlots);
                Assert.Equal(process.Worker.TotalMemory, process.Worker.AvailableMemory);
                Assert.Equal(process.Worker.TotalCacheLookupSlots, process.Worker.AvailableCacheLookupSlots);
            }
        }
    }
}
