// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.AdoBuildRunner.Vsts;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Main configuration object for the ADO build runner
    /// </summary>
    public interface IAdoBuildRunnerConfiguration
    {
        /// <summary>
        /// The directory containing the BuildXL engine executable
        /// </summary>
        public string EngineLocation { get; }


        /// <summary>
        /// Key used to disambiguate between invocations happening in the same ADO build
        /// </summary>
        public string InvocationKey { get; }

        /// <summary>
        /// Specifies the role of the pipeline in the ADO build runner
        /// </summary>
        public AgentRole? AgentRole { get; }

        /// <summary>
        /// Indicates whether the worker always reports success regardless of actual outcome
        /// </summary>
        public bool WorkerAlwaysSucceeds { get; }

        /// <summary>
        /// Maximum time to wait for a worker to be available, in seconds
        /// </summary>
        public int MaximumWaitForWorkerSeconds { get; }

        /// <summary>
        /// Disables encryption for the build process
        /// </summary>
        public bool DisableEncryption { get; }

        /// <summary>
        /// Input needed to generate a cache config file
        /// </summary>
        public ICacheConfigGenerationConfiguration CacheConfigGenerationConfiguration { get; }

        /// <summary>
        /// Set fallback values to ensure backwards compatibility until the changes are applied in the 1ESPT repo.
        /// See Bug: 2199401 for more details.
        /// </summary>
        public void PopulateFromEnvVars(IAdoEnvironment adoEnvironment, ILogger logger);

        // If you add more fields here please update the ValidateConfiguration method below
    }

    /// <nodoc/>
    public static class AdoBuildRunnerConfigurationExtensions
    {
        /// <summary>
        /// Throws an <see cref="ArgumentException"/> on error
        /// </summary>
        public static void ValidateConfiguration(this IAdoBuildRunnerConfiguration configuration)
        {
            configuration.CacheConfigGenerationConfiguration.ValidateConfiguration();
        }
    }
}
