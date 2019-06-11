// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata;
using BuildXL.Cache.MemoizationStore.Distributed.Sessions;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Vsts;
using BuildXL.Cache.MemoizationStoreAdapter;
using BuildXL.Utilities;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

[module: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses",
    Scope = "type",
    Target = "BuildXL.Cache.BuildCacheAdapter.DistributedBuildCacheFactory+Config",
    Justification = "Tool is confused - it is constructed generically")]
namespace BuildXL.Cache.BuildCacheAdapter
{
    /// <summary>
    /// The Cache Factory for MemoizationStore based remote L3 Cache backed by blob store and BuildCache service.
    /// </summary>
    public class DistributedBuildCacheFactory : ICacheFactory
    {
        private const string DefaultMetadataKeyspace = "Default:";

        // BuildCacheFactory JSON CONFIG DATA
        // {
        //     "Assembly":"BuildXL.Cache.BuildCacheAdapter",
        //     "Type":"BuildXL.Cache.BuildCacheAdapter.DistributedBuildCacheFactory",
        //     "CacheId":"{0}",
        //     "CacheLogPath":"{1}",
        //     "CacheServiceFingerprintEndpoint":{2},
        //     "CacheServiceContentEndpoint":{3},
        //     "DaysToKeepUnreferencedContent": {4},
        //     "DaysToKeepContentBags": {5},
        //     "MaxFingerprintSelectorsToFetch":{6},
        //     "MaxContentBagsToFetch":{7},
        //     "IsCacheServiceReadOnly": {8},
        //     "CacheNamespace": {9},
        //     "CacheKeyBumpTime": {10},
        //     "CacheName":{11}
        //     "ScenarioName":{12}
        //     "ConnectionsPerSession":{13},
        //     "GrpcPort":{14},
        //     "ConnectionRetryIntervalSeconds":{15},
        //     "ConnectionRetryCount":{16},
        //     "SealUnbackedContentHashLists":{17},
        //     "MaxDegreeOfParallelismForIncorporateRequests":{18},
        //     "FingerprintIncorporationEnabled":{19},
        //     "UseBlobContentHashLists":{20},
        //     "UseAad":{21},
        //     "RangeOfDaysToKeepContentBags":{22},
        //     "MaxFingerprintsPerIncorporateRequest":{23},
        //     "HttpSendTimeoutMinutes":{24},
        //     "LogFlushIntervalSeconds":{25}
        //     "DownloadBlobsThroughBlobStore":{26}
        //     "UseDedupStore":{27}
        //     "DisableContent":{28}
        //     "OverrideUnixFileAccessMode":{29}
        // }
        private sealed class Config : BuildCacheCacheConfig
        {
            public const int DefaultCacheKeyBumpTimeMins = 15;

            /// <summary>
            /// The Id of the cache instance
            /// </summary>
            [DefaultValue("DistributedBuildCache")]
            public string CacheId { get; set; }

            /// <summary>
            /// Cache key expiry bump time in minutes
            /// </summary>
            [DefaultValue(DefaultCacheKeyBumpTimeMins)]
            public int CacheKeyBumpTimeMins { get; set; }

            /// <summary>
            /// Disable content operations. Will only do content operations that are triggered by the memoization session.
            /// </summary>
            [DefaultValue(false)]
            public bool DisableContent { get; set; }
        }

