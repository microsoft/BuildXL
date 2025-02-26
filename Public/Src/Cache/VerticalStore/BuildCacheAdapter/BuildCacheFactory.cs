// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStoreAdapter;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.BuildCacheAdapter
{
    /// <summary>
    /// The Cache Factory for MemoizationStore based remote L3 Cache backed by blob store and BuildCache service.
    /// </summary>
    public class BuildCacheFactory : ICacheFactory
    {
        // BuildCacheFactory JSON CONFIG DATA
        // {
        //     "Assembly":"BuildXL.Cache.BuildCacheAdapter",
        //     "Type":"BuildXL.Cache.BuildCacheAdapter.BuildCacheFactory",
        //     "CacheId":"{0}",
        //     "CacheLogPath":"{1}",
        //     "CacheServiceFingerprintEndpoint":{2},
        //     "CacheServiceContentEndpoint":{3},
        //     "DaysToKeepUnreferencedContent":{4},
        //     "DaysToKeepContentBags":{5},
        //     "MaxFingerprintSelectorsToFetch":{6},
        //     "MaxContentBagsToFetch":{7},
        //     "IsCacheServiceReadOnly":{8},
        //     "CacheNamespace":{9},
        //     "CacheName":{10}
        //     "ScenarioName":{11}
        //     "ConnectionsPerSession":{12},
        //     "GrpcPort":{13},
        //     "ConnectionRetryIntervalSeconds":{14},
        //     "ConnectionRetryCount":{15},
        //     "SealUnbackedContentHashLists":{16},
        //     "MaxDegreeOfParallelismForIncorporateRequests":{17},
        //     "FingerprintIncorporationEnabled":{18},
        //     "UseBlobContentHashLists":{19},
        //     "UseAad":{20},
        //     "RangeOfDaysToKeepContentBags":{21},
        //     "MaxFingerprintsPerIncorporateRequest":{22},
        //     "HttpSendTimeoutMinutes":{23},
        //     "LogFlushIntervalSeconds":{24}
        //     "UseDedupStore":{26}
        //     "OverrideUnixFileAccessMode":{27}
        //     "DefaultPinInlineThresholdMinutes":{28}
        //     "DefaultIgnorePinThresholdHours":{29}
        // }
        private sealed class Config : BuildCacheCacheConfig
        {
            /// <summary>
            /// The Id of the cache instance
            /// </summary>
            [DefaultValue(typeof(CacheId))]
            public CacheId CacheId { get; set; }
        }

        /// <inheritdoc />
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId, IConfiguration configuration = null, BuildXLContext buildXLContext = null)
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
                var logger = new DisposeLogger(new EtwFileLog(logPath.Path, cacheConfig.CacheId), cacheConfig.LogFlushIntervalSeconds);

                var vstsCache = BuildCacheUtils.CreateBuildCacheCache(cacheConfig, logger, Environment.GetEnvironmentVariable("VSTSPERSONALACCESSTOKEN"), activityId);
                
                var statsFilePath = new AbsolutePath(logPath.Path + ".stats");
                var cache = new MemoizationStoreAdapterCache(
                    cacheConfig.CacheId,
                    vstsCache,
                    logger,
                    statsFilePath,
                    // The build cache service is never read-only
                    isReadOnly: false);

                logger.Diagnostic($"Initializing the cache [{cacheConfig.CacheId}]");
                var startupResult = await cache.StartupAsync();
                if (!startupResult.Succeeded)
                {
                    logger.Error($"Error while initializing the cache [{cacheConfig.CacheId}]. Failure: {startupResult.Failure}");
                    cache.Dispose();

                    return startupResult.Failure;
                }

                return cache;
            }
            catch (Exception e)
            {
                return new CacheConstructionFailure(cacheConfig.CacheId, e);
            }
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, cacheConfig =>
            {
                var failures = new List<Failure>();
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheLogPath, nameof(cacheConfig.CacheLogPath));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheId, nameof(cacheConfig.CacheId));

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
