// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdoBuildRunner;
using BuildXL.AdoBuildRunner.Build;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
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

            var workerReturn = await wBuildTask.WithTimeoutAsync(TimeSpan.FromSeconds(5)); // Worker finishes
            orchTcs.SetResult();    // And now let orchestrator finish
            var orchReturn = await oBuildTask.WithTimeoutAsync(TimeSpan.FromSeconds(5));

            // Machines communicated with the API
            var properties = MockApiService.BuildProperties;
            XAssert.IsTrue(properties.ContainsKey(orchestrator.AdoEnvironment.BuildId));

            var thisBuildProps = properties[orchestrator.AdoEnvironment.BuildId];
            XAssert.IsTrue(thisBuildProps.ContainsKey(orchestrator.RunnerService.BuildContext.InvocationKey));

            // BuildXL was launched
            XAssert.IsTrue(orchestrator.MockLauncher.Launched);
            XAssert.IsTrue(worker.MockLauncher.Launched);

            // Consistent return codes 
            XAssert.AreEqual(orchestrator.MockLauncher.ReturnCode, orchReturn);
            XAssert.AreEqual(worker.MockLauncher.ReturnCode, workerReturn);

            foreach (var arg in buildArgs)
            {
                XAssert.Contains(orchestrator.MockLauncher.Arguments, arg);
                XAssert.Contains(worker.MockLauncher.Arguments, arg);
            };
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
            XAssert.IsTrue(orchestrator.MockLauncher.Launched);
            XAssert.IsTrue(orchestrator.MockLauncher.Exited);

            var properties = MockApiService.BuildProperties;
            XAssert.IsTrue(properties.ContainsKey(orchestrator.AdoEnvironment.BuildId));

            var thisBuildProps = properties[orchestrator.AdoEnvironment.BuildId];
            XAssert.IsTrue(thisBuildProps.ContainsKey(orchestrator.RunnerService.BuildContext.InvocationKey));

            // The worker now tries to join the build session. But because the orchestrator is done, it won't even run BuildXL
            await wManager.BuildAsync();
            XAssert.IsFalse(worker.MockLauncher.Launched);
        }

        [Fact]
        public async Task PoolMismatchIsLogged()
        {
            var (orchestrator, worker) = CreateOrchestratorWorkerPairBuild();

            var invocationKey = worker.Config.InvocationKey;

            var orchTcs = new TaskCompletionSource();
            orchestrator.MockLauncher.CompletionTask = orchTcs.Task;    // We'll delay the orchestrator so the worker has a chance to 'attach' while the build is running

            orchestrator.Initialize();
            worker.Initialize();

            var buildArgs = new[] { "/foo", "/bar" };
            var wManager = new BuildManager(worker.RunnerService, worker.BuildExecutor, buildArgs, worker.MockLogger);
            var oManager = new BuildManager(orchestrator.RunnerService, orchestrator.BuildExecutor, buildArgs, orchestrator.MockLogger);

            var poolId = 0;
            // Return a different pool for each agent
            MockApiService.GetPoolName = () => $"Pool_{Interlocked.Increment(ref poolId)}";

            var oBuildTask = oManager.BuildAsync();
            var wBuildTask = wManager.BuildAsync();

            var workerReturn = await wBuildTask.WithTimeoutAsync(TimeSpan.FromSeconds(5)); // Worker finishes
            orchTcs.SetResult();    // And now let orchestrator finish
            var orchReturn = await oBuildTask.WithTimeoutAsync(TimeSpan.FromSeconds(5));

            XAssert.IsTrue(worker.MockLauncher.Launched);
            worker.MockLogger.AssertLogContains("is different than the pool the orchestrator is running on");
        }

        private (AgentHarness Orchestrator, AgentHarness Worker) CreateOrchestratorWorkerPairBuild()
        {
            var orchestrator = CreateOrchestrator();
            var worker = CreateWorker();

            // Sanity check - these should be created with the same defaults
            XAssert.AreEqual(worker.Config.InvocationKey, orchestrator.Config.InvocationKey);
            XAssert.AreEqual(worker.AdoEnvironment.BuildId, orchestrator.AdoEnvironment.BuildId);

            // Modify some defaults for the worker so it is a 'distinct' agent
            worker.AdoEnvironment.AgentMachineName = "WorkerTestAgent";
            worker.AdoEnvironment.JobId = "WorkerTestAgentJobId";
            worker.AdoEnvironment.AgentName = "WorkerTestAgentName";

            // Build is 'running' - the API knows about it
            MockApiService.AddBuild(worker.AdoEnvironment.BuildId, CreateTestBuild(worker.AdoEnvironment));

            return (orchestrator, worker);
        }
    }
}
