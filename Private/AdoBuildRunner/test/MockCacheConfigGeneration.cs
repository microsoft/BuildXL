// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.AdoBuildRunner;
using Cachetype = BuildXL.AdoBuildRunner.CacheType;

namespace Test.Tool.AdoBuildRunner
{
    /// <summary>
    /// Mock implementation of ICacheConfigGenerationConfiguration for testing purpose.
    /// </summary>
    public class MockCacheConfigGeneration : ICacheConfigGenerationConfiguration
    {
        /// <nodoc/>
        public Uri? StorageAccountEndpoint { get; set; }

        /// <nodoc/>
        public Guid ManagedIdentityId { get; set; }

        /// <nodoc/>
        public int? RetentionPolicyInDays { get; set; }

        /// <nodoc/>
        public string Universe { get; set; }

        /// <nodoc/>
        public int? CacheSizeInMB { get; set; }

        /// <nodoc/>
        public string CacheId { get; set; }

        /// <nodoc/>
        public CacheType? CacheType { get; set; }

        /// <nodoc/>
        public bool? LogGeneratedConfiguration { get; set; }

        /// <nodoc/>
        public string? HostedPoolActiveBuildCacheName { get; set; }

        /// <nodoc/>
        public string? HostedPoolBuildCacheConfigurationFile { get; set; }

        /// <nodoc/>
        public MockCacheConfigGeneration()
        {
            CacheType = Cachetype.Blob;
            HostedPoolBuildCacheConfigurationFile = string.Empty;
            HostedPoolActiveBuildCacheName = "MyCacheResource";
            StorageAccountEndpoint = new Uri("https://test.cacheresource.com");
            ManagedIdentityId = new Guid("{00000000-0000-0000-0000-000000000000}");
            LogGeneratedConfiguration = true;
            Universe = "MyCacheUniverse";
            CacheSizeInMB = 200;
            CacheId = "12345";
            RetentionPolicyInDays = 30;
        }
    }
}