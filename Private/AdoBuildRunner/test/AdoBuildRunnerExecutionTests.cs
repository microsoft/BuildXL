// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner;
using Microsoft.TeamFoundation.Build.WebApi;
using Xunit;

namespace Test.Tool.AdoBuildRunner
{
    public class AdoBuildRunnerExecutionTests : AdoBuildRunnerTestBase
    {
        [Fact]
        public async Task BasicTest()
        {
            var (orchestrator, worker) = CreateOrchestratorWorkerPairBuild();

            var invocationKey = worker.Config.InvocationKey;

            var orchTcs = new TaskCompletionSource();
            orchestrator.MockLauncher.CompletionTask = orchTcs.Task;    // We'll delay the orchestrator so the worker has a chance to 'attach' while the build is running

            worker.Config.WorkerAlwaysSucceeds = false;
            worker.MockLauncher.ReturnCode = 12; // Worker exits with some non-zero return code

            orchestrator.Initialize();
            worker.Initialize();

            var buildArgs = new[] { "/foo", "/bar" };
            var wManager = new BuildManager(worker.RunnerService, worker.BuildExecutor, buildArgs, worker.MockLogger);
            var oManager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);

            var oBuildTask = oManager.BuildAsync();
            var wBuildTask = wManager.BuildAsync();

            var workerReturn = await wBuildTask; // Worker finishes
            orchTcs.SetResult();    // And now let orchestrator finish
            var orchReturn = await oBuildTask;

            // Machines communicated with the API
            var properties = MockApiService.BuildProperties;
            Assert.True(properties.ContainsKey(orchestrator.AdoEnvironment.BuildId));

            var thisBuildProps = properties[orchestrator.AdoEnvironment.BuildId];
            Assert.True(thisBuildProps.ContainsKey(orchestrator.RunnerService.BuildContext.InvocationKey));

            // BuildXL was launched
            Assert.True(orchestrator.MockLauncher.Launched);
            Assert.True(worker.MockLauncher.Launched);

            // Consistent return codes 
            Assert.Equal(orchestrator.MockLauncher.ReturnCode, orchReturn);
            Assert.Equal(worker.MockLauncher.ReturnCode, workerReturn);

            foreach (var arg in buildArgs)
            {
                Assert.Contains(arg, orchestrator.MockLauncher.Arguments);
                Assert.Contains(arg, worker.MockLauncher.Arguments);
            };
        }

        [Fact]
        public async Task TraceInfoTelemetry()
        {
            var poolName = "TestPoolName";
            MockApiService.GetPoolName = () => poolName;
            var orchestrator = CreateOrchestrator();
            MockApiService.AddBuild(orchestrator.AdoEnvironment.BuildId, CreateTestBuild(orchestrator.AdoEnvironment));


            var invocationKey = orchestrator.Config.InvocationKey;
            orchestrator.Initialize();

            var buildArgs = new[] { "/foo", "/bar" };
            var oManager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);
            var oBuildTask = oManager.BuildAsync();
            var orchReturn = await oBuildTask;

