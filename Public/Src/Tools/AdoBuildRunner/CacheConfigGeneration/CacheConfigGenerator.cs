// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.ToolSupport;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Generates a cache config file based on the provided configuration
    /// </summary>
    /// <remarks>
    /// Defaults get picked from <see cref="CacheConfigGenerationConfigurationDefaults"/>
    /// </remarks>
    public static class CacheConfigGenerator
    {
        /// <nodoc/>
        public static string GenerateCacheConfig(ICacheConfigGenerationConfiguration configuration)
        {
            if (configuration.CacheType() == CacheType.Blob)
            {
                return GenerateBlobCacheConfig(configuration);
            }
            else if (configuration.CacheType() == CacheType.EphemeralDatacenterWide || configuration.CacheType() == CacheType.EphemeralBuildWide)
            {
                return GenerateEphemeralCacheConfig(configuration);
            }
            else
            {
                throw new InvalidArgumentException($"Unexpected cache type {configuration.CacheType()}");
            }
        }

        private static string GenerateEphemeralCacheConfig(ICacheConfigGenerationConfiguration configuration)
        {
            Contract.Requires(configuration.CacheType() == CacheType.EphemeralDatacenterWide || configuration.CacheType() == CacheType.EphemeralBuildWide);

            return @$"
{{
  ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.EphemeralCacheFactory"",
  ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
  ""CacheId"": ""{configuration.CacheId()}"",
  ""CacheLogPath"": ""[BuildXLSelectedLogPath].Ephemeral.log"",
  ""CacheRootPath"": ""[BuildXLSelectedRootPath]"",
  ""Universe"": ""{configuration.Universe()}"",
  ""LeaderMachineName"": ""[BuildXLSelectedLeader]"",
  ""DatacenterWide"": {(configuration.CacheType() == CacheType.EphemeralDatacenterWide? "true" : "false")},
  ""CacheSizeMb"": {configuration.CacheSizeInMB()},
  ""RetentionPolicyInDays"": {configuration.RetentionPolicyInDays()},
  ""StorageAccountEndpoint"": ""{configuration.StorageAccountEndpoint.AbsoluteUri}"",
  ""ManagedIdentityId"": ""{configuration.ManagedIdentityId}""
}}";
        }

        private static string GenerateBlobCacheConfig(ICacheConfigGenerationConfiguration configuration)
        {
            return @$"
{{
  ""RemoteIsReadOnly"": false,
  ""SkipDeterminismRecovery"": true,
  ""WriteThroughCasData"": true,
  ""FailIfRemoteFails"": true,
  ""RemoteConstructionTimeoutMilliseconds"": 30000,
  ""Assembly"": ""BuildXL.Cache.VerticalAggregator"",
  ""Type"": ""BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory"",
  ""RemoteCache"": {{
    ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
    ""CacheLogPath"": ""[BuildXLSelectedLogPath].Remote.log"",
    ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.BlobCacheFactory"",
    ""CacheId"": ""{configuration.CacheId()}Remote"",
    ""Universe"": ""{configuration.Universe()}"",
    ""RetentionPolicyInDays"": {configuration.RetentionPolicyInDays()},
    ""StorageAccountEndpoint"": ""{configuration.StorageAccountEndpoint.AbsoluteUri}"",
    ""ManagedIdentityId"": ""{configuration.ManagedIdentityId}""
  }},
  ""LocalCache"": {{
    ""MaxCacheSizeInMB"": {configuration.CacheSizeInMB()},
    ""Assembly"": ""BuildXL.Cache.MemoizationStoreAdapter"",
    ""UseStreamCAS"": false,
    ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory"",
    ""CacheLogPath"": ""[BuildXLSelectedLogPath]"",
    ""CacheRootPath"": ""[BuildXLSelectedRootPath]"",
    ""CacheId"": ""{configuration.CacheId()}Local"",
    ""ImplicitPin"": 0
  }}
}}";
        }
    }
}
