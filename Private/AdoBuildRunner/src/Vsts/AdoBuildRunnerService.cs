﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Utilities.Mocks;
using Microsoft.VisualStudio.Services.WebApi;


namespace BuildXL.AdoBuildRunner.Vsts
{
    /// <summary>
    /// Concrete implementation of the VSTS API interface for build coordination purposes
    /// </summary>
    public class AdoBuildRunnerService
    {
        /// <inheritdoc />
        public IAdoEnvironment AdoEnvironment { get; }

        /// <inheritdoc />
        public IAdoBuildRunnerConfiguration Config { get; }

        /// <inheritdoc />
        public BuildContext BuildContext { get; }

        private readonly ILogger m_logger;

        /// <summary>
        /// Generate a build URL from a build id
        /// </summary>
        private string GetBuildLinkFromId(int buildId) => $"{AdoEnvironment.CollectionUrl}/{AdoEnvironment.TeamProject}/_build/results?buildid={buildId}";

        private readonly AdoBuildRunnerRetryHandler m_retryHandler;

        private readonly IAdoService m_adoService;

        /// <nodoc />
        public AdoBuildRunnerService(IAdoBuildRunnerConfiguration config, IAdoEnvironment adoEnvironment, ILogger logger)
        {
            Config = config;
            m_logger = logger;
            AdoEnvironment = adoEnvironment;
            m_retryHandler = new AdoBuildRunnerRetryHandler(Constants.MaxApiAttempts);
            m_adoService = new AdoService(AdoEnvironment, m_logger);
            BuildContext = GetBuildContext();
#if DEBUG
            if (Environment.GetEnvironmentVariable("__ADOBR_INTERNAL_MOCK_ADO") == "1")
            {
                m_adoService = new MockAdoService(adoEnvironment);
            }
#endif
        }

        /// <summary>
        /// Constructor for testing purpose.
        /// </summary>
        public AdoBuildRunnerService(ILogger logger, IAdoEnvironment adoBuildRunnerEnvConfig, IAdoService adoService, IAdoBuildRunnerConfiguration adoBuildRunnerUserConfig)
        {
            AdoEnvironment = adoBuildRunnerEnvConfig;
            m_logger = logger;
            m_adoService = adoService;
            m_retryHandler = new AdoBuildRunnerRetryHandler(Constants.MaxApiAttempts);
            Config = adoBuildRunnerUserConfig;
            BuildContext = GetBuildContext();
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

            return m_retryHandler.ExecuteAsync(() => m_adoService.UpdateBuildPropertiesAsync(patch, buildId), nameof(m_adoService.UpdateBuildPropertiesAsync), m_logger);
        }

        /// <summary>
        /// Return the name of the pool where the agent running this build is running on.
        /// Returns the empty string if the pool name can't be resolved (possible due to service failures)
        /// </summary>
        public Task<string> GetRunningPoolNameAsync()
        {
            try
            {
                return m_retryHandler.ExecuteAsync(() => m_adoService.GetPoolNameAsync(), nameof(m_adoService.GetPoolNameAsync), m_logger);
            }
            catch (Exception)
            {
                // We use the pool name as a sanity check - let's not fail if this operation fails.
                // this will trigger a warning
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                return Task.FromResult(string.Empty);
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
            }
        }

