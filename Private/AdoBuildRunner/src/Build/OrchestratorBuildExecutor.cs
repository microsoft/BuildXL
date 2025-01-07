// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Launch the build as an orchestrator
    /// </summary>
    public class OrchestratorBuildExecutor : BuildExecutor
    {
        /// <nodoc />
        public OrchestratorBuildExecutor(IBuildXLLauncher buildXLLauncher, AdoBuildRunnerService adoBuildRunnerService, ILogger logger) : base(buildXLLauncher, adoBuildRunnerService, logger)
        {
        }

        /// <summary>
        /// Launch a build with a given context and arguments as orchestrator
        /// </summary>
        public override async Task<int> ExecuteDistributedBuild(string[] buildArguments)
        {
            // The orchestrator creates the build info and publishes it to the build properties
            var buildContext = AdoBuildRunnerService.BuildContext;
            var buildInfo = new BuildInfo { RelatedSessionId = Guid.NewGuid().ToString("D"), OrchestratorLocation = buildContext.AgentHostName, OrchestratorPool = buildContext.AgentPool };
            await AdoBuildRunnerService.PublishBuildInfo(buildInfo);
            Logger.Info($@"Launching distributed build as orchestrator");
            var returnCode = await ExecuteBuild(
                ConstructArguments(buildInfo, buildArguments)
            );

            try
            {
                await AdoBuildRunnerService.PublishBuildProperty(Constants.AdoBuildRunnerOrchestratorExitCode, returnCode.ToString());
            }
            catch (Exception ex)
            {
                // No need to fail here. Worst case scenario this means that the worker will be idle until it times out.
                Logger.Info($"Non-fatal failure while publishing orchestrator exit code after the build is done: {ex}.");
            }

            return returnCode;
        }

        /// <summary>
        /// Constructs build arguments for the orchestrator
        /// </summary>
        public override string[] ConstructArguments(BuildInfo buildInfo, string[] buildArguments)
        {
            return SetDefaultArguments().
                Concat(buildArguments)
                .Concat(
                [
                    $"/distributedBuildRole:orchestrator",
                    $"/distributedBuildServicePort:{Constants.MachineGrpcPort}",
                    $"/relatedActivityId:{buildInfo.RelatedSessionId}"
                ]).ToArray();
        }
    }
}
