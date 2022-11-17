// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AdoBuildRunner.Vsts;
using BuildXL.AdoBuildRunner.Vsts;

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
        /// Executes a build depending on orchestrator / worker context
        /// </summary>
        /// <returns>The exit code returned by the worker process</returns>
        public async Task<int> BuildAsync()
        {
            var buildContext = new BuildContext()
            {
                SessionId        = await GetBuildSessionId(),
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

            int returnCode;

            // Only one agent participating in the build, hence a singe machine build
            if (m_vstsApi.TotalJobsInPhase == 1)
            {
                returnCode = m_executor.ExecuteSingleMachineBuild(buildContext, m_buildArguments);
                PublishRoleInEnvironment(isOrchestrator: true);
                LogExitCode(returnCode);
            }
            // The first agent spawned in a multi-agent build is the elected orchestrator
            else if (m_vstsApi.JobPositionInPhase == 1)
            {
                await m_vstsApi.SetMachineReadyToBuild(GetAgentHostName(), GetAgentIPAddress(false), GetAgentIPAddress(true), isOrchestrator: true);

                var numDynamicWorkers = m_vstsApi.TotalJobsInPhase - 1; // The number of worker that might show up for the build
                returnCode = m_executor.ExecuteDistributedBuildAsOrchestrator(buildContext, m_buildArguments, numDynamicWorkers);

                await m_vstsApi.SetBuildResult(success: returnCode == 0);
                PublishRoleInEnvironment(isOrchestrator: true);
                LogExitCode(returnCode);
            }
            // Any agent spawned < total number of agents is a dedicated worker
            else
            {
                m_executor.InitializeAsWorker(buildContext, m_buildArguments);

                await m_vstsApi.WaitForOrchestratorToBeReady();
                var orchestratorInfo = (await m_vstsApi.GetOrchestratorAddressInformationAsync()).FirstOrDefault();

                if (orchestratorInfo == null)
                {
                    CoordinationException.LogAndThrow(m_logger, "Couldn't get orchestrator address info, aborting!");
                }

                m_logger.Info($@"Found orchestrator: {orchestratorInfo[Constants.MachineHostName]}@{orchestratorInfo[Constants.MachineIpV4Address]}");

                await m_vstsApi.SetMachineReadyToBuild(GetAgentHostName(), GetAgentIPAddress(false), GetAgentIPAddress(true));

                returnCode = m_executor.ExecuteDistributedBuildAsWorker(buildContext, m_buildArguments, orchestratorInfo);
                LogExitCode(returnCode);
                PublishRoleInEnvironment(isOrchestrator: false);

                if (returnCode == 0)
                {
                    // If the worker finished successfully but the build fails, we still want to fail this task
                    // so the task can be retried as a distributed build with the same number of workers by
                    // running "retry failed tasks".
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

        // Some post-build steps in the job may be interested in which build role
        // was adopted by this agent. We expose this fact through an environment variable
        private void PublishRoleInEnvironment(bool isOrchestrator)
        {
            var role = isOrchestrator ? Constants.BuildRoleOrchestrator : Constants.BuildRoleWorker;
            m_logger.Info($"Setting environment variable {Constants.BuildRoleVariableName}={role}");
            Console.WriteLine($"##vso[task.setvariable variable={Constants.BuildRoleVariableName}]{role}");
        }

        /// <summary>
        /// Returns the build id (related session id) as a GUID that is stable across workers of this build but unique across builds
        /// </summary>
        private async Task<string> GetBuildSessionId()
        {
            // We use the task name which is required to be unique for parallel builds in the same pipeline
            // as we use it to get the build records. The attempt number is also relevant.
            string taskName = Environment.GetEnvironmentVariable(Constants.TaskDisplayNameVariableName);
            string attemptNumber = Environment.GetEnvironmentVariable(Constants.JobAttemptVariableName);
            string startTime = (await m_vstsApi.GetBuildStartTimeAsync()).ToString("MMdd_HHmmss");
            return GuidFromString($"{m_vstsApi.TeamProjectId}-{m_vstsApi.BuildId}-{startTime}-{taskName}-{attemptNumber}");
        }

        private string GetAgentHostName()
        {
            return System.Net.Dns.GetHostName();
        }

#pragma warning disable CA5350 // GuidFromString uses a weak cryptographic algorithm SHA1.
        private static string GuidFromString(string value)
        {
            using var hash = SHA1.Create();
            byte[] bytesToHash = Encoding.Unicode.GetBytes(value);
            hash.TransformFinalBlock(bytesToHash, 0, bytesToHash.Length);

            // Guid takes a 16-byte array
            byte[] low16 = new byte[16];
            Array.Copy(hash.Hash, low16, 16);
            return new Guid(low16).ToString("D");
        }
#pragma warning restore CA5350 // GuidFromString uses a weak cryptographic algorithm SHA1.

        /// <nodoc />
        public static string GetAgentIPAddress(bool ipv6)
        {
            var firstUpInterface = NetworkInterface.GetAllNetworkInterfaces()
                .OrderByDescending(c => c.Speed)
                .FirstOrDefault(c => c.NetworkInterfaceType != NetworkInterfaceType.Loopback && c.OperationalStatus == OperationalStatus.Up);

            if (firstUpInterface != null)
            {
                var props = firstUpInterface.GetIPProperties();

                if (!ipv6)
                {
                    // get first IPV4 address assigned to this interface
                    var ipV4Address = props.UnicastAddresses
                    .Where(c => c.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(c => c.Address.ToString())
                    .FirstOrDefault();

                    if (ipV4Address != null)
                    {
                        return ipV4Address;
                    }
                }
                else
                {
                    var ipV6Address = props.UnicastAddresses
                    .Where(c => c.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(c => c.Address.ToString())
                    .Select(a => a.Split('%').FirstOrDefault() ?? a)
                    .FirstOrDefault();

                    if (ipV6Address != null)
                    {
                        return ipV6Address;
                    }
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
