// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// Execute the build as an orchestrator
    /// </summary>
    public class OrchestratorBuildExecutor : BuildExecutor
    {
        /// <nodoc />
        public OrchestratorBuildExecutor(ILogger logger, IAdoBuildRunnerService adoBuildRunnerService) : base(logger, adoBuildRunnerService)
        {
        }

        /// <summary>
        /// Execute a build with a given context and arguments as orchestrator
        /// </summary>
        public override async Task<int> ExecuteDistributedBuild(BuildContext buildContext, string[] buildArguments)
        {
            // The orchestrator creates the build info and publishes it to the build properties
            var buildInfo = new BuildInfo { RelatedSessionId = Guid.NewGuid().ToString("D"), OrchestratorLocation = buildContext.AgentHostName };
            await AdoBuildRunnerService.PublishBuildInfo(buildContext, buildInfo);
            Logger.Info($@"Launching distributed build as orchestrator");
            return ExecuteBuild(
                buildContext,
                ConstructArguments(buildContext, buildInfo, buildArguments),
                buildContext.SourcesDirectory
            );
        }

        /// <summary>
        /// Constructs build arguments for the orchestrator
        /// </summary>
        public override string[] ConstructArguments(BuildContext buildContext, BuildInfo buildInfo, string[] buildArguments)
        {
            return SetDefaultArguments(buildContext).
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
