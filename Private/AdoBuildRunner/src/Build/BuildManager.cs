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
        // CODESYNC: Public/Src/Utilities/Configuration/ExitCode.cs
        private const int BuildXLUserErrorExitCode = 1;

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

            if (m_adoBuildRunnerService.Config.AgentRole == AgentRole.Worker
                && returnCode == BuildXLUserErrorExitCode)
            {
                // Note that this behavior will prevent the worker job from participating in a user initiated retry,
                // since the job will appear as a success from ADO's perspective. But that's mostly already the case since
                // bxl does not currently coordinate worker exit codes to make all workers fail if the orchestrator fails.
                // Masking the worker error here makes for a nicer user experience as we want to rely on the orchestrator's
                // error reporting to avoid presenting redundant errors.
                m_logger.Info($"The BuildXL process completed with user errors on this worker (exit code {returnCode}). Returning success because the orchestrator reports the build failure.");
                return 0;
            }

            LogExitCode(returnCode);
            
            return returnCode; 
        }

        /// <summary>
        /// Log the exit code as an error on the ADO console if it's non-zero, as informational otherwise
        /// </summary>
        private void LogExitCode(int returnCode)
        {
            Action<string> logAction = returnCode != 0 ? m_logger.TaskCompleteAsFailed : m_logger.Info;
            logAction.Invoke($"The BuildXL process completed with exit code {returnCode}");
        }
    }
}
