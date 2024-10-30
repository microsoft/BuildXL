// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdoBuildRunner;
using BuildXL.AdoBuildRunner.Vsts;

#nullable enable

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// A build executor that can execute a build engine depending on agent status and build arguments
    /// </summary>
    public abstract class BuildExecutor : IBuildExecutor
    {
        /// <nodoc />
        protected readonly AdoBuildRunnerService AdoBuildRunnerService;
        /// <nodoc />
        protected readonly ILogger Logger;
        private readonly IBuildXLLauncher m_bxlLauncher;

        /// <nodoc />
        public BuildExecutor(IBuildXLLauncher buildXLLauncher, AdoBuildRunnerService adoBuildRunnerService, ILogger logger)
        {
            AdoBuildRunnerService = adoBuildRunnerService;
            Logger = logger;
            m_bxlLauncher = buildXLLauncher;
        }

        /// <nodoc />
        protected Task<int> ExecuteBuild(IEnumerable<string> fullArguments, string buildSourceDirectory)
        {
            var arguments = ExtractAndEscapeCommandLineArguments(fullArguments);
			Logger.Info($"Launching BuildXL with Arguments: {arguments}");
            return m_bxlLauncher.LaunchAsync(arguments, Logger.Info, Logger.Warning);
        }

        /// <nodoc />
        private static string ExtractAndEscapeCommandLineArguments(IEnumerable<string> args) => string.Join(' ', args);

        /// <inheritdoc />
        public void PrepareBuildEnvironment()
        {
        }

        /// <inheritdoc />
        public Task<int> ExecuteSingleMachineBuild(string[] buildArguments)
        {
            Logger.Info($@"Launching single machine build");
            return ExecuteBuild(buildArguments, AdoBuildRunnerService.BuildContext.SourcesDirectory);
        }

        /// <inheritdoc />
        public abstract Task<int> ExecuteDistributedBuild(string[] buildArguments);

        /// <inheritdoc />
        public abstract string[] ConstructArguments(BuildInfo buildInfo, string[] buildArguments);

        /// <summary>
        /// Set arguments common to the worker and the orchestrator.
        /// </summary>
        protected string[] SetDefaultArguments()
        {
            var buildContext = AdoBuildRunnerService.BuildContext;
            // The default values are added to the start of command line string.
            // This way, user-provided arguments will be able to override the defaults.
            // If there is a need for a new default argument that's not specific to ADO-runner,
            // it should be added to ConfigurationProvider.GetAdoConfig().
            var defaultArguments = new List<string>() {
                $"/machineHostName:{buildContext.AgentHostName}",
                // By default, set the timeout to 20min in the workers to avoid unnecessary waiting upon connection failures
                "/p:BuildXLWorkerAttachTimeoutMin=20"
            };

            // Enable gRPC encryption
            if (!AdoBuildRunnerService.Config.DisableEncryption)
            {
                defaultArguments.Add("/p:GrpcCertificateSubjectName=CN=1es-hostedpools.default.microsoft.com");
            }

            // By default, enable cache miss analysis and pass the invocation key as a prefix
            var invocationKey = AdoBuildRunnerService.Config.InvocationKey;
            var cacheMissOption = string.IsNullOrEmpty(invocationKey) ? "/cacheMiss+" : $"/cacheMiss:{invocationKey}";

            defaultArguments.Add(cacheMissOption);

            if (AdoBuildRunnerService.AdoEnvironment.JobAttemptNumber > 1)
            {
                // Retries on ADO are single-machine unless users retry the worker stage
                // Let's prevent terminations and warnings in this case until we support 
                // a better strategy (feature #2227233)
                Logger.Warning("This build is part of a job retry. BuildXL will run without minimum worker checks or warnings.");
                defaultArguments.Add("/p:BuildXLLimitProblematicWorkerCount=0");
                defaultArguments.Add("/minWorkersWarn:0");
            }

            return defaultArguments.ToArray();
        }
    }
}
