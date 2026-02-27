// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// A cache factory that uses <see cref="TwoLevelCache"/> to implement a composite cache with a local cache (via <see cref="MemoizationStoreCacheFactory"/>
    /// and a remote blob-based cache (via <see cref="BlobCacheFactory"/>)
    /// </summary>
    public class BlobWithLocalCacheFactory : ICacheFactory
    {
        /// <inheritdoc/>
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId = default, IConfiguration configuration = null, BuildXLContext buildXLContext = null)
        {
            Contract.Requires(cacheData != null);

            var possibleCacheConfig = cacheData.Create<BlobWithLocalCacheConfig>(activityId, configuration, buildXLContext);
            if (!possibleCacheConfig.Succeeded)
            {
                return possibleCacheConfig.Failure;
            }

            return await InitializeCacheAsync(possibleCacheConfig.Result);
        }

        private static async Task<Possible<ICache, Failure>> InitializeCacheAsync(BlobWithLocalCacheConfig blobWithLocalCacheConfig)
        {
            var remoteCacheConfig = blobWithLocalCacheConfig.RemoteCache;
            var localCacheConfig = blobWithLocalCacheConfig.LocalCache;

            MemoizationStore.Interfaces.Caches.ICache remoteCache = null;
            ILogger combinedLogger = null;
            ContentStore.Interfaces.FileSystem.AbsolutePath logPath = null;
            Failure remoteFailure = null;

            // Try to initialize the remote cache
            try
            {
                // This call will also set up Kusto logging if configured
                (combinedLogger, logPath) = await BlobCacheFactory.CreateLoggerAsync(remoteCacheConfig);
                remoteCache = await new BlobCacheFactory().CreateInnerCacheAsync(combinedLogger, remoteCacheConfig);
            }
            catch (Exception e)
            {
                remoteFailure = new CacheConstructionFailure(remoteCacheConfig.CacheId, e);

                if (blobWithLocalCacheConfig.FailIfRemoteFails)
                {
                    combinedLogger?.Dispose();
                    return remoteFailure;
                }

                // If the logger wasn't created (failed during logger creation), create a basic one from local config
                if (combinedLogger == null)
                {
                    logPath = new ContentStore.Interfaces.FileSystem.AbsolutePath(localCacheConfig.CacheLogPath);
                    combinedLogger = new DisposeLogger(new EtwFileLog(logPath.Path, localCacheConfig.CacheId), localCacheConfig.LogFlushIntervalSeconds);
                }
            }

            // Initialize local cache
            MemoizationStore.Interfaces.Caches.ICache localCache;

            try
            {
                // Let's use a single logger for both local and remote, so we follow the same user experience as the ephemeral cache, where local and remote logs go to the same file
                localCache = new MemoizationStoreCacheFactory().CreateInnerCache(combinedLogger, localCacheConfig);
            }
            catch (Exception e)
            {
                combinedLogger?.Dispose();
                return new CacheConstructionFailure(localCacheConfig.CacheId, e);
            }

            MemoizationStore.Interfaces.Caches.ICache innerCache;
            CacheId cacheId;
            var stateDegradationFailures = new List<Failure>();

            if (remoteCache != null)
            {
                // Both remote and local succeeded — create the two-level cache
                var twoLevelCacheConfig = new TwoLevelCacheConfiguration
                {
                    RemoteCacheIsReadOnly = remoteCacheConfig.IsReadOnly,
                    AlwaysUpdateFromRemote = true,
                    BatchRemotePinsOnPut = false,
                    SkipRemotePutIfAlreadyExistsInLocal = false,
                    SkipRemotePinOnPut = true,
                };

                innerCache = new TwoLevelCache(localCache, remoteCache, twoLevelCacheConfig);
                cacheId = new CacheId(localCacheConfig.CacheId, remoteCacheConfig.CacheId);
            }
            else
            {
                // Remote failed — fall back to local only
                innerCache = localCache;
                cacheId = localCacheConfig.CacheId;

                string warningMessage = BuildRemoteCacheFailureWarning(remoteCacheConfig, remoteFailure);
                stateDegradationFailures.Add(new RemoteCacheFallbackFailure(warningMessage));
                combinedLogger.Log(Severity.Warning, warningMessage);
            }

            var statsFilePath = new ContentStore.Interfaces.FileSystem.AbsolutePath(logPath.Path + ".stats");

            var cache = new MemoizationStoreAdapterCache(
                    cacheId,
                    innerCache,
                    combinedLogger,
                    statsFilePath,
                    // Not really used at this level. Each local & remote cache carry their own config at this respect.
                    isReadOnly: false,
                    replaceExistingOnPlaceFile: false,
                    implicitPin: ContentStore.Interfaces.Stores.ImplicitPin.None,
                    precedingStateDegradationFailures: stateDegradationFailures);

            var startupResult = await cache.StartupAsync();
            if (!startupResult.Succeeded)
            {
                cache.Dispose();
                return startupResult.Failure;
            }

            BlobCacheAccessor.CacheLogger!.Value?.SetValue(combinedLogger);
            return cache;
        }

        /// <summary>
        /// Builds a warning message for remote cache construction failure, including an actionable URL when available.
        /// </summary>
        private static string BuildRemoteCacheFailureWarning(BlobCacheConfig remoteCacheConfig, Failure remoteFailure)
        {
            var message = remoteFailure.Describe();

            // Include an actionable URL when a developer build cache resource is configured
            if (!string.IsNullOrEmpty(remoteCacheConfig.DeveloperBuildCacheResourceId))
            {
                message += $" To manage cache access, visit: https://portal.azure.com/#@/resource{remoteCacheConfig.DeveloperBuildCacheResourceId}";
            }

            return message;
        }

        /// <inheritdoc/>
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            // These validation callbacks are currently not working. Check comment on the base interface.
            return new Failure[] { };
        }
    }
}
