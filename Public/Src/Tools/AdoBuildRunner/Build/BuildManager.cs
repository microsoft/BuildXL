// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;

#nullable enable

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// A class managing execution of orchestrated builds depending on VSTS agent states
    /// </summary>
    public class BuildManager
    {
        private readonly IApi m_vstsApi;

        private readonly IBuildExecutor m_executor;

        private readonly string[] m_buildArguments;
        private readonly BuildContext m_buildContext;
        private readonly ILogger m_logger;

        /// <summary>
        /// Initializes the build manager with a concrete VSTS API implementation and all parameters necessary
        /// to orchestrate a distributed build
        /// </summary>
        /// <param name="vstsApi">Interface to interact with VSTS API</param>
        /// <param name="executor">Interface to execute the build engine</param>
        /// <param name="args">Build CLI arguments</param>
        /// <param name="buildContext">Build context</param>
        /// <param name="logger">Interface to log build info</param>
        public BuildManager(IApi vstsApi, IBuildExecutor executor, BuildContext buildContext, string[] args, ILogger logger)
        {
            m_vstsApi = vstsApi;
            m_executor = executor;
            m_logger = logger;
            m_buildArguments = args;
            m_buildContext = buildContext;
        }

        /// <summary>
        /// Executes a build depending on orchestrator / worker context
        /// </summary>
        /// <returns>The exit code returned by the worker process</returns>
        public async Task<int> BuildAsync(bool isOrchestrator)
        {
            // Possibly extend context with additional info that can influence the build environment as needed
            m_executor.PrepareBuildEnvironment(m_buildContext);

            int returnCode;

            if (isOrchestrator)
            {
                // The orchestrator creates the build info and publishes it to the build properties
                var buildInfo = new BuildInfo { RelatedSessionId = Guid.NewGuid().ToString("D"), OrchestratorLocation = m_buildContext.AgentHostName  };
                await m_vstsApi.PublishBuildInfo(m_buildContext, buildInfo);
                returnCode = m_executor.ExecuteDistributedBuildAsOrchestrator(m_buildContext, buildInfo.RelatedSessionId, m_buildArguments);
            }
            else
            {
                // Get the build info from the orchestrator build
                var buildInfo = await m_vstsApi.WaitForBuildInfo(m_buildContext);
                returnCode = m_executor.ExecuteDistributedBuildAsWorker(m_buildContext, buildInfo, m_buildArguments);
            }

            LogExitCode(returnCode);

            if (!isOrchestrator 
                && Environment.GetEnvironmentVariable(Constants.WorkerAlwaysSucceeds) == "true" 
                && returnCode != 0)
            {
                // If the orchestrator succeeds, then we don't want to make the pipeline fail
                // just because of this worker's failure. Log the failure but make the task succeed
                m_logger.Error($"The build finished with errors in this worker (exit code: {returnCode}).");
                m_logger.Warning("Marking this task as successful so the build pipeline won't fail");
                returnCode = 0;
            }

            return returnCode;
        }

        /// <summary>
        /// Log the exit code as an error on the ADO console if it's non-zero, as informational otherwise
        /// </summary>
        private void LogExitCode(int returnCode)
        {
            Action<string> logAction = returnCode != 0 ? m_logger.Error : m_logger.Info;
            logAction.Invoke($"The BuildXL process completed with exit code {returnCode}");
        }
    }
}
