// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;

using Environment = System.Environment;
using TimelineRecord = Microsoft.TeamFoundation.DistributedTask.WebApi.TimelineRecord;
using TimelineRecordState = Microsoft.TeamFoundation.DistributedTask.WebApi.TimelineRecordState;

namespace BuildXL.Orchestrator.Vsts
{
    /// <summary>
    /// Concrete implementation of the VSTS API interface for build orchestration purposes
    /// </summary>
    public class Api : IApi
    {
        private enum AgentType
        {
            Master,
            Worker
        }

        private readonly BuildHttpClient m_buildClient;

        private readonly TaskHttpClient m_taskClient;

        private readonly ILogger m_logger;

        private const string HubType = "build";

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
                    throw new ApplicationException($"The env var {Constants.JobsPositionInPhaseVarName} contains a value that cannot be parsed to int");
                }

                JobPositionInPhase = position;
                string totalJobsInPhase = Environment.GetEnvironmentVariable(Constants.TotalJobsInPhaseVarName);

                if (!int.TryParse(totalJobsInPhase, out int totalJobs))
                {
                    throw new ApplicationException($"The env var {Constants.TotalJobsInPhaseVarName} contains a value that cannot be parsed to int");
                }

                TotalJobsInPhase = totalJobs;
            }

            var server = new Uri(ServerUri);
            var cred = new VssBasicCredential(string.Empty, AccessToken);

            m_taskClient = new TaskHttpClient(server, cred);
            m_buildClient = new BuildHttpClient(server, cred);
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
        public Task<IEnumerable<IDictionary<string, string>>> GetMasterAddressInformationAsync()
        {
            return GetAddressInformationAsync(AgentType.Master);
        }

        private async Task<List<TimelineRecord>> GetTimelineRecords()
        {
            List<TimelineRecord> records = await m_taskClient.GetRecordsAsync(new Guid(TeamProjectId), HubType, new Guid(PlanId), new Guid(TimelineId));
            List<TimelineRecord> timelineRecords =
                records.Where(r => r.Name.Equals(Constants.BuildOrchestrationTaskName, StringComparison.OrdinalIgnoreCase)).ToList();

            return timelineRecords;
        }

        /// <inherit />
        public async Task<DateTime> GetBuildStartTimeAsync()
        {
            if (!int.TryParse(BuildId, out int buildId))
            {
                throw new ApplicationException($"{Constants.BuildIdVarName} is not set or cannot be parsed into an int value");
            }

            var build = await m_buildClient.GetBuildAsync(new Guid(TeamProjectId), buildId);
            return build.StartTime.Value;
        }

        /// <inherit />
        public async Task SetMachineReadyToBuild(string hostName, string ipV4Address, bool isMaster)
        {
            List<TimelineRecord> records = await GetTimelineRecords();
            TimelineRecord record = records.FirstOrDefault(t => t.WorkerName.Equals(AgentName, StringComparison.OrdinalIgnoreCase));

            if (record != null)
            {
                // Add / update agent info for the build orchestration
                record.Variables[Constants.MachineType] = (isMaster ? AgentType.Master : AgentType.Worker).ToString();
                record.Variables[Constants.MachineHostName] = hostName;
                record.Variables[Constants.MachineIpV4Address] = ipV4Address;

                await m_taskClient.UpdateTimelineRecordsAsync(
                    new Guid(TeamProjectId),
                    HubType,
                    new Guid(PlanId),
                    new Guid(TimelineId),
                    new List<TimelineRecord>() { record });
            }
        }

        /// <inherit />
        public Task WaitForOtherWorkersToBeReady()
        {
            m_logger.Info("Waiting for workers to get ready...");
            return WaitForAgentsToBeReady(AgentType.Worker);
        }

        /// <inherit />
        public Task WaitForMasterToBeReady()
        {
            m_logger.Info("Waiting for master to get ready...");
            return WaitForAgentsToBeReady(AgentType.Master);
        }

        private async Task WaitForAgentsToBeReady(AgentType type)
        {
            var otherAgentsAreReady = false;
            var elapsedTime = 0;

            while (!otherAgentsAreReady && elapsedTime < Constants.MaxWaitingPeriodBeforeFailingInSeconds)
            {
                List<TimelineRecord> records = await GetTimelineRecords();
                if (records.Any(r => r.ErrorCount.HasValue && r.ErrorCount.Value != 0))
                {
                    throw new ApplicationException("One of the agents failed during the orchestration task with errors, aborting build!");
                }

                var filteredMachines = records.Where(r =>
                    r.Variables.ContainsKey(Constants.MachineType) &&
                    r.Variables.ContainsKey(Constants.MachineHostName) &&
                    r.Variables.ContainsKey(Constants.MachineIpV4Address) &&
                    (((AgentType) Enum.Parse(typeof(AgentType), r.Variables[Constants.MachineType].Value)) == type)).ToList();

                switch (type)
                {
                    case AgentType.Master:
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

            if (elapsedTime >= Constants.MaxWaitingPeriodBeforeFailingInSeconds)
            {
                throw new ApplicationException("Waiting for all agents to get ready failed, aborting!");
            }
        }
    }
}
