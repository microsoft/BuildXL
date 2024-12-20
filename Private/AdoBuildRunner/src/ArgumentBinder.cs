// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Parsing;
using System.Linq;


namespace BuildXL.AdoBuildRunner
{
    internal class ArgumentBinder : BinderBase<IAdoBuildRunnerConfiguration>
    {
        private readonly Option<string> m_engineLocationOption = new(
                name: "--engineLocation",
                description: "Specifies the location of the BuildXL engine. If not specified, the engine is assumed to be in the same directory as this executable.");

        private readonly Option<AgentRole?> m_agentRoleOption = new(
                name: "--agentRole",
                description: "Whether this agent will act as the orchestrator of a worker for the build.")
        {
            // TODO: Make this required once we transition all callers
            //  IsRequired = true,
        };

        private readonly Option<string> m_invocationKey = new(
                name: "--invocationKey",
                description: "Used to disambiguate between invocations happening in the same ADO build. The value should be unique across a pipeline definition."
        )
        {
            // TODO: Make this required once we transition all callers
            //  IsRequired = true,
        };

        private readonly Option<bool> m_disableEncryption = new(
            name: "--disableEncryption",
            description: "Whether to disable encryption",
            getDefaultValue: () => false);

        private readonly Option<int?> m_cacheConfigSizeInMb = new(
                    name: "--cacheConfigCacheSizeInMB",
                    description: "The cache size in megabytes");

        private readonly Option<bool> m_cacheConfigLogGeneratedConfiguration = new(
                    name: "--cacheConfigLogGeneratedConfiguration",
                    description: "Whether to log the generated cache configuration for debug purposes",
                    getDefaultValue: () => false);

        private readonly Option<CacheType?> m_cacheConfigCacheType = new Option<CacheType?>(
                            name: "--cacheConfigCacheType",
                            description: "The cache type");

        private readonly Option<Guid> m_cacheConfigManagedIdentityId = new(
                            name: "--cacheConfigManagedIdentityId",
                            description: "The cache config managed identity id",
                            parseArgument: ParseGuid);

        private static Guid ParseGuid(ArgumentResult result)
        {
            var provided = result.Tokens.Single().Value;
            if (Guid.TryParse(provided, out var guid))
            {
                return guid;
            }

            result.ErrorMessage = $"The specified value for {result.Argument.Name} should be a GUID. '{provided}' has an incorrect format.";
            return default;
        }

        private static Uri? ParseUri(ArgumentResult result)
        {
            var providedValue = result.Tokens.Single().Value;
            if (Uri.TryCreate(providedValue, UriKind.Absolute, out var uri))
            {
                return uri;
            }
            else
            {
                result.ErrorMessage = $"The value for {result.Argument.Name} should be a valid URI (provided value: {providedValue}).";
                return null;
            }
        }

        private readonly Option<int?> m_cacheConfigRetentionPolicyInDays = new(
                            name: "--cacheConfigRetentionPolicyInDays",
                            description: "Number of days to preserve cache entries");

        private readonly Option<Uri?> m_cacheConfigStorageAccountEndpoint = new(
                                    name: "--cacheConfigStorageAccountEndpoint",
                                    description: "Storage account endpoint",
                                    parseArgument: ParseUri);

        private readonly Option<string?> m_cacheConfigUniverse = new(
                                    name: "--cacheConfigUniverse",
                                    description: "Name for the cache universe");

        private readonly Option<string?> m_hostedPoolActiveBuildCacheName = new(
                                    name: "--hostedPoolActiveBuildCacheName",
                                    description: "Name of the active cache within Hosted Pools to use");

        private readonly Option<int> m_maximumWaitForWorkerSeconds = new(
                                    name: "--maximumWaitForAgentSeconds",
                                    description: "Specifies a timeout while working for agents while doing ADO coordination",
                                    getDefaultValue: () => Constants.DefaultMaximumWaitForWorkerSeconds);

        private readonly Option<bool> m_workerAlwaysSucceeds = new(
                                            name: "--workerAlwaysSucceeds",
                                            description: "If specified, worker agents always exit the runner with a 0 exit code",
                                            getDefaultValue: () => false);

        public ArgumentBinder(RootCommand argumentParserCommand) => AddOptions(argumentParserCommand);

        protected override IAdoBuildRunnerConfiguration GetBoundValue(BindingContext bindingContext)
        {
            T? get<T>(Option<T> o) => bindingContext.ParseResult.GetValueForOption(o);

            var config = new AdoBuildRunnerConfiguration(get(m_engineLocationOption)!, get(m_invocationKey)!);
            var cacheConfig = config.CacheConfigGenerationConfiguration;

            // Populate from specified options
            config.AgentRole = get(m_agentRoleOption);
            config.DisableEncryption = get(m_disableEncryption);
            config.MaximumWaitForWorkerSeconds = get(m_maximumWaitForWorkerSeconds);
            config.WorkerAlwaysSucceeds = get(m_workerAlwaysSucceeds);

            cacheConfig.CacheSizeInMB = get(m_cacheConfigSizeInMb);
            cacheConfig.CacheType = get(m_cacheConfigCacheType);
            cacheConfig.Universe = get(m_cacheConfigUniverse);
            cacheConfig.LogGeneratedConfiguration = get(m_cacheConfigLogGeneratedConfiguration);
            cacheConfig.ManagedIdentityId = get(m_cacheConfigManagedIdentityId);
            cacheConfig.RetentionPolicyInDays = get(m_cacheConfigRetentionPolicyInDays);
            cacheConfig.StorageAccountEndpoint = get(m_cacheConfigStorageAccountEndpoint);
            cacheConfig.HostedPoolActiveBuildCacheName = get(m_hostedPoolActiveBuildCacheName);

            return config;
        }

        private void AddOptions(RootCommand argumentParserCommand)
        {
            void add(Option opt)
            {
                argumentParserCommand.AddOption(opt);
            }

            add(m_engineLocationOption);
            add(m_agentRoleOption);
            add(m_invocationKey);
            add(m_disableEncryption);
            add(m_maximumWaitForWorkerSeconds);
            add(m_workerAlwaysSucceeds);
            add(m_cacheConfigSizeInMb);
            add(m_cacheConfigCacheType);
            add(m_cacheConfigUniverse);
            add(m_cacheConfigLogGeneratedConfiguration);
            add(m_cacheConfigManagedIdentityId);
            add(m_cacheConfigRetentionPolicyInDays);
            add(m_cacheConfigStorageAccountEndpoint);
            add(m_hostedPoolActiveBuildCacheName);
        }
    }
}
