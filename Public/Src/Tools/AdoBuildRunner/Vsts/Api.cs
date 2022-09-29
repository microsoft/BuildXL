// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdoBuildRunner.Vsts;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using TimelineRecord = Microsoft.TeamFoundation.DistributedTask.WebApi.TimelineRecord;

namespace BuildXL.AdoBuildRunner.Vsts
{
    /// <summary>
    /// Concrete implementation of the VSTS API interface for build coordination purposes
    /// </summary>
    public class Api : IApi
    {
        private enum AgentType
        {
            Orchestrator,
            Worker
        }

        private readonly BuildHttpClient m_buildClient;

        private readonly TaskHttpClient m_taskClient;

        private readonly ILogger m_logger;

        private const string HubType = "build";

        // Timeouts
        private readonly int m_maxWaitingTimeSeconds;

        /// <nodoc />
        public string BuildId { get; }

        /// <nodoc />
        public string TeamProject { get; }

        /// <nodoc />
        public string ServerUri { get; }

        /// <nodoc />
        public string AccessToken { get; }

        /// <nodoc />
        public int JobPositionInPhase { get; }

        /// <nodoc />
        public string AgentName { get; }

        /// <nodoc />
        public string SourcesDirectory { get; }

        /// <nodoc />
        public string TeamProjectId { get; }

        /// <nodoc />
        public int TotalJobsInPhase { get; }

        /// <nodoc />
        public string TimelineId { get; }

        /// <nodoc />
        public string PlanId { get; }

        /// <nodoc />
        public string RepositoryUrl { get; }

