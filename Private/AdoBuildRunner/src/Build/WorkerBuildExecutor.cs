// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Launch the build as a Worker.
    /// </summary>
    public class WorkerBuildExecutor : BuildExecutor
    {
        private readonly TimeSpan? m_monitorPollInterval;

        /// <nodoc />
        public WorkerBuildExecutor(IBuildXLLauncher buildXLLauncher, AdoBuildRunnerService adoBuildRunnerService, ILogger logger)
            : this(buildXLLauncher, adoBuildRunnerService, logger, monitorPollInterval: null)
        {
        }

        /// <summary>
        /// Test hook: allows callers to tighten the monitor poll cadence. Production paths use the
        /// default <see cref="Constants.OrchestratorStatusPollSeconds"/>.
        /// </summary>
        internal WorkerBuildExecutor(IBuildXLLauncher buildXLLauncher, AdoBuildRunnerService adoBuildRunnerService, ILogger logger, TimeSpan? monitorPollInterval)
            : base(buildXLLauncher, adoBuildRunnerService, logger)
        {
            m_monitorPollInterval = monitorPollInterval;
        }

        /// <summary>
        /// Perform any work before setting the machine "ready" to build and executes the build.
        /// </summary>        
        public override async Task<int> ExecuteDistributedBuild(string[] buildArguments)
        {
            void skipLogUpload()
            {
                // We consume this variable in the 1ESPT Workflow to avoid running the log upload task with a non-existing directory
                AdoBuildRunnerService.SetVariable("BuildXLWorkflowSkipLogUpload", "true");
            }

            // Before we start, check if the build is already done.
            // This might happen if the worker is very late to a very fast build
            int returnCode;
            var maybeOrchestratorReturnCode = await AdoBuildRunnerService.GetBuildProperty(Constants.AdoBuildRunnerOrchestratorExitCode);
            if (maybeOrchestratorReturnCode != null && int.TryParse(maybeOrchestratorReturnCode, out var orchExitCode))
            {
                Logger.Warning($@"The orchestrator exited with exit code {orchExitCode} before the runner could launch this distributed build as a worker. This means that this agent was late to the build, possibly due to (comparatively) long pre-build task durations.");
                Logger.Warning($@"Skipping the worker invocation altogether and finishing with exit code 0");
                skipLogUpload();
                returnCode = 0;
            }
            else
            {
                // Get the build info from the orchestrator build
                BuildInfo buildInfo;
                try
                {
                    buildInfo = await AdoBuildRunnerService.WaitForBuildInfo();
                }
                catch (OrchestratorTerminatedException ex)
                {
                    // The orchestrator's failure is already user-visible; surface this worker's abort as a warning and exit cleanly
                    Logger.Warning(ex.Message);
                    skipLogUpload();
                    return 0;
                }

                if (!(await CheckOrchestratorPoolMatches(buildInfo)))
                {
                    // The pools don't match, we can't run the worker
                    // But we want to exit gracefully (with some warnings that have already logged)
                    Logger.Info($"Skipping the build: the running pool doesn't match the orchestrator pool.");
                    skipLogUpload();
                    return 0;
                }

                Logger.Info($@"Launching distributed build as worker");
                returnCode = await LaunchWithOrchestratorMonitorAsync(buildInfo, buildArguments);
            }

            if (AdoBuildRunnerService.Config.WorkerAlwaysSucceeds
                && returnCode != 0)
            {
                // If the orchestrator succeeds, then we don't want to make the pipeline fail
                // just because of this worker's failure. Log the failure but make the task succeed
                Logger.Error($"The build finished with errors in this worker (exit code: {returnCode}).");
                Logger.Warning("Marking this task as successful so the build pipeline won't fail");
                returnCode = 0;
            }

            return returnCode;
        }

        /// <summary>
        /// Launches BuildXL alongside a monitor that polls the orchestrator's ADO job state. If the
        /// orchestrator reaches a terminal state (Failed/Canceled) the runner signals BuildXL via an
        /// inheritable anonymous pipe (handle passed in <see cref="Constants.OrchestratorTerminationPipeEnvVar"/>);
        /// BuildXL then exits cleanly on its own.
        ///
        /// The signal is sticky: if the orchestrator already terminated by the time BuildXL opens the
        /// read end, the wait returns immediately. Only an explicit byte triggers the worker exit -- if
        /// the runner exits without writing, BuildXL sees EOF but deliberately takes no action, because
        /// EOF does not reveal the orchestrator outcome (which may be success).
        ///
        /// BuildXL reacts the same way regardless of whether it has attached: pre-attach the engine
        /// returns <c>SuccessNotRun</c>; post-attach BuildXL's <c>Exit</c> path is idempotent.
        ///
        /// CODESYNC: Public/Src/Engine/Dll/Distribution/WorkerService.cs (StartOrchestratorTerminationWatcher)
        /// </summary>
        private async Task<int> LaunchWithOrchestratorMonitorAsync(BuildInfo buildInfo, string[] buildArguments)
        {
            var orchestratorBuildId = AdoBuildRunnerService.OrchestratorBuildId;

            // WaitForBuildInfo has run, so the orchestrator build id is set and the orchestrator has published a parseable JobId.
            Contract.Assert(orchestratorBuildId != null, "OrchestratorBuildId should have been set by WaitForBuildInfo.");
            Contract.Assert(!string.IsNullOrEmpty(buildInfo.OrchestratorJobId), "BuildInfo should include an OrchestratorJobId.");
            var orchestratorJobId = Guid.Parse(buildInfo.OrchestratorJobId);

            using var orchMonitorCts = new CancellationTokenSource();

            // Create the pipe BEFORE launching BuildXL so the signal is sticky (no setup-order race).
            // Anonymous, inheritable, parent-writes / child-reads. Cross-platform, no naming/cleanup story.
            using var orchTerminationPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            var pipeHandleString = orchTerminationPipe.GetClientHandleAsString();
            var extraEnv = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Constants.OrchestratorTerminationPipeEnvVar] = pipeHandleString,
            };

            var orchMonitor = new OrchestratorStatusMonitor(AdoBuildRunnerService, orchestratorBuildId.Value, orchestratorJobId, Logger, m_monitorPollInterval);
            var orchMonitorTask = orchMonitor.RunAsync(orchMonitorCts.Token);
            var bxlTask = ExecuteBuild(ConstructArguments(buildInfo, buildArguments), extraEnv);

            // We intentionally skip DisposeLocalCopyOfClientHandle: the runner is the writer
            // (PipeDirection.Out), so BuildXL's EOF view depends only on the server stream, not on our
            // local copy. Calling it also closes the underlying handle, which breaks the mock launcher
            // that wraps the same handle in-process.

            var winner = await Task.WhenAny(bxlTask, orchMonitor.OrchestratorTerminated);

            if (winner == orchMonitor.OrchestratorTerminated)
            {
                Logger.Warning($"Orchestrator build (id={orchestratorBuildId}) terminated. Signaling BuildXL to exit cleanly via orchestrator-termination pipe.");
                try
                {
                    // Any byte will do -- BuildXL's watcher exits the worker on a successful read.
                    orchTerminationPipe.WriteByte(0x01);
                    await orchTerminationPipe.FlushAsync();
                }
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to write to orchestrator-termination pipe (BuildXL may still exit via in-band gRPC signals): {ex}");
                }
