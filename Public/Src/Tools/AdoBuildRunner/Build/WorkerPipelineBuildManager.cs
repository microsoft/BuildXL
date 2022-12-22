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
    public class WorkerPipelineBuildManager
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
        /// <param name="buildContext">Build context</param>
        /// <param name="executor">Interface to execute the build engine</param>
        /// <param name="args">Build CLI arguments</param>
        /// <param name="logger">Interface to log build info</param>
        public WorkerPipelineBuildManager(IApi vstsApi, IBuildExecutor executor, BuildContext buildContext, string[] args, ILogger logger)
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
        public Task<int> BuildAsync(bool isOrchestrator)
        {
            // Possibly extend context with additional info that can influence the build environment as needed
            m_executor.PrepareBuildEnvironment(m_buildContext);

            int returnCode;

            if (isOrchestrator)
            {
                returnCode = m_executor.ExecuteDistributedBuildAsOrchestrator(m_buildContext, m_buildArguments);
                // await m_vstsApi.SetBuildResult(success: returnCode == 0); -- TODO: Maybe workers want to query this
                LogExitCode(returnCode);
            }
            else
            {
                m_executor.InitializeAsWorker(m_buildContext, m_buildArguments);
                returnCode = m_executor.ExecuteDistributedBuildAsWorker(m_buildContext, m_buildArguments);
                LogExitCode(returnCode);
            }

            return Task.FromResult(returnCode);
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
