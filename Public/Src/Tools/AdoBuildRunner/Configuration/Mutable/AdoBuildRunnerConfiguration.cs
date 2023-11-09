// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.AdoBuildRunner
{
    /// <nodoc/>
    public sealed class AdoBuildRunnerConfiguration : IAdoBuildRunnerConfiguration
    {
        /// <nodoc/>
        public AdoBuildRunnerConfiguration()
        {
            CacheConfigGenerationConfiguration = new CacheConfigGenerationConfiguration();
        }

        /// <nodoc/>
        public CacheConfigGenerationConfiguration CacheConfigGenerationConfiguration { get; set; }

        /// <nodoc/>
        ICacheConfigGenerationConfiguration IAdoBuildRunnerConfiguration.CacheConfigGenerationConfiguration => CacheConfigGenerationConfiguration;
    }
}