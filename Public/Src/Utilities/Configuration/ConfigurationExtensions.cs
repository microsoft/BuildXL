// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Set of extension methods for <see cref="IConfiguration"/>.
    /// </summary>
    public static class ConfigurationExtensions
    {
        /// <summary>
        /// Whether this build is running in CloudBuild.
        /// </summary>
        public static bool InCloudBuild(this IConfiguration configuration)
        {
            return configuration.InCloudBuild ?? false;
        }

        /// <summary>
        /// Whether to enable distributed source file hashing.
        /// </summary>
        public static bool EnableDistributedSourceHashing(this IConfiguration configuration)
        {
            return configuration.InCloudBuild()
                && !configuration.Distribution.VerifySourceFilesOnWorkers
                && configuration.Schedule.ForceSkipDependencies == ForceSkipDependenciesMode.Disabled;
        }

        /// <summary>
        /// Disable the default source resolver (as the last considered resolver) when set to true.
        /// </summary>
        public static bool DisableDefaultSourceResolver(this IConfiguration configuration)
        {
            // The default source resolver is still needed when modules or projects are specified. So in case an explicit value is not provided
            // disable it when no modules nor projects are defined. In this way we avoid the enumeration of the whole repo looking for DScript files.
            return configuration.DisableDefaultSourceResolver 
                ?? ((configuration.Modules == null || configuration.Modules.Count == 0) 
                && (configuration.Packages == null || configuration.Packages.Count == 0)
                && (configuration.Projects == null || configuration.Projects.Count == 0));
        }

        /// <summary>
        /// Whether full reparse point resolving should be enabled
        /// </summary>
        /// <remarks>
        /// The value can be explicitly configured with <see cref="IUnsafeSandboxConfiguration.EnableFullReparsePointResolving"/>, in which case it is honored. Otherwise
        /// if any configured resolver requires it via <see cref="IResolverSettings.RequestFullReparsePointResolving"/>, then it is enabled.
        /// </remarks>
        public static bool EnableFullReparsePointResolving(this IConfiguration configuration)
        {
            if (configuration.Sandbox.UnsafeSandboxConfiguration.EnableFullReparsePointResolving.HasValue)
            {
                return configuration.Sandbox.UnsafeSandboxConfiguration.EnableFullReparsePointResolving.Value;
            }

            if (configuration.Resolvers.Any(resolver => resolver.RequestFullReparsePointResolving))
            {
                return true;
            }

            return configuration.Sandbox.UnsafeSandboxConfiguration.EnableFullReparsePointResolving();
        }

        /// <summary>
        /// False unless specified otherwise
        /// </summary>
        public static bool DisableInBoxSdkSourceResolver(this IConfiguration configuration)
        {
            return configuration.DisableInBoxSdkSourceResolver ?? false;
        }

        /// <summary>
        /// Whether this build should store fingerprints
        /// </summary>
        public static bool FingerprintStoreEnabled(this IConfiguration configuration)
        {
            // Distributed workers send their execution events back to orchestrator,
            // to reduce storage needed on workers, workers do not need a fingerprint store

            return configuration.Logging.StoreFingerprints.HasValue
                && configuration.Logging.StoreFingerprints.Value
                && configuration.Distribution.BuildRole != DistributedBuildRoles.Worker
                && configuration.Layout.FingerprintStoreDirectory.IsValid
                && configuration.Engine.Phase.HasFlag(EnginePhases.Execute);
        }

        /// <summary>
        /// Gets the update and delay time for status timers
        /// </summary>
        public static int GetTimerUpdatePeriodInMs(this ILoggingConfiguration loggingConfig)
        {
            if (loggingConfig != null)
            {
                if (loggingConfig.OptimizeConsoleOutputForAzureDevOps || loggingConfig.OptimizeProgressUpdatingForAzureDevOps || loggingConfig.OptimizeVsoAnnotationsForAzureDevOps)
                {
                    return 10_000;
                }

                if (loggingConfig.FancyConsole)
                {
                    return 2_000;
                }
            }

            return 5_000;
        }

        /// <summary>
        /// Creates a <see cref="ConfigurationStatistics"/> based on the given configuration
        /// </summary>
        public static ConfigurationStatistics GetStatistics(this IConfiguration configuration)
        {
            // Get a stable list of the different kind of resolvers used in this build
            IEnumerable<string> kinds = (configuration.Resolvers ?? CollectionUtilities.EmptyArray<IResolverSettings>())
                .Select(resolver => resolver.Kind)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(kind => kind, StringComparer.Ordinal);

            return new ConfigurationStatistics(kinds);
        }
    }
}
