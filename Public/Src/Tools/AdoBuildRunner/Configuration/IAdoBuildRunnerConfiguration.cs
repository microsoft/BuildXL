// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Main configuration object for the ADO build runner
    /// </summary>
    public interface IAdoBuildRunnerConfiguration
    {
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
