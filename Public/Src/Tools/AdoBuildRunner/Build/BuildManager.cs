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
                var buildInfo = new BuildInfo { RelatedSessionId = Guid.NewGuid().ToString("D"), OrchestratorLocation = m_buildContext.AgentMachineName  };
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

            if (!isOrchestrator && Environment.GetEnvironmentVariable(Constants.WaitForOrchestratorExitVariableName) == "true")
            {
                // If the worker finished successfully but the build fails, we may still want to fail this task
                // so the task can be retried as a distributed build with the same number of workers by
                // running "retry failed tasks". This behavior makes the agents wait unnecessarily so it's 
                // disabled by default.
                if (returnCode == 0)
                {
                    var orchestratorSucceeded = await m_vstsApi.WaitForOrchestratorExit();
                    if (!orchestratorSucceeded)
                    {
                        m_logger.Error($"The build finished with errors in the orchestrator. Failing this task with exit code {Constants.OrchestratorFailedWorkerReturnCode} so this worker will participate in retries.");
                        returnCode = Constants.OrchestratorFailedWorkerReturnCode;
                    }
                }
                else
                {
                    // If the orchestrator succeeds, then we don't want to make the pipeline fail
                    // just because of this worker's failure. Log the failure but make the task succeed
                    m_logger.Error($"The build finished with errors in this worker (exit code: {returnCode}).");
                    m_logger.Warning("Marking this task as successful so the build pipeline won't fail");
                    returnCode = 0;
                }
            }

            return returnCode;
        }

        private void LogExitCode(int returnCode)
        {
            if (returnCode != 0)
            {
                m_logger.Error(($"ExitCode: {returnCode}"));
            }
            else
            {
                m_logger.Info($"ExitCode: {returnCode}");
            }
        }
    }
}
