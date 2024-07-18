// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using AdoBuildRunner;
using AdoBuildRunner.Vsts;
using BuildXL.AdoBuildRunner.Build;
using Microsoft.VisualStudio.Services.WebApi;

#nullable enable

namespace BuildXL.AdoBuildRunner.Vsts
{
    /// <summary>
    /// Concrete implementation of the VSTS API interface for build coordination purposes
    /// </summary>
    public class AdoBuildRunnerService : IAdoBuildRunnerService
    {
        /// <inheritdoc />
        public IAdoEnvironment AdoEnvironment { get; }

        /// <inheritdoc />
        public IAdoBuildRunnerConfig Config { get; }

        /// <nodoc />
        private int TotalJobsInPhase { get; }

        /// <nodoc />
        private int JobPositionInPhase { get; }

        private enum AgentType
        {
            Orchestrator,
            Worker
        }

        private readonly ILogger m_logger;

        /// <summary>
        /// Generate a build URL from a build id
        /// </summary>
        private string GetBuildLinkFromId(int buildId) => $"{AdoEnvironment.CollectionUrl}/{AdoEnvironment.TeamProject}/_build/results?buildid={buildId}";

        private readonly AdoBuildRunnerRetryHandler m_retryHandler;

        private readonly IAdoAPIService m_adoAPIService;

        /// <nodoc />
        public AdoBuildRunnerService(ILogger logger)
        {
            m_logger = logger;
            AdoEnvironment = new AdoEnvironment(m_logger);
            Config = new AdoBuildRunnerConfig(m_logger);
            m_retryHandler = new AdoBuildRunnerRetryHandler(Constants.MaxApiAttempts);
            m_adoAPIService = new AdoApiService(m_logger, AdoEnvironment, Config);
        }

        /// <summary>
        /// Constructor for testing purpose.
        /// </summary>
        public AdoBuildRunnerService(ILogger logger, IAdoEnvironment adoBuildRunnerEnvConfig, IAdoAPIService adoAPIService, IAdoBuildRunnerConfig adoBuildRunnerUserConfig)
        {
            AdoEnvironment = adoBuildRunnerEnvConfig;
            m_logger = logger;
            m_adoAPIService = adoAPIService;
            m_retryHandler = new AdoBuildRunnerRetryHandler(Constants.MaxApiAttempts);
            Config = adoBuildRunnerUserConfig;
        }

        /// <inheritdoc />
        private async Task<DateTime> GetBuildStartTimeAsync()
        {
            if (AdoEnvironment.BuildId <= 0)
            {
                LogAndThrow($"{Constants.BuildIdVarName} is not set or cannot be parsed into an int value");
            }

            var build = await m_retryHandler.ExecuteAsync(() => m_adoAPIService.GetBuildAsync(AdoEnvironment.BuildId), nameof(m_adoAPIService.GetBuildAsync), m_logger);
            return build.StartTime.GetValueOrDefault();
        }

        /// <summary>
        /// Adds or updates build properties atomically for the specifies build id.
        /// </summary>
        private Task AddBuildProperty(int buildId, string property, string value)
        {
            // UpdateBuildProperties is ultimately an HTTP PATCH: the new properties specified will be added to the existing ones
            // in an atomic fashion. So we don't have to worry about multiple builds concurrently calling UpdateBuildPropertiesAsync
            // as long as the keys don't clash.
            PropertiesCollection patch = new()
            {
                { property, value }
            };

            return m_retryHandler.ExecuteAsync(() => m_adoAPIService.UpdateBuildPropertiesAsync(patch, buildId), nameof(m_adoAPIService.UpdateBuildPropertiesAsync), m_logger);
        }

