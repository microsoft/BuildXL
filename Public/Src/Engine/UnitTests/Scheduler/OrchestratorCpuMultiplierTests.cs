// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public sealed class OrchestratorCpuMultiplierTests : TemporaryStorageTestBase
    {
        public OrchestratorCpuMultiplierTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(true, null)]
        [InlineData(true, 0.0)]
        [InlineData(true, 0.7)]
        [InlineData(true, 1.0)]
        [InlineData(false, null)]
        [InlineData(false, 0.0)]
        [InlineData(false, 0.7)]
        public void ExplicitLimitControlsCpuDispatcher(bool historical, double? multiplier)
        {
            var configuration = new ConfigurationImpl();
            configuration.Schedule.MaxProcesses = 20;
            configuration.Schedule.UseHistoricalCpuThrottling = historical;
            configuration.Schedule.OrchestratorCpuMultiplier = multiplier;
            bool semaphoreOnly = historical && !(multiplier > 0);

            using var queue = new PipQueue(LoggingContext, configuration);
            XAssert.AreEqual(semaphoreOnly ? int.MaxValue : 20, queue.GetMaxParallelDegreeByKind(DispatcherKind.CPU));

            foreach (int slots in new[] { 14, 4, 0, 20 })
            {
                queue.SetMaxParallelDegreeByKind(DispatcherKind.CPU, slots);
                XAssert.AreEqual(
                    semaphoreOnly ? int.MaxValue : Math.Max(1, slots),
                    queue.GetMaxParallelDegreeByKind(DispatcherKind.CPU));
            }

            queue.SetMaxParallelDegreeByKind(DispatcherKind.IO, 3);
            XAssert.AreEqual(3, queue.GetMaxParallelDegreeByKind(DispatcherKind.IO));
        }

        [Theory]
        [InlineData(true, 0.7, false, false, 1, false, false)]
        [InlineData(true, 0.7, true, false, 1, false, true)]
        [InlineData(true, 0.7, false, true, 1, false, true)]
        [InlineData(true, null, false, false, 1, false, true)]
        [InlineData(true, 0.0, false, false, 1, false, true)]
        [InlineData(false, 0.7, false, false, 1, false, true)]
        [InlineData(true, 0.7, false, false, 0, false, false)]
        [InlineData(true, 0.7, false, false, 3, true, false)]
        public void ExplicitHistoricalLimitPreventsLocalOversubscription(
            bool historical, double? multiplier, bool remote, bool light, int slots, bool cpuSaturated, bool expectSecondAcquisition)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var configuration = ConfigurationHelpers.GetDefaultForTesting(
                context.PathTable, AbsolutePath.Create(context.PathTable, Path.Combine(TemporaryDirectory, "config.dsc")));
            configuration.Schedule.MaxProcesses = slots;
            configuration.Schedule.MaxLight = 1;
            configuration.Schedule.UseHistoricalCpuThrottling = historical;
            configuration.Schedule.OrchestratorCpuMultiplier = multiplier;

            using var queue = new PipQueue(LoggingContext, configuration);
            using var worker = remote
                ? (Worker)new TestRemoteWorker(context, configuration.Schedule)
                : new LocalWorker(configuration.Schedule, queue, detoursListener: null, context);
            worker.UpdatePerfInfo(LoggingContext, currentTotalRamMb: 16384, machineAvailableRamMb: 16384,
                engineRamMb: 0, engineCpuUsage: null, machineCpuUsage: 0);

            using var pipTable = new PipTable(context.PathTable, context.SymbolTable, initialBufferSize: 1024,
                maxDegreeOfParallelism: 1, debug: false);
            var environment = new DummyPipExecutionEnvironment(LoggingContext, context, configuration, pipTable: pipTable);
            for (uint id = 1; id <= 2; id++)
            {
                var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, Path.Combine(TemporaryDirectory, "tool.exe")));
                var process = new ProcessBuilder()
                    .WithExecutable(executable)
                    .WithWorkingDirectory(AbsolutePath.Create(context.PathTable, TemporaryDirectory))
                    .WithStandardDirectory(AbsolutePath.Create(context.PathTable, TemporaryDirectory))
                    .WithDependencies(new HashSet<FileArtifact> { executable })
                    .WithContext(context)
                    .WithOptions(light ? Process.Options.IsLight : Process.Options.None)
                    .WithArguments(PipDataBuilder.CreatePipData(context.StringTable, " ", PipDataFragmentEscaping.NoEscaping, ""))
                    .Build();
                var pipId = pipTable.Add(id, process);
                var runnable = (ProcessRunnablePip)RunnablePip.Create(
                    LoggingContext, environment, pipId, PipType.Process, 0, (Func<RunnablePip, Task>)null, 0);
                runnable.HistoricPerfData = new ProcessPipHistoricPerfData(
                    new ProcessPipExecutionPerformance(
                        PipExecutionLevel.Executed, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(1),
                        FingerprintUtilities.ZeroFingerprint, TimeSpan.FromSeconds(1), default, default,
                        cpuSaturated ? TimeSpan.FromHours(1) : TimeSpan.FromMilliseconds(10),
                        TimeSpan.Zero, ProcessMemoryCounters.CreateFromMb(1, 1),
                        1, workerId: 0, suspendedDurationMs: 0, pushOutputsToCacheDurationMs: 0),
                    runDurationInMs: 1000);

                bool acquired = worker.TryAcquireProcess(runnable, out var limitingResource, loadFactor: 10);
                XAssert.AreEqual(id == 1 || expectSecondAcquisition, acquired);
                if (!acquired)
                {
                    XAssert.AreEqual(
                        cpuSaturated ? WorkerResource.AvailableCpu : WorkerResource.AvailableProcessSlots,
                        limitingResource.Value);
                }
            }
        }

        private sealed class TestRemoteWorker : Worker
        {
            public TestRemoteWorker(PipExecutionContext context, IScheduleConfiguration schedule)
                : base(workerId: 1, context, schedule)
            {
                TotalProcessSlots = schedule.EffectiveMaxProcesses;
                TotalLightProcessSlots = schedule.MaxLight;
                TotalMaterializeInputSlots = schedule.MaxMaterialize;
                Start();
            }
        }
    }
}
