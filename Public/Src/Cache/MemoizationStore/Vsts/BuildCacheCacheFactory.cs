// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Vsts.Http;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    ///     Factory for BuildCacheCache with helpers for handling auth
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static class BuildCacheCacheFactory
    {
        /// <summary>
        /// Creates an ICache that can communicate with a VSTS Build Cache Service.
        /// </summary>
        public static ICache Create(
            IAbsFileSystem fileSystem,
            ILogger logger,
            VssCredentialsFactory vssCredentialsFactory,
            BuildCacheServiceConfiguration cacheConfig,
            Func<IContentStore> writeThroughContentStoreFunc)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(cacheConfig != null);
            return new BuildCacheCache(
                fileSystem,
                cacheConfig.CacheNamespace,
                new BuildCacheHttpClientFactory(new Uri(cacheConfig.CacheServiceFingerprintEndpoint), vssCredentialsFactory, TimeSpan.FromMinutes(cacheConfig.HttpSendTimeoutMinutes), cacheConfig.UseAad),
                new BackingContentStoreHttpClientFactory(new Uri(cacheConfig.CacheServiceContentEndpoint), vssCredentialsFactory, TimeSpan.FromMinutes(cacheConfig.HttpSendTimeoutMinutes), cacheConfig.UseAad),
                cacheConfig.MaxFingerprintSelectorsToFetch,
                TimeSpan.FromDays(cacheConfig.DaysToKeepUnreferencedContent),
                TimeSpan.FromDays(cacheConfig.DaysToKeepContentBags),
                TimeSpan.FromDays(cacheConfig.RangeOfDaysToKeepContentBags),
                logger,
                cacheConfig.FingerprintIncorporationEnabled,
                cacheConfig.MaxDegreeOfParallelismForIncorporateRequests,
                cacheConfig.MaxFingerprintsPerIncorporateRequest,
                writeThroughContentStoreFunc,
                cacheConfig.SealUnbackedContentHashLists,
                cacheConfig.UseBlobContentHashLists,
                cacheConfig.DownloadBlobsThroughBlobStore,
                cacheConfig.UseDedupStore,
                cacheConfig.OverrideUnixFileAccessMode);
        }
    }
}
