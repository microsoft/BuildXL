// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Scheduler.Distribution;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Tests that a worker's RAM semaphore limit is sized from its own <see cref="Worker.RamSemaphoreMultiplier"/>.
    /// A remote worker reports its <c>/ramSemaphoreMultiplier</c> at attach time, so a worker launched with a
    /// different multiplier than the orchestrator is honored per machine (rather than every worker using the
    /// orchestrator's global value).
    /// </summary>
    public sealed class WorkerRamSemaphoreTests : XunitBuildXLTest
    {
        private const int Ram100Gb = 100 * 1024;

        public WorkerRamSemaphoreTests(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>
        /// Minimal concrete <see cref="Worker"/> used to exercise the RAM semaphore limit computation in isolation.
        /// </summary>
        private sealed class TestWorker : Worker
        {
            public TestWorker(IScheduleConfiguration scheduleConfig, PipExecutionContext context)
                : base(workerId: 0, context, scheduleConfig)
            {
            }
        }

        private static TestWorker CreateWorker(double configuredMultiplier)
        {
            var schedule = new ScheduleConfiguration { RamSemaphoreMultiplier = configuredMultiplier };
            return new TestWorker(schedule, BuildXLContext.CreateInstanceForTesting());
        }

        [Fact]
        public void WorkerDefaultsToConfiguredMultiplier()
        {
            using var worker = CreateWorker(configuredMultiplier: 0.9);

            // Without an attach-time override, the worker uses the (orchestrator's) configured multiplier.
            XAssert.AreEqual(0.9, worker.RamSemaphoreMultiplier);

            worker.UpdatePerfInfo(
                LoggingContext,
                currentTotalRamMb: Ram100Gb,
                machineAvailableRamMb: Ram100Gb,
                engineRamMb: 0,
                engineCpuUsage: 0,
                machineCpuUsage: 0);

            XAssert.AreEqual((int)Math.Round((double)Ram100Gb * 0.9), worker.RamSemaphoreLimitMb);
        }

        [Fact]
        public void WorkerHonorsPerWorkerMultiplierOverride()
        {
            // Orchestrator configured 0.9, but this (remote) worker reported 0.8 at attach time.
            using var worker = CreateWorker(configuredMultiplier: 0.9);
            worker.RamSemaphoreMultiplier = 0.8;

            worker.UpdatePerfInfo(
                LoggingContext,
                currentTotalRamMb: Ram100Gb,
                machineAvailableRamMb: Ram100Gb,
                engineRamMb: 0,
                engineCpuUsage: 0,
                machineCpuUsage: 0);

            // The limit must reflect the worker's own multiplier (0.8), not the orchestrator's (0.9).
            XAssert.AreEqual((int)Math.Round((double)Ram100Gb * 0.8), worker.RamSemaphoreLimitMb);
        }

        [Fact]
        public void RamSemaphoreLimitAddsEngineRamToAvailableBeforeApplyingMultiplier()
        {
            // InitialAvailableRamMb = machineAvailableRamMb + engineRamMb; the multiplier applies to that sum.
            using var worker = CreateWorker(configuredMultiplier: 0.9);
            worker.RamSemaphoreMultiplier = 0.5;

            // engineCpuUsage null keeps the engine-pip semaphore update a no-op while still adding engine RAM.
            worker.UpdatePerfInfo(
                LoggingContext,
                currentTotalRamMb: 128 * 1024,
                machineAvailableRamMb: 100 * 1024,
                engineRamMb: 20 * 1024,
                engineCpuUsage: null,
                machineCpuUsage: 0);

            XAssert.AreEqual(120 * 1024, worker.InitialAvailableRamMb.Value);
            XAssert.AreEqual((int)Math.Round(120.0 * 1024 * 0.5), worker.RamSemaphoreLimitMb);
        }
    }
}
