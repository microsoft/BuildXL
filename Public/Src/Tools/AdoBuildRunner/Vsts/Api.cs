// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using AdoBuildRunner.Vsts;
using BuildXL.AdoBuildRunner.Build;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using TimelineRecord = Microsoft.TeamFoundation.DistributedTask.WebApi.TimelineRecord;

#nullable enable

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
        public string JobId { get; }

        /// <nodoc />
        public string AgentName { get; }
        
        /// <nodoc />
        public string AgentMachineName { get; }

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
        public string CollectionUrl { get; }

        /// <summary>
        /// We use bare HTTP for some methods that do not have a library interface
        /// </summary>
        private readonly VstsHttpRelay m_http;

        /// <summary>
        /// Generate a build URL from a build id
        /// </summary>
        /// <param name="buildId"></param>
        /// <returns></returns>
        private string GetBuildLinkFromId(int buildId) => $"{CollectionUrl}/{TeamProject}/_build/results?buildid={buildId}";

        /// <nodoc />
        public Api(ILogger logger)
        {
            m_logger = logger;

            BuildId = Environment.GetEnvironmentVariable(Constants.BuildIdVarName)!;
            TeamProject = Environment.GetEnvironmentVariable(Constants.TeamProjectVarName)!;
            ServerUri = Environment.GetEnvironmentVariable(Constants.ServerUriVarName)!;
            AccessToken = Environment.GetEnvironmentVariable(Constants.AccessTokenVarName)!;
            AgentName = Environment.GetEnvironmentVariable(Constants.AgentNameVarName)!;
            AgentMachineName = Environment.GetEnvironmentVariable(Constants.AgentMachineNameVarName)!;
            SourcesDirectory = Environment.GetEnvironmentVariable(Constants.SourcesDirectoryVarName)!;
            TeamProjectId = Environment.GetEnvironmentVariable(Constants.TeamProjectIdVarName)!;
            TimelineId = Environment.GetEnvironmentVariable(Constants.TimelineIdVarName)!;
            JobId = Environment.GetEnvironmentVariable(Constants.JobIdVariableName)!;
            PlanId = Environment.GetEnvironmentVariable(Constants.PlanIdVarName)!;
            RepositoryUrl = Environment.GetEnvironmentVariable(Constants.RepositoryUrlVariableName)!;
            CollectionUrl = Environment.GetEnvironmentVariable(Constants.CollectionUrlVariableName)!;

            m_http = new VstsHttpRelay(AccessToken, logger);

            string jobPositionInPhase = Environment.GetEnvironmentVariable(Constants.JobsPositionInPhaseVarName)!;

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
                string totalJobsInPhase = Environment.GetEnvironmentVariable(Constants.TotalJobsInPhaseVarName)!;

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

        private Task<PropertiesCollection> GetBuildProperties(int buildId)
        {
            // Get properties for this build if a build id is not specified
            return m_buildClient.GetBuildPropertiesAsync(new Guid(TeamProjectId), buildId);
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
        private async Task<DateTime> GetBuildStartTimeAsync()
        {
            if (!int.TryParse(BuildId, out int buildId))
            {
                LogAndThrow($"{Constants.BuildIdVarName} is not set or cannot be parsed into an int value");
            }

            var build = await m_buildClient.GetBuildAsync(new Guid(TeamProjectId), buildId);
            return build.StartTime.GetValueOrDefault();
        }

        private Task AddBuildProperty(int buildId, string property, string value)
        {
            // UpdateBuildProperties is ultimately an HTTP PATCH: the new properties specified will be added to the existing ones
            // in an atomic fashion. So we don't have to worry about multiple builds concurrently calling UpdateBuildPropertiesAsync
            // as long as the keys don't clash.
            PropertiesCollection patch = new()
            {
                { property, value }
            };

            return m_buildClient.UpdateBuildPropertiesAsync(patch, new Guid(TeamProjectId), buildId);
        }

        /// <inherit />
        public async Task PublishBuildInfo(BuildContext buildContext, BuildInfo buildInfo)
        {
            // Check that the build identifier was not used before - this is to catch user specification errors 
            // where some pipeline uses the same identifier for two different BuildXL invocations.
            // Note that this check is racing against a concurrent update, but this doesn't matter because
            // we expect to catch it most of times, and that's all we need for the error to be surfaced and fixed.
            var buildId = int.Parse(BuildId);
            var properties = await GetBuildProperties(buildId);
            if (properties.ContainsKey(buildContext.InvocationKey))
            {
                LogAndThrow($"A build with identifier '{buildContext.InvocationKey}' is already running in this pipeline. " +
                    $"Identifiers (set through the environment variable '{Constants.AdoBuildRunnerInvocationKey}') should be unique" +
                    $" for invocations within the same ADO Build.");
            }

            await AddBuildProperty(buildId, buildContext.InvocationKey, buildInfo.Serialize());
        }

        /// <summary>
        /// With knowledge of the build id running the orchestrator for this build, we perform two verifications
        /// to try to surface any configuration errors. 
        /// 
        /// (1) A sanity check that the branch and commit for this run are consistent with the orchestrator run.
        ///     This is to ensure a minimum of consistency in the sources for all the workers: of course, there are
        ///     other ways in which the different pipelines might end up with different sources before launch time,
        ///     but we should verify this bare minimum 
        /// 
        /// (2) If multiple workers query the same build with the same invocation key, we want to make sure they are all
        ///     coming from the same job. This is to prevent user errors where different worker pipelines are using the
        ///     same invocation key and are triggered by the same orchestrator pipeline (this may happen, for example,
        ///     if there are two different builds happening in the same pipeline).
        /// </summary>
        /// <remarks>
        /// The second check is racy: it is technically possible that multiple concurrent agents query the build properties 
        /// at the same time, not notice the other one, and then 'last-one-wins' updating the properties.
        /// However, it is enough for catching this specification error that this is just unlikely (which it is) and not impossible.
        /// </remarks>
        private async Task VerifyWorkerCorrectness(BuildContext buildContext, int orchestratorBuildId)
        {
            var orchestratorBuild = await m_buildClient.GetBuildAsync(TeamProject, orchestratorBuildId);
            
            // (1) Source branch / version should match
            var localSourceBranch = Environment.GetEnvironmentVariable(Constants.SourceBranchVariableName);
            var localSourceVersion = Environment.GetEnvironmentVariable(Constants.SourceVersionVariableName);
            if (orchestratorBuild.SourceBranch != localSourceBranch || orchestratorBuild.SourceVersion != localSourceVersion)
            {
                LogAndThrow($"Version control mismatch between the orchestrator build. Ensure the worker pipeline is triggered with the same sources as the orchestrator pipeline." + Environment.NewLine
                            + $"This build: SourceBranch='{localSourceBranch}', SourceVersion='{localSourceVersion}'" + Environment.NewLine
                            + $"Orchestrator build (id={orchestratorBuildId}): SourceBranch='{orchestratorBuild.SourceBranch}', SourceVersion='{orchestratorBuild.SourceVersion}'");
            }

            // (2) One-to-one correspondence between a jobs in a worker pipeline, and an orchestrator
            if (JobPositionInPhase != 1)
            {
                // Only do this once per 'parallel group', i.e., only for the first agent in a parallel strategy context
                // (this means only one worker per distributed build). This is because there is no value that we can
                // reliably use that is unique to a job but shared amongst the parallel agents of the same 'job'.
                // But because parallel agents running the same job are exact replicas of each other (modulo 'JobPositionInPhase')
                // this is okay: the point of this verification is to surface any misconfiguration quickly by virtue of it
                // failing 'most of the time', and as long as the first agent fails with the appropriate message (even though
                // the other agent will continue), our intention is satisfied.
                return;
            }

            var properties = await GetBuildProperties(orchestratorBuildId);
            var workerInvocationSentinel = $"{buildContext.InvocationKey}__workerjobid";
            if (properties.ContainsKey(workerInvocationSentinel))
            {
                var value = properties.GetValue(workerInvocationSentinel, string.Empty);
                if (value != JobId)
                {
                    LogAndThrow($"All workers participating in the build '{buildContext.InvocationKey}' must originate from the same parallel job. This failure probably means that some pipeline specification is duplicating invocation keys");
                }
            }
            else
            {
                m_logger?.Info($"No property found with the key {workerInvocationSentinel}");
            }

            // "Claim" this invocation key for this job by publishing the sentinel in the properties,
            // so subsequent jobs that may be running in different will fail the above check
            await AddBuildProperty(orchestratorBuildId, workerInvocationSentinel, "1");
        }

        /// <inherit />
        public async Task SetMachineReadyToBuild(string hostName, string ipV4Address, string ipv6Address, bool isOrchestrator)
        {
            // Inject the information into a timeline record for this worker
            var records = await GetTimelineRecords();
            TimelineRecord? record = records.FirstOrDefault(t => t.WorkerName.Equals(AgentName, StringComparison.OrdinalIgnoreCase));
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

        /// <inherit />
        public async Task<BuildInfo> WaitForBuildInfo(BuildContext buildContext)
        {
            var triggerInfo = await m_http.GetBuildTriggerInfoAsync();
            if (triggerInfo == null)
            {
                LogAndThrow("TriggerInfo is required to query the BuildInfo");
            }

            int triggeringBuildId = 0;
            if (!triggerInfo.TryGetValue(Constants.TriggeringAdoBuildIdParameter, out string? triggeringBuildIdString) || !int.TryParse(triggeringBuildIdString, out triggeringBuildId))
            {
                LogAndThrow($"A worker build needs the value {Constants.TriggeringAdoBuildIdParameter} in the trigger info to be set to the orchestrator's ADO build id to connect to the build. Found this value for: {triggeringBuildIdString}");
            }

            var elapsedTime = 0;

            m_logger.Info($"Orchestrator build: {GetBuildLinkFromId(triggeringBuildId)}");

            // At this point we can perform the sanity checks for the workers against the orchestrator
            await VerifyWorkerCorrectness(buildContext, triggeringBuildId);

            m_logger.Info($"Querying the build properties of build {triggeringBuildId} for the build information");
            while (elapsedTime < m_maxWaitingTimeSeconds)
            {
                var properties = await GetBuildProperties(triggeringBuildId);
                var maybeInfo = properties.GetValue<string?>(buildContext.InvocationKey, null);
                if (maybeInfo != null)
                {
                    return BuildInfo.Deserialize(maybeInfo);
                }

                m_logger.Info($"Couldn't find the build info in the build properties: retrying in {Constants.PollRetryPeriodInSeconds} Seconds(s)...");
                await Task.Delay(TimeSpan.FromSeconds(Constants.PollRetryPeriodInSeconds));
                elapsedTime += Constants.PollRetryPeriodInSeconds;
            }

            LogAndThrow($"Waiting for orchestrator address failed after {m_maxWaitingTimeSeconds} seconds. Aborting...");
            return null;
        }

        private async Task WaitForAgentsToBeReady(AgentType type)
        {
            var otherAgentsAreReady = false;
            var elapsedTime = 0;

            while (!otherAgentsAreReady && elapsedTime < m_maxWaitingTimeSeconds)
            {
                List<TimelineRecord> records = await GetTimelineRecords();

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

        /// <inheritdoc />
        public Task QueueBuildAsync(int pipelineId, 
            string sourceBranch, 
            string sourceVersion, 
            Dictionary<string, string>? parameters = null, 
            Dictionary<string, string>? templateParameters = null,
            Dictionary<string, string>? triggerInfo = null)
        {
            return m_http.QueuePipelineAsync(pipelineId, sourceBranch, sourceVersion, parameters, templateParameters, triggerInfo);
        }

        [DoesNotReturn]
        private void LogAndThrow(string error)
        {
            CoordinationException.LogAndThrow(m_logger, error);
        }

        /// <summary>
        /// Gets the build context from the parameters and environment of this 
        /// </summary>
        /// <returns></returns>
        public async Task<BuildContext> GetBuildContextAsync(string invocationId)
        {
            var buildContext = new BuildContext()
            {
                InvocationKey = invocationId,
                StartTime = await GetBuildStartTimeAsync(),
                BuildId = BuildId,
                AgentMachineName = AgentMachineName,
                SourcesDirectory = SourcesDirectory,
                RepositoryUrl = RepositoryUrl,
                ServerUrl = ServerUri,
                TeamProjectId = TeamProjectId,
            };

            return buildContext;
        }
    }
}