            // TraceInfo has some telemetry
            Assert.Contains($"/traceInfo:InvocationKey={invocationKey}", orchestrator.MockLauncher.Arguments);
            Assert.Contains($"/traceInfo:AgentPool={poolName}", orchestrator.MockLauncher.Arguments);
        }

        [Fact]
        public async Task LateWorkerDoesNotLaunchEngine()
        {
            var (orchestrator, worker) = CreateOrchestratorWorkerPairBuild();
            orchestrator.Initialize();
            worker.Initialize();

            var buildArgs = new[] { "/foo", "/bar" };
            var wManager = new BuildManager(worker.RunnerService, worker.BuildExecutor, buildArgs, worker.MockLogger);
            var oManager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);

            // Make the orchestrator run and finish
            await oManager.BuildAsync();
            Assert.True(orchestrator.MockLauncher.Launched);
            Assert.True(orchestrator.MockLauncher.Exited);

            var properties = MockApiService.BuildProperties;
            Assert.True(properties.ContainsKey(orchestrator.AdoEnvironment.BuildId));

            var thisBuildProps = properties[orchestrator.AdoEnvironment.BuildId];
            Assert.True(thisBuildProps.ContainsKey(orchestrator.RunnerService.BuildContext.InvocationKey));

            // The worker now tries to join the build session. But because the orchestrator is done, it won't even run BuildXL
            await wManager.BuildAsync();
            Assert.False(worker.MockLauncher.Launched);
        }

        [Fact]
        public async Task PoolMismatchIsLogged()
        {
            var poolId = 0;
            // Return a different pool for each agent
            MockApiService.GetPoolName = () => $"Pool_{nameof(PoolMismatchIsLogged)}_{Interlocked.Increment(ref poolId)}";

            var (orchestrator, worker) = CreateOrchestratorWorkerPairBuild();

            var invocationKey = worker.Config.InvocationKey;

            var orchTcs = new TaskCompletionSource();
            orchestrator.MockLauncher.CompletionTask = orchTcs.Task;    // We'll delay the orchestrator so the worker has a chance to 'attach' while the build is running

            orchestrator.Initialize();
            worker.Initialize();

            var buildArgs = new[] { "/foo", "/bar" };
            var wManager = new BuildManager(worker.RunnerService, worker.BuildExecutor, buildArgs, worker.MockLogger);
            var oManager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);


            var oBuildTask = oManager.BuildAsync();
            var wBuildTask = wManager.BuildAsync();

            var ct = new CancellationTokenSource();
            var workerReturn = await wBuildTask; // Worker finishes
            orchTcs.SetResult();    // And now let orchestrator finish
            var orchReturn = await oBuildTask;

            // The build is not started and we exit gracefully, logging a warning
            worker.MockLogger.AssertLogContains("is different than the pool the orchestrator is running on");
            Assert.Equal(0, workerReturn);
            Assert.False(worker.MockLauncher.Launched);
        }

        [Fact]
        public async Task PoolMismatchIsBestEffort()
        {
            var poolId = 0;
            MockApiService.GetPoolName = () =>
            {
                if (Interlocked.Increment(ref poolId) == 1)
                {
                    return "PoolName_1";
                }

                // Empty string means "failed to resolve"
                return string.Empty;
            };

            var (orchestrator, worker) = CreateOrchestratorWorkerPairBuild();

            var invocationKey = worker.Config.InvocationKey;

            var orchTcs = new TaskCompletionSource();
            orchestrator.MockLauncher.CompletionTask = orchTcs.Task;

            orchestrator.Initialize();
            worker.Initialize();

            var buildArgs = new[] { "/foo", "/bar" };
            var wManager = new BuildManager(worker.RunnerService, worker.BuildExecutor, buildArgs, worker.MockLogger);
            var oManager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);

            var oBuildTask = oManager.BuildAsync();
            var wBuildTask = wManager.BuildAsync();

            var ct = new CancellationTokenSource();
            var workerReturn = await wBuildTask; // Worker finishes
            orchTcs.SetResult();                 // And now let orchestrator finish
            var orchReturn = await oBuildTask;

            Assert.True(worker.MockLauncher.Launched);

            // Even though there is a 'mismatch', we don't see a warning, because one of the 'GetPoolName' operations failed,
            // so we just skipped the check.
            worker.MockLogger.AssertLogNotContains("is different than the pool the orchestrator is running on");
        }

        private (AgentHarness Orchestrator, AgentHarness Worker) CreateOrchestratorWorkerPairBuild()
        {
            var orchestrator = CreateOrchestrator();
            var worker = CreateWorker();

            // Sanity check - these should be created with the same defaults
            Assert.Equal(worker.Config.InvocationKey, orchestrator.Config.InvocationKey);
            Assert.Equal(worker.AdoEnvironment.BuildId, orchestrator.AdoEnvironment.BuildId);

            // Modify some defaults for the worker so it is a 'distinct' agent
            worker.AdoEnvironment.AgentMachineName = "WorkerTestAgent";
            worker.AdoEnvironment.JobId = "WorkerTestAgentJobId";
            worker.AdoEnvironment.AgentName = "WorkerTestAgentName";

            // Build is 'running' - the API knows about it
            MockApiService.AddBuild(worker.AdoEnvironment.BuildId, CreateTestBuild(worker.AdoEnvironment));

            return (orchestrator, worker);
        }
        
        /// <summary>
        /// Tests that when a build fails, the exit code message is not logged as an ADO error
        /// but failure is signaled via ##vso[task.complete result=Failed] instead.
        /// </summary>
        [Fact]
        public async Task FailedBuildProducesRedundantErrorMessages()
        {
            // Simulate a failed BuildXL run (exit code 1)
            var orchestrator = CreateOrchestrator();
            orchestrator.MockLauncher.ReturnCode = 1;
            MockApiService.AddBuild(orchestrator.AdoEnvironment.BuildId, CreateTestBuild(orchestrator.AdoEnvironment));
            orchestrator.Initialize();

            var buildArgs = new[] { "/foo" };
            var manager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);

            var exitCode = await manager.BuildAsync();

            // Keep exit code so error "bash exited with code 1" will still be there. This keeps the basics working.
            Assert.Equal(1, exitCode);

            // The exit code message is not logged as an ADO error issue
            orchestrator.MockLogger.AssertLogNotContains("task.logissue type=error;");
         
            // The exit code message is still logged, and failure is signaled via task.complete (not via error)
            orchestrator.MockLogger.AssertLogContains("The BuildXL process completed with exit code 1");
            orchestrator.MockLogger.AssertLogContains("##vso[task.complete result=Failed;]");
        }

        /// <summary>
        /// Verifies that a single worker's BuildXL failure does not cascade: the orchestrator and any
        /// other workers in the build must still complete normally. The fast-fail path in
        /// <see cref="AdoBuildRunnerService.WaitForBuildInfo"/> keys off the ORCHESTRATOR's ADO build
        /// status, so a peer worker's exit code must not affect siblings.
        /// </summary>
        [Fact]
        public async Task WorkerFailureDoesNotAffectOrchestratorOrOtherWorkers()
        {
            var orchestrator = CreateOrchestrator();
            var worker1 = CreateWorker(position: 1, totalWorkers: 2);
            var worker2 = CreateWorker(position: 2, totalWorkers: 2);

            // Distinguish worker agents
            worker1.AdoEnvironment.AgentMachineName = "Worker1Agent";
            worker1.AdoEnvironment.JobId = "Worker1JobId";
            worker1.AdoEnvironment.AgentName = "Worker1Name";
            worker2.AdoEnvironment.AgentMachineName = "Worker2Agent";
            worker2.AdoEnvironment.JobId = "Worker2JobId";
            worker2.AdoEnvironment.AgentName = "Worker2Name";

            MockApiService.AddBuild(orchestrator.AdoEnvironment.BuildId, CreateTestBuild(orchestrator.AdoEnvironment));

            // Delay the orchestrator so both workers attach before it completes.
            var orchTcs = new TaskCompletionSource();
            orchestrator.MockLauncher.CompletionTask = orchTcs.Task;

            // Worker1 fails (non-zero BuildXL exit). With WorkerAlwaysSucceeds the agent task is still
            // marked successful, but the BuildXL exit code is preserved in the launcher.
            worker1.MockLauncher.ReturnCode = 1;
            worker1.Config.WorkerAlwaysSucceeds = true;

            // Worker2 succeeds.
            worker2.MockLauncher.ReturnCode = 0;
            worker2.Config.WorkerAlwaysSucceeds = true;

            orchestrator.Initialize();
            worker1.Initialize();
            worker2.Initialize();

            var buildArgs = new[] { "/foo", "/bar" };
            var oManager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);
            var w1Manager = new BuildManager(worker1.RunnerService, worker1.BuildExecutor, buildArgs, worker1.MockLogger);
            var w2Manager = new BuildManager(worker2.RunnerService, worker2.BuildExecutor, buildArgs, worker2.MockLogger);

            var oTask = oManager.BuildAsync();
            var w1Task = w1Manager.BuildAsync();
            var w2Task = w2Manager.BuildAsync();

            var w1Return = await w1Task;
            var w2Return = await w2Task;
            orchTcs.SetResult();
            var oReturn = await oTask;

            // Orchestrator and worker2 are unaffected by worker1's failure.
            Assert.True(orchestrator.MockLauncher.Launched);
            Assert.True(worker1.MockLauncher.Launched);
            Assert.True(worker2.MockLauncher.Launched);
            Assert.Equal(0, oReturn);
            Assert.Equal(0, w2Return);

            // Worker1 surfaced its failure but masked it (WorkerAlwaysSucceeds): exit code is 0 to ADO,
            // and the diagnostic messages confirm the underlying failure was observed.
            Assert.Equal(0, w1Return);
            worker1.MockLogger.AssertLogContains("The build finished with errors in this worker");
            worker1.MockLogger.AssertLogContains("Marking this task as successful");

            // Neither the orchestrator nor the healthy worker saw a fast-fail message.
            orchestrator.MockLogger.AssertLogNotContains("terminated with result");
            worker2.MockLogger.AssertLogNotContains("terminated with result");
        }

        /// <summary>
        /// Orchestrator-status monitor scenario: the worker has received BuildInfo and BuildXL is
        /// running. The orchestrator's ADO build transitions to Failed. The runner must signal
        /// BuildXL via the cross-process orchestrator-termination pipe (it does NOT kill BuildXL),
        /// and the mock BuildXL — wired to react to that signal — must exit cleanly with code 0.
        /// This is the same contract whether or not BuildXL has attached: the watcher in BuildXL
        /// routes both cases through the orchestrator-exit-RPC path.
        /// </summary>
        [Fact]
        public async Task OrchestratorMonitorSignalsWorkerWhenOrchestratorFails()
        {
            var (orchestrator, worker) = CreateOrchestratorWorkerPairBuild();

            // BuildXL on the worker never finishes on its own — only the pipe signal can end this.
            var workerBxlNeverCompletes = new TaskCompletionSource();
            worker.MockLauncher.CompletionTask = workerBxlNeverCompletes.Task;
            // Simulate BuildXL's in-process watcher reacting to the runner's pipe write.
            worker.MockLauncher.ReactToOrchestratorTerminationPipe = true;

            // Delay the orchestrator so it has a chance to publish BuildInfo before "dying".
            var orchTcs = new TaskCompletionSource();
            orchestrator.MockLauncher.CompletionTask = orchTcs.Task;

            // Tighten the monitor poll cadence so this test runs in milliseconds, not minutes.
            worker.MonitorPollInterval = TimeSpan.FromMilliseconds(50);

            orchestrator.Initialize();
            worker.Initialize();

            var buildArgs = new[] { "/foo", "/bar" };
            var oManager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);
            var wManager = new BuildManager(worker.RunnerService, worker.BuildExecutor, buildArgs, worker.MockLogger);

            var oBuildTask = oManager.BuildAsync();
            var wBuildTask = wManager.BuildAsync();

            // Wait until the worker has launched BuildXL — that means it's past WaitForBuildInfo and
            // the monitor is active.
            await TestHelpers.WaitUntilAsync(() => worker.MockLauncher.Launched);

            // Now transition the orchestrator's job in the build timeline to Completed/Failed. The
            // monitor poll will notice on its next tick, the runner will signal BuildXL through the
            // orchestrator-termination pipe, and the mock BuildXL will react and exit.
            MockApiService.SetOrchestratorJobState(
                orchestrator.AdoEnvironment.BuildId,
                Guid.Parse(orchestrator.AdoEnvironment.JobId),
                TimelineRecordState.Completed,
                TaskResult.Failed);

            var workerReturn = await wBuildTask;

            // Worker exited cleanly with code 0. The runner signaled BuildXL via the pipe (not via kill).
            Assert.Equal(0, workerReturn);
            Assert.True(worker.MockLauncher.PipeSignaled, "Runner should have written to the orchestrator-termination pipe.");
            worker.MockLogger.AssertLogContains("reached terminal state 'Failed'");
            worker.MockLogger.AssertLogContains("Signaling BuildXL to exit cleanly via orchestrator-termination pipe");

            // Let the orchestrator finish to clean up.
            orchTcs.SetResult();
            await oBuildTask;
        }

        /// <summary>
        /// Orchestrator-status monitor scenario: same as the Failed case, but the orchestrator's
        /// ADO build transitions to Canceled. The monitor must treat Canceled identically to Failed
        /// — signal BuildXL via the orchestrator-termination pipe and let BuildXL exit cleanly —
        /// and the log message must reflect the Canceled state. Guards the <see cref="BuildResult.Canceled"/>
        /// mapping in <c>AdoBuildRunnerService.GetOrchestratorStateAsync</c>.
        /// </summary>
        [Fact]
        public async Task OrchestratorMonitorSignalsWorkerWhenOrchestratorCanceled()
        {
            var (orchestrator, worker) = CreateOrchestratorWorkerPairBuild();

            var workerBxlNeverCompletes = new TaskCompletionSource();
            worker.MockLauncher.CompletionTask = workerBxlNeverCompletes.Task;
            worker.MockLauncher.ReactToOrchestratorTerminationPipe = true;

            var orchTcs = new TaskCompletionSource();
            orchestrator.MockLauncher.CompletionTask = orchTcs.Task;

            worker.MonitorPollInterval = TimeSpan.FromMilliseconds(50);

            orchestrator.Initialize();
            worker.Initialize();

            var buildArgs = new[] { "/foo", "/bar" };
            var oManager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);
            var wManager = new BuildManager(worker.RunnerService, worker.BuildExecutor, buildArgs, worker.MockLogger);

            var oBuildTask = oManager.BuildAsync();
            var wBuildTask = wManager.BuildAsync();

            await TestHelpers.WaitUntilAsync(() => worker.MockLauncher.Launched);

            // Transition the orchestrator's job in the build timeline to Completed/Canceled. The
            // monitor poll must map TaskResult.Canceled to OrchestratorState.Canceled and signal
            // the worker via the orchestrator-termination pipe.
            MockApiService.SetOrchestratorJobState(
                orchestrator.AdoEnvironment.BuildId,
                Guid.Parse(orchestrator.AdoEnvironment.JobId),
                TimelineRecordState.Completed,
                TaskResult.Canceled);

            var workerReturn = await wBuildTask;

            Assert.Equal(0, workerReturn);
            Assert.True(worker.MockLauncher.PipeSignaled, "Runner should have written to the orchestrator-termination pipe.");
            worker.MockLogger.AssertLogContains("reached terminal state 'Canceled'");

            orchTcs.SetResult();
            await oBuildTask;
        }

        /// <summary>
        /// Inverted-design property: when BuildXL ignores the signal (e.g. its watcher is wedged,
        /// disabled, or never opened the pipe), the runner must not interfere — it neither kills
        /// BuildXL nor lets the rewrite-to-zero on orchestrator-termination depend on BuildXL having
        /// reacted. The mock here does NOT react to the pipe, simulating a BuildXL that is unable
        /// or unwilling to act on the signal. BuildXL's natural exit must drive the runner's wait, and
        /// the runner's rewrite to 0 must still apply (the orchestrator failure is already user-visible).
        /// </summary>
        [Fact]
        public async Task RunnerDoesNotKillBuildXLAndPreservesExitCodeWhenSignalIsIgnored()
        {
            var (orchestrator, worker) = CreateOrchestratorWorkerPairBuild();

            // BuildXL on the worker is gated by a TCS so the test controls when it "finishes".
            // Crucially, the mock does NOT react to the pipe — it simulates a BuildXL that
            // is unable or unwilling to act on the signal.
            var workerBxlTcs = new TaskCompletionSource();
            worker.MockLauncher.CompletionTask = workerBxlTcs.Task;
            worker.MockLauncher.ReturnCode = 42; // distinguish a natural exit from the previous default (0)
            worker.MockLauncher.ReactToOrchestratorTerminationPipe = false;

            var orchTcs = new TaskCompletionSource();
            orchestrator.MockLauncher.CompletionTask = orchTcs.Task;

            worker.MonitorPollInterval = TimeSpan.FromMilliseconds(50);

            // We assert on the *raw* BuildXL exit code; the WorkerAlwaysSucceeds rewrite would mask it.
            worker.Config.WorkerAlwaysSucceeds = false;

            orchestrator.Initialize();
            worker.Initialize();

            var buildArgs = new[] { "/foo", "/bar" };
            var oManager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);
            var wManager = new BuildManager(worker.RunnerService, worker.BuildExecutor, buildArgs, worker.MockLogger);

            var oBuildTask = oManager.BuildAsync();
            var wBuildTask = wManager.BuildAsync();

            await TestHelpers.WaitUntilAsync(() => worker.MockLauncher.Launched);

            // Transition the orchestrator to Failed. The runner will write to the pipe, but the
            // mock BuildXL ignores it (simulating a wedged or absent watcher).
            MockApiService.SetOrchestratorJobState(
                orchestrator.AdoEnvironment.BuildId,
                Guid.Parse(orchestrator.AdoEnvironment.JobId),
                TimelineRecordState.Completed,
                TaskResult.Failed);

            // Give the runner a few cycles to (correctly) NOT kill BuildXL.
            await Task.Delay(300);
            Assert.False(worker.MockLauncher.Exited, "BuildXL should still be running because it ignored the signal.");

            // BuildXL finishes naturally with its own exit code. The runner preserves it — bxl is the
            // authority on this worker's outcome. (WorkerAlwaysSucceeds is disabled in this test so the
            // raw exit code surfaces.)
            workerBxlTcs.SetResult();
            var workerReturn = await wBuildTask;
            Assert.Equal(42, workerReturn);

            // Orchestrator clean-up.
            orchTcs.SetResult();
            await oBuildTask;
        }

        /// <summary>
        /// Orchestrator-status monitor scenario: BuildXL on the worker exits on its own (e.g. fast
        /// build, or an internal BuildXL error) before the orchestrator dies. The monitor must shut
        /// down cleanly and the worker must return BuildXL's exit code unchanged.
        /// </summary>
        [Fact]
        public async Task OrchestratorMonitorPreservesBxlExitCodeWhenBxlFinishesFirst()
        {
            var (orchestrator, worker) = CreateOrchestratorWorkerPairBuild();

            // Orchestrator stays running so the worker's WaitForBuildInfo can complete normally.
            var orchTcs = new TaskCompletionSource();
            orchestrator.MockLauncher.CompletionTask = orchTcs.Task;

            // Worker's BuildXL just exits with a specific code immediately.
            worker.MockLauncher.CompletionTask = Task.CompletedTask;
            worker.MockLauncher.ReturnCode = 7; // arbitrary non-zero (after the launcher's exit==7→0 rewrite this would be 0,
                                                 // but the rewrite happens inside the concrete BuildXLLauncher, not in MockLauncher)
            worker.Config.WorkerAlwaysSucceeds = false;
            worker.MonitorPollInterval = TimeSpan.FromMilliseconds(50);

            orchestrator.Initialize();
            worker.Initialize();

            var buildArgs = new[] { "/foo", "/bar" };
            var oManager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);
            var wManager = new BuildManager(worker.RunnerService, worker.BuildExecutor, buildArgs, worker.MockLogger);

            var oBuildTask = oManager.BuildAsync();
            var workerReturn = await wManager.BuildAsync();

            Assert.Equal(7, workerReturn);
            worker.MockLogger.AssertLogNotContains("terminated with state");

            orchTcs.SetResult();
            await oBuildTask;
        }
    }

    /// <summary>
    /// Small test utility helpers.
    /// </summary>
    internal static class TestHelpers
    {
        /// <summary>
        /// Polls <paramref name="condition"/> every <paramref name="pollMs"/> ms until it returns true
        /// or <paramref name="timeoutMs"/> ms elapse. Throws on timeout so failing tests have a clear
        /// signal instead of hanging.
        /// </summary>
        public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5_000, int pollMs = 25)
        {
            var deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(pollMs);
            }
            throw new TimeoutException($"Condition did not become true within {timeoutMs}ms.");
        }
    }
}
