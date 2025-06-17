// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Launch the build as a Worker.
    /// </summary>
    public class WorkerBuildExecutor : BuildExecutor
    {
        /// <nodoc />
        public WorkerBuildExecutor(IBuildXLLauncher buildXLLauncher, AdoBuildRunnerService adoBuildRunnerService, ILogger logger) : base(buildXLLauncher, adoBuildRunnerService, logger)
        {
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
                var buildInfo = await AdoBuildRunnerService.WaitForBuildInfo();
                if (!(await CheckOrchestratorPoolMatches(buildInfo)))
                {
                    // The pools don't match, we can't run the worker
                    // But we want to exit gracefully (with some warnings that have already logged)
                    Logger.Info($"Skipping the build: the running pool doesn't match the orchestrator pool.");
                    skipLogUpload();
                    return 0;
                }

                Logger.Info($@"Launching distributed build as worker");
                returnCode = await ExecuteBuild(
                    ConstructArguments(buildInfo, buildArguments)
                );
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
