// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Pips.Builders;
using BuildXL.Processes.Remoting;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace ExternalToolTest.BuildXL.Scheduler
{
    public sealed class RemoteExecutionTests : SchedulerIntegrationTestBase
    {
        public RemoteExecutionTests(ITestOutputHelper output) : base(output)
        {
            ShouldLogSchedulerStats = true;
            Configuration.Schedule.EnableProcessRemoting = true;
            RemoteProcessManagerFactory.RemoteProcessManager = new Lazy<IRemoteProcessManager>(() => new TestRemoteProcessManager(shouldRunLocally: false));
        }

        [Fact]
        public void RunSingleProcessRemotely()
        {
            // Force run remotely.
            Configuration.Schedule.RemotingThresholdMultiplier = 0.0;

            ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) });
            ProcessWithOutputs process = SchedulePipBuilder(builder);
            ScheduleRunResult result = RunScheduler().AssertSuccess();
            XAssert.AreEqual(1, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRunRemoteProcesses));
            XAssert.AreEqual(0, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRunLocallyProcessesOnRemotingWorker));
            XAssert.AreEqual(0, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRemoteFallbackRetries));

            RunScheduler().AssertCacheHit(process.Process.PipId);
        }

        [Fact]
        public void RunMultipleProcessesLocallyAndRemotely()
        {
            Configuration.Schedule.RemotingThresholdMultiplier = 1;
            Configuration.Schedule.NumOfRemoteAgentLeases = 2;
            Configuration.Schedule.MaxProcesses = 1;

            for (int i = 0; i < 5; ++i)
            {
                ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) });
                SchedulePipBuilder(builder);
            }
            
            ScheduleRunResult result = RunScheduler().AssertSuccess();
            XAssert.IsTrue(result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRunRemoteProcesses) > 0);
            XAssert.IsTrue(result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRunLocallyProcessesOnRemotingWorker) > 0);
            XAssert.AreEqual(0, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRemoteFallbackRetries));
        }

        [Fact]
        public void AllMultipleProcessesRunLocally()
        {
            Configuration.Schedule.RemotingThresholdMultiplier = 4;
            Configuration.Schedule.NumOfRemoteAgentLeases = 2;
            Configuration.Schedule.MaxProcesses = 1;

            for (int i = 0; i < 5; ++i)
            {
                ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) });
                SchedulePipBuilder(builder);
            }

            ScheduleRunResult result = RunScheduler().AssertSuccess();
            XAssert.AreEqual(0, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRunRemoteProcesses));
            XAssert.AreEqual(5, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRunLocallyProcessesOnRemotingWorker));
            XAssert.AreEqual(0, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRemoteFallbackRetries));
        }

        [Fact]
        public void ProcessMustRunLocalDueToTag()
        {
            // Force run remotely.
            Configuration.Schedule.RemotingThresholdMultiplier = 0.0;

            // This configuration will test that must-run-local tags take precendence over can-run-remote tags.
            const string MustRunLocalTag = nameof(MustRunLocalTag);
            Configuration.Schedule.ProcessMustRunLocalTags = new List<string> { MustRunLocalTag };
            Configuration.Schedule.ProcessCanRunRemoteTags = new List<string> { MustRunLocalTag };

            ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) });
            builder.AddTags(Context.StringTable, MustRunLocalTag);

            ProcessWithOutputs process = SchedulePipBuilder(builder);
            ScheduleRunResult result = RunScheduler().AssertSuccess();
            XAssert.AreEqual(0, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRunRemoteProcesses));
            XAssert.AreEqual(1, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRunLocallyProcessesOnRemotingWorker));
            XAssert.AreEqual(0, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRemoteFallbackRetries));
        }

        [Fact]
        public void RemoteFallbackProcessRetryToRunLocally()
        {
            RemoteProcessManagerFactory.RemoteProcessManager = new Lazy<IRemoteProcessManager>(() => new TestRemoteProcessManager(shouldRunLocally: true));

            // Force run remotely.
            Configuration.Schedule.RemotingThresholdMultiplier = 0.0;

            ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) });
            ProcessWithOutputs process = SchedulePipBuilder(builder);

            ScheduleRunResult result = RunScheduler().AssertSuccess();
            XAssert.AreEqual(1, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRunRemoteProcesses));
            XAssert.AreEqual(1, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRunLocallyProcessesOnRemotingWorker));
            XAssert.AreEqual(1, result.PipExecutorCounters.GetCounterValue(global::BuildXL.Scheduler.PipExecutorCounter.TotalRemoteFallbackRetries));
        }
    }
}
