// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.ToolSupport;
using Newtonsoft.Json;

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

            var ephemeralCacheConfigDict = new Dictionary<string, object>
            {
                { "Type", "BuildXL.Cache.MemoizationStoreAdapter.EphemeralCacheFactory" },
                { "Assembly", "BuildXL.Cache.MemoizationStoreAdapter" },
                { "CacheId", configuration.CacheId() },
                { "CacheLogPath", "[BuildXLSelectedLogPath].Ephemeral.log" },
                { "CacheRootPath", "[BuildXLSelectedRootPath]"},
                { "LeaderMachineName", "[BuildXLSelectedLeader]" },
                { "DatacenterWide",  (configuration.CacheType() == CacheType.EphemeralDatacenterWide ? "true" : "false")},
                { "CacheSizeMb", configuration.CacheSizeInMB() }
            };

            if (!configuration.HonorBuildCacheConfigurationFile())
            {
                ephemeralCacheConfigDict.Add("Universe", configuration.Universe());
                ephemeralCacheConfigDict.Add("RetentionPolicyInDays", configuration.RetentionPolicyInDays());
                ephemeralCacheConfigDict.Add("StorageAccountEndpoint", configuration.StorageAccountEndpoint.AbsoluteUri);
                ephemeralCacheConfigDict.Add("ManagedIdentityId", configuration.ManagedIdentityId);
            }
            else
            {
                ephemeralCacheConfigDict.Add("HostedPoolBuildCacheConfigurationFile", configuration.HostedPoolBuildCacheConfigurationFile);
                if (!string.IsNullOrEmpty(configuration.HostedPoolActiveBuildCacheName))
                {
                    ephemeralCacheConfigDict.Add("HostedPoolActiveBuildCacheName", configuration.HostedPoolActiveBuildCacheName());
                }
                // For now the 1ESHP build cache file is not encrypted
                ephemeralCacheConfigDict.Add("ConnectionStringFileDataProtectionEncrypted", "false");
            }

            return JsonConvert.SerializeObject(ephemeralCacheConfigDict, Formatting.Indented);
        }

        private static string GenerateBlobCacheConfig(ICacheConfigGenerationConfiguration configuration)
        {
            var configDict = new Dictionary<string, object>
            {
                { "RemoteIsReadOnly", false },
                { "SkipDeterminismRecovery", true },
                { "WriteThroughCasData", true },
                { "FailIfRemoteFails", true },
                { "RemoteConstructionTimeoutMilliseconds", 30000 },
                { "Assembly", "BuildXL.Cache.VerticalAggregator" },
                { "Type", "BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory" },
            };

            var remoteCacheDict = new Dictionary<string, object> {
                { "Assembly", "BuildXL.Cache.MemoizationStoreAdapter" },
                { "CacheLogPath", "[BuildXLSelectedLogPath].Remote.log" },
                { "Type", "BuildXL.Cache.MemoizationStoreAdapter.BlobCacheFactory" },
                { "CacheId", $"{configuration.CacheId()}Remote" },
            };

            if (!configuration.HonorBuildCacheConfigurationFile())
            {
                remoteCacheDict.Add("Universe", configuration.Universe());
                remoteCacheDict.Add("RetentionPolicyInDays", configuration.RetentionPolicyInDays());
                remoteCacheDict.Add("StorageAccountEndpoint", configuration.StorageAccountEndpoint.AbsoluteUri);
                remoteCacheDict.Add("ManagedIdentityId", configuration.ManagedIdentityId);
            }
            else
            {
                remoteCacheDict.Add("HostedPoolBuildCacheConfigurationFile", configuration.HostedPoolBuildCacheConfigurationFile);
                if (!string.IsNullOrEmpty(configuration.HostedPoolActiveBuildCacheName))
                {
                    remoteCacheDict.Add("HostedPoolActiveBuildCacheName", configuration.HostedPoolActiveBuildCacheName());
                }
                // For now the 1ESHP build cache file is not encrypted
                remoteCacheDict.Add("ConnectionStringFileDataProtectionEncrypted", "false");
            }

            var localCacheDict = new Dictionary<string, object> {
                { "MaxCacheSizeInMB", configuration.CacheSizeInMB() },
                { "Assembly", "BuildXL.Cache.MemoizationStoreAdapter" },
                { "UseStreamCAS", false },
                { "Type", "BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory" },
                { "CacheLogPath", "[BuildXLSelectedLogPath]" },
                { "CacheRootPath", "[BuildXLSelectedRootPath]" },
                { "CacheId", $"{configuration.CacheId()}Local" },
                { "ImplicitPin", 0 }
            };

            configDict["RemoteCache"] = remoteCacheDict;
            configDict["LocalCache"] = localCacheDict;
            return JsonConvert.SerializeObject(configDict, Formatting.Indented);
        }
    }
}