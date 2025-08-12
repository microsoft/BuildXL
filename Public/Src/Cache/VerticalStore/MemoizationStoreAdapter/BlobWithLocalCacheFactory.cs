// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
            // Initialize remote cache
            var remoteCacheConfig = blobWithLocalCacheConfig.RemoteCache;

            MemoizationStore.Interfaces.Caches.ICache remoteCache;
            ILogger combinedLogger = null;
            ContentStore.Interfaces.FileSystem.AbsolutePath logPath;

            try
            {
                // This call will also set up Kusto logging if configured
                (combinedLogger, logPath) = await BlobCacheFactory.CreateLoggerAsync(remoteCacheConfig);
                remoteCache = await new BlobCacheFactory().CreateInnerCacheAsync(combinedLogger, remoteCacheConfig);
            }
            catch (Exception e)
            {
                combinedLogger?.Dispose();
                return new CacheConstructionFailure(remoteCacheConfig.CacheId, e);
            }

            // Initialize local cache
            var localCacheConfig = blobWithLocalCacheConfig.LocalCache;

            MemoizationStore.Interfaces.Caches.ICache localCache;

            try
            {
                // Let's use a single logger for both local and remote, so we follow the same user experience as the ephemeral cache, where local and remote logs go to the same file
                localCache = new MemoizationStoreCacheFactory().CreateInnerCache(combinedLogger, localCacheConfig);
            }
            catch (Exception e)
            {
                return new CacheConstructionFailure(localCacheConfig.CacheId, e);
            }

            var twoLevelCacheConfig = new TwoLevelCacheConfiguration
            {
                RemoteCacheIsReadOnly = remoteCacheConfig.IsReadOnly,
                AlwaysUpdateFromRemote = true,
                BatchRemotePinsOnPut = false,
                SkipRemotePutIfAlreadyExistsInLocal = false,
                SkipRemotePinOnPut = true,
            };

            var innerCache = new TwoLevelCache(localCache, remoteCache, twoLevelCacheConfig);

            var statsFilePath = new ContentStore.Interfaces.FileSystem.AbsolutePath(logPath.Path + ".stats");

            var cache = new MemoizationStoreAdapterCache(
                    new CacheId(localCacheConfig.CacheId, remoteCacheConfig.CacheId),
                    innerCache,
                    combinedLogger,
                    statsFilePath,
                    // Not really used at this level. Each local & remote cache carry their own config at this respect.
                    isReadOnly: false,
                    replaceExistingOnPlaceFile: false,
                    implicitPin: ContentStore.Interfaces.Stores.ImplicitPin.None);

            var startupResult = await cache.StartupAsync();
            if (!startupResult.Succeeded)
            {
                cache.Dispose();
                return startupResult.Failure;
            }

            BlobCacheAccessor.CacheLogger!.Value?.SetValue(combinedLogger);
            return cache;
        }

        /// <inheritdoc/>
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            // These validation callbacks are currently not working. Check comment on the base interface.
            return new Failure[] { };
        }
    }
}