#pragma warning restore ERP022
            }

            // Always wait for BuildXL to exit and return its exit code -- BuildXL has the more complete
            // picture (gRPC state, MaterializeOutputs in progress, graceful teardown) so we honor its
            // decision rather than declare success from the runner side. The monitor is stopped either way.
            await orchMonitorCts.CancelAsync();
            await ObserveAsync(orchMonitorTask);
            return await bxlTask;
        }

        private static async Task ObserveAsync(Task task)
        {
            try
            {
                await task;
            }
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            catch
            {
                // Best-effort observation; the monitor's own logic logs any meaningful failure.
            }
#pragma warning restore ERP022
        }

        private async Task<bool> CheckOrchestratorPoolMatches(BuildInfo buildInfo)
        {
            // Pools should match, or we fail this task
            try
            {
                var workerPoolName = await AdoBuildRunnerService.GetRunningPoolNameAsync();
                // The pool name can be empty if we failed to resolve it (in either agent) - we do this best effort
                if (!string.IsNullOrEmpty(workerPoolName) && !string.IsNullOrEmpty(buildInfo.OrchestratorPool))
                {
                    if (workerPoolName != buildInfo.OrchestratorPool)
                    {
                        Logger?.Warning($"This agent is running on pool '{workerPoolName}', which is different than the pool the orchestrator is running on '{buildInfo.OrchestratorPool}'");
                        Logger?.Warning($"This mismatch can occur when a backup pool is configured for the pool specified for this pipeline and the pool is in failover mode.");
                        return false;
                    }
                }
            }
            catch (Exception)
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            {
                // Swallow any other errors, we don't want to fail the build if this check fails
                // Any error messages should have been logged
            }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler

            return true;
        }

        /// <summary>
        /// Constructs build arguments for the worker
        /// </summary>
        public override string[] ConstructArguments(BuildInfo buildInfo, string[] buildArguments)
        {
            return SetDefaultArguments().
                Concat(buildArguments)
                .Concat(
                [
                    $"/distributedBuildRole:worker",
                    $"/distributedBuildServicePort:{Constants.MachineGrpcPort}",
                    $"/distributedBuildOrchestratorLocation:{buildInfo.OrchestratorLocation}:{Constants.MachineGrpcPort}",
                    $"/relatedActivityId:{buildInfo.RelatedSessionId}"
                ])
                .ToArray();
        }
    }
}
