// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading.Tasks;
using AdoBuildRunner;
using BuildXL.AdoBuildRunner.Vsts;
using MachineRole = AdoBuildRunner.MachineRole;

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// Launch the build as a Worker.
    /// </summary>
    public class WorkerBuildExecutor : BuildExecutor
    {
        /// <nodoc />
        public WorkerBuildExecutor(IBuildXLLauncher buildXLLauncher, IAdoBuildRunnerService adoBuildRunnerService, ILogger logger) : base(buildXLLauncher, adoBuildRunnerService, logger)
        {
        }

        /// <summary>
        /// Perform any work before setting the machine "ready" to build and executes the build.
        /// </summary>        
        public override async Task<int> ExecuteDistributedBuild(string[] buildArguments)
        {
            // Before we start, check if the build is already done.
            // This might happen if the worker is very late to a very fast build
            int returnCode;
            var maybeOrchestratorReturnCode = await AdoBuildRunnerService.GetBuildProperty(Constants.AdoBuildRunnerOrchestratorExitCode);
            if (maybeOrchestratorReturnCode != null && int.TryParse(maybeOrchestratorReturnCode, out var orchExitCode))
            {
                Logger.Warning($@"The orchestrator exited with exit code {orchExitCode} before the runner could launch this distributed build as a worker. This means that this agent was late to the build, possibly due to (comparatively) long pre-build task durations.");
                Logger.Warning($@"Skipping the worker invocation altogether and finishing with exit code 0");
                returnCode = 0;
            }
            else
            {
                // Get the build info from the orchestrator build
                var buildInfo = await AdoBuildRunnerService.WaitForBuildInfo();
                Logger.Info($@"Launching distributed build as worker");
                returnCode = await ExecuteBuild(
                    ConstructArguments(buildInfo, buildArguments),
                    AdoBuildRunnerService.BuildContext.SourcesDirectory
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
