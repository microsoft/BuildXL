// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.AdoBuildRunner.Vsts;
using System;
using System.Threading.Tasks;


namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// A class managing execution of orchestrated builds depending on VSTS agent states
    /// </summary>
    public class BuildManager
    {
        private readonly AdoBuildRunnerService m_adoBuildRunnerService;

        private readonly IBuildExecutor m_executor;

        private readonly string[] m_buildArguments;
        private readonly ILogger m_logger;

        /// <summary>
        /// Initializes the build manager with a concrete VSTS API implementation and all parameters necessary
        /// to orchestrate a distributed build
        /// </summary>
        /// <param name="adoBuildRunnerService">Interface to interact with VSTS API</param>
        /// <param name="executor">Interface to execute the build engine</param>
        /// <param name="args">Build CLI arguments</param>
        /// <param name="logger">Interface to log build info</param>
        public BuildManager(AdoBuildRunnerService adoBuildRunnerService, IBuildExecutor executor, string[] args, ILogger logger)
        {
            m_adoBuildRunnerService = adoBuildRunnerService;
            m_executor = executor;
            m_logger = logger;
            m_buildArguments = args;
        }

        /// <summary>
        /// Executes a build depending on orchestrator / worker context
        /// </summary>
        /// <returns>The exit code returned by the worker process</returns>
        public async Task<int> BuildAsync()
        {
            // Possibly extend context with additional info that can influence the build environment as needed
            m_executor.PrepareBuildEnvironment();
            var returnCode = await m_executor.ExecuteDistributedBuild(m_buildArguments);

            LogExitCode(returnCode);

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
