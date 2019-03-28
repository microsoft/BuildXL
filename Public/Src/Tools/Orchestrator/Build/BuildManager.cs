// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using BuildXL.Orchestrator.Vsts;

namespace BuildXL.Orchestrator.Build
{
    /// <summary>
    /// A class managing execution of orchestrated builds depending on VSTS agent states
    /// </summary>
    public class BuildManager
    {
        private readonly IApi m_vstsApi;

        private readonly IBuildExecutor m_executor;

        private readonly string[] m_buildArguments;

        private readonly ILogger m_logger;

        /// <summary>
        /// Initializes the build manager with a concrete VSTS API implementation and all parameters necessary
        /// to orchestrate a distributed build
        /// </summary>
        /// <param name="vstsApi">Interface to interact with VSTS API</param>
        /// <param name="executor">Interface to execute the build engine</param>
        /// <param name="args">Build CLI arguments</param>
        /// <param name="logger">Interface to log build info</param>
        public BuildManager(IApi vstsApi, IBuildExecutor executor, string[] args, ILogger logger)
        {
            m_vstsApi = vstsApi ?? throw new ArgumentNullException(nameof(vstsApi));
            m_executor = executor ?? throw new ArgumentNullException(nameof(executor));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_buildArguments = args;
        }

        /// <summary>
        /// Executes a build depending on master / worker context
        /// </summary>
        /// <returns>The exit code returned by the worker process</returns>
        public async Task<int> BuildAsync()
        {
            string sessionId = (await m_vstsApi.GetBuildStartTimeAsync()).ToString("MMdd_HHmmss");

            var buildContext = new BuildContext()
            {
                SessionId        = sessionId,
                BuildId          = m_vstsApi.BuildId,
                SourcesDirectory = m_vstsApi.SourcesDirectory,
                RepositoryUrl    = m_vstsApi.RepositoryUrl,
                ServerUrl        = m_vstsApi.ServerUri,
                TeamProjectId    = m_vstsApi.TeamProjectId,
            };

            // Possibly extend context with additional info that can influence the build environment as needed
            m_executor.PrepareBuildEnvironment(buildContext);

            m_logger.Info($@"Value of the job position in the phase: {m_vstsApi.JobPositionInPhase}");
            m_logger.Info($@"Value of the total jobs in the phase: {m_vstsApi.TotalJobsInPhase}");

            var returnCode = 1;

            // Only one agent participating in the build, hence a singe machine build
            if (m_vstsApi.TotalJobsInPhase == 1)
            {
                returnCode = m_executor.ExecuteSingleMachineBuild(buildContext, m_buildArguments);
                LogExitCode(returnCode);
            }
            // Currently the agent spawned last in a multi-agent build is the elected master
            else if (m_vstsApi.TotalJobsInPhase == m_vstsApi.JobPositionInPhase)
            {
                await m_vstsApi.SetMachineReadyToBuild(GetAgentHostName(), GetAgentIPAddress(), isMaster: true);
                await m_vstsApi.WaitForOtherWorkersToBeReady();

                var machines = (await m_vstsApi.GetWorkerAddressInformationAsync()).ToList();
                foreach (var entry in machines)
                {
                    m_logger.Info($@"Found worker: {entry[Constants.MachineHostName]}@{entry[Constants.MachineIpV4Address]}");
                }

                returnCode = m_executor.ExecuteDistributedBuildAsMaster(buildContext, m_buildArguments, machines);
                LogExitCode(returnCode);
            }
            // Any agent spawned < total number of agents is a dedicated worker
            else
            {
                await m_vstsApi.WaitForMasterToBeReady();
                await m_vstsApi.SetMachineReadyToBuild(GetAgentHostName(), GetAgentIPAddress());

                var masterInfo = (await m_vstsApi.GetMasterAddressInformationAsync()).FirstOrDefault();
                if (masterInfo == null)
                {
                    throw new ApplicationException($"Couldn't get master address info, aborting!");
                }

                m_logger.Info($@"Found master: {masterInfo[Constants.MachineHostName]}@{masterInfo[Constants.MachineIpV4Address]}");

                returnCode = m_executor.ExecuteDistributedBuildAsWorker(buildContext, m_buildArguments, masterInfo);
                LogExitCode(returnCode);
            }

            return returnCode;
        }

        private string GetAgentHostName()
        {
            return System.Net.Dns.GetHostName();
        }

        private string GetAgentIPAddress()
        {
            var firstUpInterface = NetworkInterface.GetAllNetworkInterfaces()
                .OrderByDescending(c => c.Speed)
                .FirstOrDefault(c => c.NetworkInterfaceType != NetworkInterfaceType.Loopback && c.OperationalStatus == OperationalStatus.Up);

            if (firstUpInterface != null)
            {
                var props = firstUpInterface.GetIPProperties();
                // get first IPV4 address assigned to this interface
                var ipV4Address = props.UnicastAddresses
                    .Where(c => c.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(c => c.Address)
                    .FirstOrDefault();

                if (ipV4Address != null)
                {
                    return ipV4Address.ToString();
                }
            }

            throw new ApplicationException($"Unable to determine IP address, aborting!");
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
