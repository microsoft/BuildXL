// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
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
        private readonly ILogger m_logger;

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

        private Task<PropertiesCollection> GetBuildProperties(int buildId)
        {
            // Get properties for this build if a build id is not specified
            return m_buildClient.GetBuildPropertiesAsync(new Guid(TeamProjectId), buildId);
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
            if (JobPositionInPhase != TotalJobsInPhase)
            {
                // Only do this once per 'parallel group', i.e., only for the last agent in a parallel strategy context
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
        public async Task<BuildInfo> WaitForBuildInfo(BuildContext buildContext)
        {
            var triggerInfo = await m_http.GetBuildTriggerInfoAsync();
            if (triggerInfo == null 
                || !triggerInfo.TryGetValue(Constants.TriggeringAdoBuildIdParameter, out string? triggeringBuildIdString) 
                || !int.TryParse(triggeringBuildIdString, out int triggeringBuildId))
            {
                m_logger.Info("Couldn't find trigger info for this build. Assuming it is being ran on the same pipeline as the orchestrator and using the current build id as the orchestrator's build id");
                triggeringBuildId = int.Parse(BuildId);
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
                AgentHostName = $"{AgentMachineName}.internal.cloudapp.net",  // see https://learn.microsoft.com/en-us/azure/virtual-network/virtual-networks-name-resolution-for-vms-and-role-instances
                SourcesDirectory = SourcesDirectory,
                RepositoryUrl = RepositoryUrl,
                ServerUrl = ServerUri,
                TeamProjectId = TeamProjectId,
            };

            return buildContext;
        }
    }
}