        /// <nodoc />
        public Api(ILogger logger)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));

            BuildId = Environment.GetEnvironmentVariable(Constants.BuildIdVarName);
            TeamProject = Environment.GetEnvironmentVariable(Constants.TeamProjectVarName);
            ServerUri = Environment.GetEnvironmentVariable(Constants.ServerUriVarName);
            AccessToken = Environment.GetEnvironmentVariable(Constants.AccessTokenVarName);
            AgentName = Environment.GetEnvironmentVariable(Constants.AgentNameVarName);
            SourcesDirectory = Environment.GetEnvironmentVariable(Constants.SourcesDirectoryVarName);
            TeamProjectId = Environment.GetEnvironmentVariable(Constants.TeamProjectIdVarName);
            TimelineId = Environment.GetEnvironmentVariable(Constants.TimelineIdVarName);
            PlanId = Environment.GetEnvironmentVariable(Constants.PlanIdVarName);
            RepositoryUrl = Environment.GetEnvironmentVariable(Constants.RepositoryUrlVariableName);

            string jobPositionInPhase = Environment.GetEnvironmentVariable(Constants.JobsPositionInPhaseVarName);

            if (string.IsNullOrWhiteSpace(jobPositionInPhase))
            {
                m_logger.Info("The job position in the build phase could not be determined. Therefore it must be a single machine build");
                JobPositionInPhase = 1;
                TotalJobsInPhase = 1;
            }
            else
            {
                if (!int.TryParse(jobPositionInPhase, out int position))
                {
                    LogAndThrow($"The env var {Constants.JobsPositionInPhaseVarName} contains a value that cannot be parsed to int");
                }

                JobPositionInPhase = position;
                string totalJobsInPhase = Environment.GetEnvironmentVariable(Constants.TotalJobsInPhaseVarName);

                if (!int.TryParse(totalJobsInPhase, out int totalJobs))
                {
                    LogAndThrow($"The env var {Constants.TotalJobsInPhaseVarName} contains a value that cannot be parsed to int");
                }

                TotalJobsInPhase = totalJobs;
            }

            var server = new Uri(ServerUri);
            var cred = new VssBasicCredential(string.Empty, AccessToken);

            m_taskClient = new TaskHttpClient(server, cred);
            m_buildClient = new BuildHttpClient(server, cred);

            m_maxWaitingTimeSeconds = Constants.DefaultMaximumWaitForWorkerSeconds;
            var userMaxWaitingTime = Environment.GetEnvironmentVariable(Constants.MaximumWaitForWorkerSecondsVariableName);
            if (!string.IsNullOrEmpty(userMaxWaitingTime))
            {
                if (!int.TryParse(userMaxWaitingTime, out var maxWaitingTime))
                {
                    m_logger.Warning($"Couldn't parse value '{userMaxWaitingTime}' for {Constants.MaximumWaitForWorkerSecondsVariableName}." +
                        $"Using the default value of {Constants.DefaultMaximumWaitForWorkerSeconds}");
                }
                else 
                {
                    m_maxWaitingTimeSeconds = maxWaitingTime;
                }
            }
            
            m_maxWaitingTimeSeconds =  string.IsNullOrEmpty(userMaxWaitingTime) ?
                Constants.DefaultMaximumWaitForWorkerSeconds
                : int.Parse(userMaxWaitingTime);
        }

        private async Task<IEnumerable<IDictionary<string, string>>> GetAddressInformationAsync(AgentType type)
        {
            List<TimelineRecord> timelineRecords = await GetTimelineRecords();

            return timelineRecords
                .Select(r => r.Variables)
                .Where(v => v.ContainsKey(Constants.MachineHostName) &&
                            v.ContainsKey(Constants.MachineIpV4Address) &&
                            v.TryGetValue(Constants.MachineType, out var t) && ((AgentType)Enum.Parse(typeof(AgentType), t.Value)) == type)
                .Select(e => e.ToDictionary(kv => kv.Key, kv => kv.Value.Value));
        }

        /// <inherit />
        public Task<IEnumerable<IDictionary<string, string>>> GetWorkerAddressInformationAsync()
        {
            return GetAddressInformationAsync(AgentType.Worker);
        }

        /// <inherit />
        public Task<IEnumerable<IDictionary<string, string>>> GetOrchestratorAddressInformationAsync()
        {
            return GetAddressInformationAsync(AgentType.Orchestrator);
        }

        private async Task<List<TimelineRecord>> GetTimelineRecords()
        {
            var currentTask = Environment.GetEnvironmentVariable(Constants.TaskDisplayNameVariableName);

            m_logger.Debug($"Getting timeline records for task '{currentTask}'");

            var allRecords = await m_taskClient.GetRecordsAsync(new Guid(TeamProjectId), HubType, new Guid(PlanId), new Guid(TimelineId));
            var records = allRecords.Where(r => r.Name == currentTask).ToList();

            m_logger.Debug($"Found {records.Count} records");
            return records;
        }

        /// <inherit />
        public async Task<DateTime> GetBuildStartTimeAsync()
        {
            if (!int.TryParse(BuildId, out int buildId))
            {
                LogAndThrow($"{Constants.BuildIdVarName} is not set or cannot be parsed into an int value");
            }

            var build = await m_buildClient.GetBuildAsync(new Guid(TeamProjectId), buildId);
            return build.StartTime.Value;
        }

        /// <inherit />
        public async Task SetMachineReadyToBuild(string hostName, string ipV4Address, string ipv6Address, bool isOrchestrator)
        {
            // Inject the information into a timeline record for this worker
            var records = await GetTimelineRecords();
            TimelineRecord record = records.FirstOrDefault(t => t.WorkerName.Equals(AgentName, StringComparison.OrdinalIgnoreCase));
            if (record != null)
            {
                record.Variables[Constants.MachineType] = (isOrchestrator ? AgentType.Orchestrator : AgentType.Worker).ToString();
                record.Variables[Constants.MachineHostName] = hostName;
                record.Variables[Constants.MachineIpV4Address] = ipV4Address;
                record.Variables[Constants.MachineIpV6Address] = ipv6Address;
                record.Variables[Constants.BuildStatus] = Constants.BuildStatusNotFinished;

                await m_taskClient.UpdateTimelineRecordsAsync(
                    new Guid(TeamProjectId),
                    HubType,
                    new Guid(PlanId),
                    new Guid(TimelineId),
                    new List<TimelineRecord>() { record });

                m_logger.Info("Marked machine as ready to build in the timeline records");
            }
            else
            {
                LogAndThrow("No records found for this worker");
            }
        }

        /// <inherit />
        public async Task SetBuildResult(bool isSuccess)
        {
            // Inject the information into a timeline record for this worker
            var records = await GetTimelineRecords();

            // Retrieve the buildstatus record for this machine
            var record = records.First(r =>
                r.WorkerName.Equals(AgentName, StringComparison.OrdinalIgnoreCase) &&
                r.Variables.ContainsKey(Constants.BuildStatus));

            var resultStatus = isSuccess ? Constants.BuildStatusSuccess : Constants.BuildStatusFailure;
            record.Variables[Constants.BuildStatus] = resultStatus;
            await m_taskClient.UpdateTimelineRecordsAsync(
                new Guid(TeamProjectId),
                HubType,
                new Guid(PlanId),
                new Guid(TimelineId),
                new List<TimelineRecord>() { record });
            
            m_logger.Info($"Marked machine build status as {resultStatus}");
        }

        /// <inherit />
        public Task WaitForOtherWorkersToBeReady()
        {
            m_logger.Info("Waiting for workers to get ready...");
            return WaitForAgentsToBeReady(AgentType.Worker);
        }


        /// <inherit />
        public async Task<bool> WaitForOrchestratorExit()
        {
            m_logger.Info("Waiting for the orchestrator to exit the build");

            var elapsedTime = 0;
            while (elapsedTime < m_maxWaitingTimeSeconds)
            {
                // Get the orchestrator record that indicates its build result
                List<TimelineRecord> records = await GetTimelineRecords();
                var record = records.Where(r =>
                    r.Variables.ContainsKey(Constants.BuildStatus) &&
                    (((AgentType)Enum.Parse(typeof(AgentType), r.Variables[Constants.MachineType].Value)) == AgentType.Orchestrator)).First();

                if (record.Variables[Constants.BuildStatus].Value == Constants.BuildStatusSuccess)
                {
                    return true;
                }
                if (record.Variables[Constants.BuildStatus].Value == Constants.BuildStatusFailure)
                {
                    return false;
                }

                await Task.Delay(TimeSpan.FromSeconds(Constants.PollRetryPeriodInSeconds));
                elapsedTime += Constants.PollRetryPeriodInSeconds;
            }

            m_logger.Info("Timed out waiting for the orchestrator to exit");
            return false;
        }

        /// <inherit />
        public Task WaitForOrchestratorToBeReady()
        {
            m_logger.Info("Waiting for orchestrator to get ready...");
            return WaitForAgentsToBeReady(AgentType.Orchestrator);
        }

        private async Task WaitForAgentsToBeReady(AgentType type)
        {
            var otherAgentsAreReady = false;
            var elapsedTime = 0;

            while (!otherAgentsAreReady && elapsedTime < m_maxWaitingTimeSeconds)
            {
                List<TimelineRecord> records = await GetTimelineRecords();

                var errors = records.Where(r => r.ErrorCount.HasValue && r.ErrorCount.Value != 0);
                if (errors.Any())
                {
                    LogAndThrow("One of the other agents participating in this build failed during the pre-build coordination. The task on this agent will be aborted.");
                }

                var filteredMachines = records.Where(r =>
                    r.Variables.ContainsKey(Constants.MachineType) &&
                    r.Variables.ContainsKey(Constants.MachineHostName) &&
                    r.Variables.ContainsKey(Constants.MachineIpV4Address) &&
                    (((AgentType) Enum.Parse(typeof(AgentType), r.Variables[Constants.MachineType].Value)) == type)).ToList();

                switch (type)
                {
                    case AgentType.Orchestrator:
                        otherAgentsAreReady = (filteredMachines.Count == 1);
                        break;
                    case AgentType.Worker:
                        otherAgentsAreReady = (filteredMachines.Count == (TotalJobsInPhase - 1));
                        break;
                }

                if (!otherAgentsAreReady)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Constants.PollRetryPeriodInSeconds));
                    elapsedTime += Constants.PollRetryPeriodInSeconds;

                    m_logger.Info($"Other agents are not ready, retrying in {Constants.PollRetryPeriodInSeconds} Seconds(s)...");
                }
            }

            if (elapsedTime >= m_maxWaitingTimeSeconds)
            {
                LogAndThrow($"Waiting for all agents to get ready failed after {m_maxWaitingTimeSeconds} seconds. Aborting...");
            }
        }

        private void LogAndThrow(string error)
        {
            CoordinationException.LogAndThrow(m_logger, error);
        }
    }
}
