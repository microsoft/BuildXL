// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;


namespace BuildXL.AdoBuildRunner
{
    /// <nodoc/>
    public sealed class CacheConfigGenerationConfiguration : ICacheConfigGenerationConfiguration
    {
        /// <nodoc/>
        public Uri? StorageAccountEndpoint { get; set; }

        /// <nodoc/>
        public Guid ManagedIdentityId { get; set; }

        /// <nodoc/>
        public int? RetentionPolicyInDays { get; set; }

        /// <nodoc/>
        public string? Universe { get; set; }

        /// <nodoc/>
        public int? CacheSizeInMB { get; set; }

        /// <nodoc/>
        public string? CacheId { get; set; }

        /// <nodoc/>
        public CacheType? CacheType { get; set; }

        /// <nodoc/>
        public bool GenerateCacheConfig { get; set; }

        /// <nodoc/>
        public bool? LogGeneratedConfiguration { get; set; }

        /// <nodoc/>
        public string? HostedPoolActiveBuildCacheName { get; set; }

        /// <nodoc/>
        public string? HostedPoolBuildCacheConfigurationFile { get; set; }

        /// <nodoc/>
        public CacheConfigGenerationConfiguration()
        {
            HostedPoolBuildCacheConfigurationFile = CacheConfigGenerationConfigurationDefaults.GetHostedPoolBuildCacheConfigurationFile();
        }
    }
}