        /// <inheritdoc />
        public async Task PublishBuildInfo(BuildInfo buildInfo)
        {
            // Check that the build identifier was not used before - this is to catch user specification errors 
            // where some pipeline uses the same identifier for two different BuildXL invocations.
            // Note that this check is racing against a concurrent update, but this doesn't matter because
            // we expect to catch it most of times, and that's all we need for the error to be surfaced and fixed.            
            var properties = await m_retryHandler.ExecuteAsync(() => m_adoService.GetBuildPropertiesAsync(AdoEnvironment.BuildId), nameof(m_adoService.GetBuildPropertiesAsync), m_logger);
            if (properties.ContainsKey(BuildContext.InvocationKey))
            {
                LogAndThrow($"A build with identifier '{BuildContext.InvocationKey}' is already running in this pipeline. " +
                    $"Identifiers (set through the environment variable '{Constants.InvocationKey}') should be unique" +
                    $" for invocations within the same ADO Build.");
            }

            await AddBuildProperty(AdoEnvironment.BuildId, BuildContext.InvocationKey, buildInfo.Serialize());
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
        private async Task VerifyWorkerCorrectness(BuildInfo buildInfo, int orchestratorBuildId)
        {
            var orchestratorBuild = await m_retryHandler.ExecuteAsync(() => m_adoService.GetBuildAsync(orchestratorBuildId), nameof(m_adoService.GetBuildAsync), m_logger);

            // (1) Source branch / version should match
            if (orchestratorBuild.SourceBranch != AdoEnvironment.LocalSourceBranch || orchestratorBuild.SourceVersion != AdoEnvironment.LocalSourceVersion)
            {
                LogAndThrow($"Version control mismatch between the orchestrator build. Ensure the worker pipeline is triggered with the same sources as the orchestrator pipeline." + Environment.NewLine
                            + $"This build: SourceBranch='{AdoEnvironment.LocalSourceBranch}', SourceVersion='{AdoEnvironment.LocalSourceVersion}'" + Environment.NewLine
                            + $"Orchestrator build (id={orchestratorBuildId}): SourceBranch='{orchestratorBuild.SourceBranch}', SourceVersion='{orchestratorBuild.SourceVersion}'");
            }

#pragma warning restore ERP022 // Unobserved exception in a generic exception handler

            // (2) One-to-one correspondence between a jobs in a worker pipeline, and an orchestrator
            if (AdoEnvironment.JobPositionInPhase != AdoEnvironment.TotalJobsInPhase)
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

            var properties = await m_retryHandler.ExecuteAsync(() => m_adoService.GetBuildPropertiesAsync(orchestratorBuildId), nameof(m_adoService.GetBuildPropertiesAsync), m_logger);
            var workerInvocationSentinel = $"{BuildContext.InvocationKey}__workerjobid";
            if (properties.ContainsKey(workerInvocationSentinel))
            {
                var value = properties.GetValue(workerInvocationSentinel, string.Empty);
                if (!string.IsNullOrEmpty(value))
                {
                    LogAndThrow($"All workers participating in the build '{BuildContext.InvocationKey}' must originate from the same parallel job. This failure probably means that some pipeline specification is duplicating invocation keys");
                }
            }
            else
            {
                m_logger?.Debug($"No property found with the key {workerInvocationSentinel}");
            }

            // "Claim" this invocation key for this job by publishing the sentinel in the properties,
            // so subsequent jobs that may be running in different will fail the above check
            await AddBuildProperty(orchestratorBuildId, workerInvocationSentinel, "1");
        }

        /// <inheritdoc />
        public async Task<BuildInfo> WaitForBuildInfo()
        {
            var triggerInfo = await m_retryHandler.ExecuteAsync(() => m_adoService.GetBuildTriggerInfoAsync(), nameof(m_adoService.GetBuildTriggerInfoAsync), m_logger);
            if (triggerInfo == null
                || !triggerInfo.TryGetValue(Constants.TriggeringAdoBuildIdParameter, out string? triggeringBuildIdString)
                || !int.TryParse(triggeringBuildIdString, out int triggeringBuildId))
            {
                triggeringBuildId = AdoEnvironment.BuildId;
            }
            else
            {
                m_logger.Info($"Orchestrator build: {GetBuildLinkFromId(triggeringBuildId)}");
            }

            var elapsedTime = 0;

            m_logger.Info($"Polling the build properties for the orchestrator's information (this operation will timeout in {Config.MaximumWaitForWorkerSeconds} seconds)...");
            while (elapsedTime < Config.MaximumWaitForWorkerSeconds)
            {
                var properties = await m_retryHandler.ExecuteAsync(() => m_adoService.GetBuildPropertiesAsync(triggeringBuildId), nameof(m_adoService.GetBuildPropertiesAsync), m_logger);
                var maybeInfo = properties.GetValue<string?>(BuildContext.InvocationKey, null);
                if (maybeInfo != null)
                {
                    m_logger.Info($"Orchestrator information retrieved from build properties.");

                    var buildInfo = BuildInfo.Deserialize(maybeInfo);

                    // At this point we can perform the sanity checks for the workers against the orchestrator
                    await VerifyWorkerCorrectness(buildInfo, triggeringBuildId);
                    return buildInfo;
                }

                await Task.Delay(TimeSpan.FromSeconds(Constants.PollRetryPeriodInSeconds));
                elapsedTime += Constants.PollRetryPeriodInSeconds;
            }

            LogAndThrow($"Did not receive the orchestrator information after {Config.MaximumWaitForWorkerSeconds} seconds." +
                $"The task will be aborted, but note that this timeout is by design (and configurable with the maximumWaitForAgentSeconds option)." +
                $"This error means that either the job in the orchestrator agent failed before running the BuildXL task, " +
                $"or that said task did not start before this agent hit the timeout. " +
                $"The latter can happen naturally due to variance in the agents' starting time and/or the durations of pre-build tasks." +
                "Comparing the timestamps of the orchestrator task with this task will clarify which scenario was hit.");

            return null;
        }

        [DoesNotReturn]
        private void LogAndThrow(string error)
        {
            CoordinationException.LogAndThrow(m_logger, error);
        }

        /// <summary>
        /// Gets the build context from the parameters and environment of this.
        /// </summary>
        /// <remarks>
        /// This method should be idempotent: we consider the AdoEnvironment unchanging throuhgouht the build
        /// </remarks>
        private BuildContext GetBuildContext()
        {
            var buildContext = new BuildContext()
            {
                InvocationKey = GetInvocationKey(),
                BuildId = AdoEnvironment.BuildId,
                AgentPool = GetRunningPoolNameAsync().GetAwaiter().GetResult(),
                AgentMachineName = AdoEnvironment.AgentMachineName,
                AgentHostName = $"{AdoEnvironment.AgentMachineName}.internal.cloudapp.net",  // see https://learn.microsoft.com/en-us/azure/virtual-network/virtual-networks-name-resolution-for-vms-and-role-instances
                SourcesDirectory = AdoEnvironment.SourcesDirectory,
            };

            return buildContext;
        }

        /// <inheritdoc />
        public string GetInvocationKey()
        {
            var invocationKey = Config.InvocationKey;

            if (string.IsNullOrEmpty(invocationKey))
            {
                throw new CoordinationException($"The environment variable {Constants.InvocationKey} must be set (to a value that is unique within a particular pipeline run): " +
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
        public static async Task GenerateCacheConfigFileIfNeededAsync(ILogger logger, ICacheConfigGenerationConfiguration configuration, List<string> buildArgs, string? tempFileName = null)
        {
            if (!configuration.ShouldGenerateCacheConfigurationFile())
            {
                return;
            }

            string cacheConfigContent = CacheConfigGenerator.GenerateCacheConfig(configuration);
            string cacheConfigFilePath = tempFileName ?? Path.GetTempFileName();

            await File.WriteAllTextAsync(cacheConfigFilePath, cacheConfigContent);
            logger.Info($"Cache config file generated at '{cacheConfigFilePath}'");

            if (configuration.LogGeneratedConfiguration())
            {
                logger.Info($"Generated cache config file: {cacheConfigContent}");
            }

            // Inject the cache config file path as the first argument in the build arguments. This is so if there is any user override pointing to another
            // cache config file, that will be honored
            buildArgs.Insert(0, $"/cacheConfigFilePath:{cacheConfigFilePath}");
        }

        /// <inheritdoc />
        public async Task<string?> GetBuildProperty(string propertyName)
        {
            var properties = await m_retryHandler.ExecuteAsync(() => m_adoService.GetBuildPropertiesAsync(AdoEnvironment.BuildId), nameof(GetBuildProperty), m_logger);
            return properties.GetValue<string?>(GetPropertyKey(propertyName), null);
        }

        /// <inheritdoc />
        public Task PublishBuildProperty(string propertyName, string value) => AddBuildProperty(AdoEnvironment.BuildId, GetPropertyKey(propertyName), value);

        /// <summary>
        /// Returns a unique key for a property associated to this build session
        /// </summary>
        private string GetPropertyKey(string propertyName) => $"{BuildContext.InvocationKey}-{propertyName}";

        /// <summary>
        /// Sets an ADO variable to consume on subsequent tasks
        /// </summary>
        public void SetVariable(string variableName, string value)
        {
            Console.WriteLine($"##[debug] Setting $({variableName})={value}");
            Console.WriteLine($"##vso[task.setvariable variable={variableName};]{value}");
        }
    }
}