        /// <inheritdoc />
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId)
        {
            Contract.Requires(cacheData != null);

            var possibleCacheConfig = cacheData.Create<Config>();
            if (!possibleCacheConfig.Succeeded)
            {
                return possibleCacheConfig.Failure;
            }

            Config cacheConfig = possibleCacheConfig.Result;

            try
            {
                var logPath = new AbsolutePath(cacheConfig.CacheLogPath);
                var logger = new DisposeLogger(() => new EtwFileLog(logPath.Path, cacheConfig.CacheId), cacheConfig.LogFlushIntervalSeconds);

                var distributedCache = CreateDistributedCache(logger, cacheConfig);
                logger.Debug($"Distributed cache created successfully.");

                var statsFilePath = new AbsolutePath(logPath.Path + ".stats");
                var cache = new MemoizationStoreAdapterCache(cacheConfig.CacheId, distributedCache, logger, statsFilePath);

                logger.Diagnostic($"Initializing the cache [{cacheConfig.CacheId}]");
                var startupResult = await cache.StartupAsync();
                if (!startupResult.Succeeded)
                {
                    logger.Error($"Error while initializing the cache [{cacheConfig.CacheId}]. Failure: {startupResult.Failure}");
                    return startupResult.Failure;
                }

                return cache;
            }
            catch (Exception e)
            {
                return new CacheConstructionFailure(cacheConfig.CacheId, e);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private static BuildXL.Cache.MemoizationStore.Interfaces.Caches.ICache CreateDistributedCache(ILogger logger, Config cacheConfig)
        {
            int cacheKeyBumpTimeMins = cacheConfig.CacheKeyBumpTimeMins;
            if (cacheKeyBumpTimeMins <= 0)
            {
                logger.Debug("Config specified bump time in minutes is invalid {0}. Using default bump time {1}", cacheKeyBumpTimeMins, Config.DefaultCacheKeyBumpTimeMins);
                cacheKeyBumpTimeMins = Config.DefaultCacheKeyBumpTimeMins;
            }

            TimeSpan keyBump = TimeSpan.FromMinutes(cacheKeyBumpTimeMins);

            var metadataTracer = new DistributedCacheSessionTracer(logger, nameof(DistributedCache));

            string metadataKeyspace = cacheConfig.CacheNamespace;
            if (string.IsNullOrWhiteSpace(metadataKeyspace))
            {
                metadataKeyspace = DefaultMetadataKeyspace;
            }

            IMetadataCache metadataCache = RedisMetadataCacheFactory.Create(metadataTracer, keySpace: metadataKeyspace, cacheKeyBumpTime: keyBump);

            var innerCache = cacheConfig.DisableContent
                ?  new OneLevelCache(
                    contentStoreFunc: () => new ReadOnlyEmptyContentStore(),
                    memoizationStoreFunc: () => (BuildCacheCache) BuildCacheUtils.CreateBuildCacheCache(cacheConfig, logger, Environment.GetEnvironmentVariable("VSTSPERSONALACCESSTOKEN")),
                    id: Guid.NewGuid(),
                    passContentToMemoization: false)
                : BuildCacheUtils.CreateBuildCacheCache(cacheConfig, logger, Environment.GetEnvironmentVariable("VSTSPERSONALACCESSTOKEN"));

            ReadThroughMode readThroughMode = cacheConfig.SealUnbackedContentHashLists ? ReadThroughMode.ReadThrough : ReadThroughMode.None;
            return new DistributedCache(logger, innerCache, metadataCache, metadataTracer, readThroughMode);
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, cacheConfig =>
            {
                var failures = new List<Failure>();
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheLogPath, nameof(cacheConfig.CacheLogPath));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheId, nameof(cacheConfig.CacheId));

                if (cacheConfig.CacheKeyBumpTimeMins <= 0)
                {
                    failures.Add(new IncorrectJsonConfigDataFailure($"{nameof(cacheConfig.CacheKeyBumpTimeMins)} must be greater than 0"));
                }

                if (!Uri.IsWellFormedUriString(cacheConfig.CacheServiceContentEndpoint, UriKind.Absolute))
                {
                    failures.Add(new IncorrectJsonConfigDataFailure($"{nameof(cacheConfig.CacheServiceContentEndpoint)}=[{cacheConfig.CacheServiceContentEndpoint}] is not a valid Uri."));
                }

                if (!Uri.IsWellFormedUriString(cacheConfig.CacheServiceFingerprintEndpoint, UriKind.Absolute))
                {
                    failures.Add(new IncorrectJsonConfigDataFailure($"{nameof(cacheConfig.CacheServiceFingerprintEndpoint)}=[{cacheConfig.CacheServiceFingerprintEndpoint}] is not a valid Uri."));
                }

                return failures;
            });
        }
    }
}
