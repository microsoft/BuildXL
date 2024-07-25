// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using AdoBuildRunner;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Main configuration object for the ADO build runner
    /// </summary>
    public interface IAdoBuildRunnerConfiguration
    {
        /// <summary>
        /// Key used for invoking specific build runner logic
        /// </summary>
        public string InvocationKey { get; }

        /// <summary>
        /// Specifies the role of the pipeline in the ADO build runner
        /// </summary>
        public MachineRole PipelineRole { get; }

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