        /// <inheritdoc />
        public async Task PublishBuildInfo(BuildContext buildContext, BuildInfo buildInfo)
        {
            // Check that the build identifier was not used before - this is to catch user specification errors 
            // where some pipeline uses the same identifier for two different BuildXL invocations.
            // Note that this check is racing against a concurrent update, but this doesn't matter because
            // we expect to catch it most of times, and that's all we need for the error to be surfaced and fixed.            
            var properties = await m_retryHandler.ExecuteAsync(() => m_adoAPIService.GetBuildPropertiesAsync(AdoEnvironment.BuildId), nameof(m_adoAPIService.GetBuildPropertiesAsync), m_logger);
            if (properties.ContainsKey(buildContext.InvocationKey))
            {
                LogAndThrow($"A build with identifier '{buildContext.InvocationKey}' is already running in this pipeline. " +
                    $"Identifiers (set through the environment variable '{Constants.AdoBuildRunnerInvocationKey}') should be unique" +
                    $" for invocations within the same ADO Build.");
            }

            await AddBuildProperty(AdoEnvironment.BuildId, buildContext.InvocationKey, buildInfo.Serialize());
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
            var orchestratorBuild = await m_retryHandler.ExecuteAsync(() => m_adoAPIService.GetBuildAsync(orchestratorBuildId), nameof(m_adoAPIService.GetBuildAsync), m_logger);

            // (1) Source branch / version should match
            if (orchestratorBuild.SourceBranch != AdoEnvironment.LocalSourceBranch || orchestratorBuild.SourceVersion != AdoEnvironment.LocalSourceVersion)
            {
                LogAndThrow($"Version control mismatch between the orchestrator build. Ensure the worker pipeline is triggered with the same sources as the orchestrator pipeline." + Environment.NewLine
                            + $"This build: SourceBranch='{AdoEnvironment.LocalSourceBranch}', SourceVersion='{AdoEnvironment.LocalSourceVersion}'" + Environment.NewLine
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

            var properties = await m_retryHandler.ExecuteAsync(() => m_adoAPIService.GetBuildPropertiesAsync(orchestratorBuildId), nameof(m_adoAPIService.GetBuildPropertiesAsync), m_logger);
            var workerInvocationSentinel = $"{buildContext.InvocationKey}__workerjobid";
            if (properties.ContainsKey(workerInvocationSentinel))
            {
                var value = properties.GetValue(workerInvocationSentinel, string.Empty);
                if (value != AdoEnvironment.JobId)
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

        /// <inheritdoc />
        public async Task<BuildInfo> WaitForBuildInfo(BuildContext buildContext)
        {
            var triggerInfo = await m_retryHandler.ExecuteAsync(() => m_adoAPIService.GetBuildTriggerInfoAsync(), nameof(m_adoAPIService.GetBuildTriggerInfoAsync), m_logger);
            if (triggerInfo == null
                || !triggerInfo.TryGetValue(Constants.TriggeringAdoBuildIdParameter, out string? triggeringBuildIdString)
                || !int.TryParse(triggeringBuildIdString, out int triggeringBuildId))
            {
                m_logger.Info("Couldn't find trigger info for this build. Assuming it is being ran on the same pipeline as the orchestrator and using the current build id as the orchestrator's build id");
                triggeringBuildId = AdoEnvironment.BuildId;
            }

            var elapsedTime = 0;

            m_logger.Info($"Orchestrator build: {GetBuildLinkFromId(triggeringBuildId)}");

            // At this point we can perform the sanity checks for the workers against the orchestrator
            await VerifyWorkerCorrectness(buildContext, triggeringBuildId);

            m_logger.Info($"Querying the build properties of build {triggeringBuildId} for the build information");
            while (elapsedTime < Config.MaximumWaitForWorkerSeconds)
            {
                var properties = await m_retryHandler.ExecuteAsync(() => m_adoAPIService.GetBuildPropertiesAsync(triggeringBuildId), nameof(m_adoAPIService.GetBuildPropertiesAsync), m_logger);
                var maybeInfo = properties.GetValue<string?>(buildContext.InvocationKey, null);
                if (maybeInfo != null)
                {
                    return BuildInfo.Deserialize(maybeInfo);
                }

                m_logger.Info($"Couldn't find the build info in the build properties: retrying in {Constants.PollRetryPeriodInSeconds} Seconds(s)...");
                await Task.Delay(TimeSpan.FromSeconds(Constants.PollRetryPeriodInSeconds));
                elapsedTime += Constants.PollRetryPeriodInSeconds;
            }

            LogAndThrow($"Waiting for orchestrator address failed after {Config.MaximumWaitForWorkerSeconds} seconds. Aborting...");
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
        public async Task<BuildContext> GetBuildContextAsync(string invocationId)
        {
            var buildContext = new BuildContext()
            {
                InvocationKey = invocationId,
                StartTime = await GetBuildStartTimeAsync(),
                BuildId = AdoEnvironment.BuildId,
                AgentMachineName = AdoEnvironment.AgentMachineName,
                AgentHostName = $"{AdoEnvironment.AgentMachineName}.internal.cloudapp.net",  // see https://learn.microsoft.com/en-us/azure/virtual-network/virtual-networks-name-resolution-for-vms-and-role-instances
                SourcesDirectory = AdoEnvironment.SourcesDirectory,
                RepositoryUrl = AdoEnvironment.RepositoryUrl,
                ServerUrl = AdoEnvironment.ServerUri,
                TeamProjectId = AdoEnvironment.TeamProjectId,
            };

            return buildContext;
        }

        /// <inheritdoc />
        public MachineRole GetRole() 
        {
            // For now, we explicitly mark the role with an environment
            // variable. When we discontinue the "non-worker-pipeline" approach, we can
            // infer the role from the parameters, but for now there is no easy way
            // to distinguish runs using the "worker-pipeline" model from ones who don't
            // When the build role is not specified, we assume this build is being run with the parallel strategy
            // where the role is inferred from the ordinal position in the phase: the first agent is the orchestrator
            var role = Config.AdoBuildRunnerPipelineRole;

            if (string.IsNullOrEmpty(role) && JobPositionInPhase == 1)
            {
                return MachineRole.Orchestrator;
            }
            else if (Enum.TryParse(role, true, out MachineRole machineRole))
            {
                if (machineRole == MachineRole.Orchestrator || machineRole == MachineRole.Worker)
                {
                    return machineRole;
                }
                else
                {
                    throw new CoordinationException($"{Constants.AdoBuildRunnerPipelineRole} must be 'Worker' or 'Orchestrator'");
                }
            }
            else
            {
                throw new CoordinationException($"{Constants.AdoBuildRunnerPipelineRole} is invalid.");
            }
        }

        /// <inheritdoc />
        public string GetInvocationKey()
        {
            var invocationKey = Config.AdoBuildRunnerInvocationKey;

            if (string.IsNullOrEmpty(invocationKey))
            {
                throw new CoordinationException($"The environment variable {Constants.AdoBuildRunnerInvocationKey} must be set (to a value that is unique within a particular pipeline run): " +
                    $"it is used to disambiguate between multiple builds running as part of the same pipeline (e.g.: debug, ship, test...) " +
                    $"and to communicate the build information to the worker pipeline");
            }

            if (AdoEnvironment.JobAttemptNumber > 1)
            {
                // The job was rerun. Let's change the invocation key to reflect that
                // so we don't conflict with the first run.
                invocationKey += $"__jobretry_{AdoEnvironment.JobAttemptNumber}";
            }

            return invocationKey;
        }

        /// <inheritdoc />
        public async Task GenerateCacheConfigFileIfNeededAsync(Logger logger, IAdoBuildRunnerConfiguration configuration, List<string> buildArgs)
        {
            // If the storage account endpoint was provided, that is taken as an indicator that the cache config needs to be generated
            if (configuration.CacheConfigGenerationConfiguration.StorageAccountEndpoint == null)
            {
                return;
            }

            string cacheConfigContent = CacheConfigGenerator.GenerateCacheConfig(configuration.CacheConfigGenerationConfiguration);
            string cacheConfigFilePath = Path.GetTempFileName();

            await File.WriteAllTextAsync(cacheConfigFilePath, cacheConfigContent);
            logger.Info($"Cache config file generated at '{cacheConfigFilePath}'");

            if (configuration.CacheConfigGenerationConfiguration.LogGeneratedConfiguration())
            {
                logger.Info($"Generated cache config file: {cacheConfigContent}");
            }

            // Inject the cache config file path as the first argument in the build arguments. This is so if there is any user override pointing to another
            // cache config file, that will be honored
            buildArgs.Insert(0, $"/cacheConfigFilePath:{cacheConfigFilePath}");
        }
    }
}