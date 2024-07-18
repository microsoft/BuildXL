// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;
using MachineRole = AdoBuildRunner.MachineRole;

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// Execute the build as a Worker.
    /// </summary>
    public class WorkerBuildExecutor : BuildExecutor
    {
        /// <nodoc />
        public WorkerBuildExecutor(ILogger logger, IAdoBuildRunnerService adoBuildRunnerService) : base(logger, adoBuildRunnerService)
        {
        }

        /// <summary>
        /// Perform any work before setting the machine "ready" to build and executes the build.
        /// </summary>        
        public override async Task<int> ExecuteDistributedBuild(BuildContext buildContext, string[] buildArguments)
        {
            // Get the build info from the orchestrator build
            var buildInfo = await AdoBuildRunnerService.WaitForBuildInfo(buildContext);
            Logger.Info($@"Launching distributed build as worker");
            var returnCode = ExecuteBuild(
                buildContext,
                ConstructArguments(buildContext, buildInfo, buildArguments),
                buildContext.SourcesDirectory
            );

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
        public override string[] ConstructArguments(BuildContext buildContext, BuildInfo buildInfo, string[] buildArguments)
        {
            return SetDefaultArguments(buildContext).
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
