// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Vsts.Http;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using BuildXL.Utilities.Authentication;

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

            var domain = new ByteDomainId(cacheConfig.DomainId);
            return new BuildCacheCache(
                new BackingContentStoreConfiguration()
                {
                    FileSystem = fileSystem,
                    ArtifactHttpClientFactory = new BackingContentStoreHttpClientFactory(new Uri(cacheConfig.CacheServiceContentEndpoint), vssCredentialsFactory, TimeSpan.FromMinutes(cacheConfig.HttpSendTimeoutMinutes), domain, cacheConfig.UseAad),
                    TimeToKeepContent = TimeSpan.FromDays(cacheConfig.DaysToKeepUnreferencedContent),
                    PinInlineThreshold = TimeSpan.FromMinutes(cacheConfig.PinInlineThresholdMinutes),
                    IgnorePinThreshold = TimeSpan.FromHours(cacheConfig.IgnorePinThresholdHours),
                    UseDedupStore = cacheConfig.UseDedupStore,
                    DownloadBlobsUsingHttpClient = cacheConfig.DownloadBlobsUsingHttpClient,
                    RequiredContentKeepUntil = cacheConfig.RequiredContentKeepUntilHours >= 0 ? TimeSpan.FromHours(cacheConfig.RequiredContentKeepUntilHours) : null
                },
                cacheConfig.CacheNamespace,
                new BuildCacheHttpClientFactory(new Uri(cacheConfig.CacheServiceFingerprintEndpoint), vssCredentialsFactory, TimeSpan.FromMinutes(cacheConfig.HttpSendTimeoutMinutes), cacheConfig.UseAad),
                cacheConfig.MaxFingerprintSelectorsToFetch,
                TimeSpan.FromDays(cacheConfig.DaysToKeepContentBags),
                TimeSpan.FromDays(cacheConfig.RangeOfDaysToKeepContentBags),
                logger,
                cacheConfig.FingerprintIncorporationEnabled,
                cacheConfig.MaxDegreeOfParallelismForIncorporateRequests,
                cacheConfig.MaxFingerprintsPerIncorporateRequest,
                domain,
                cacheConfig.ForceUpdateOnAddContentHashList,
                writeThroughContentStoreFunc,
                cacheConfig.SealUnbackedContentHashLists,
                cacheConfig.UseBlobContentHashLists,
                cacheConfig.OverrideUnixFileAccessMode,
                cacheConfig.EnableEagerFingerprintIncorporation,
                TimeSpan.FromHours(cacheConfig.InlineFingerprintIncorporationExpiryHours),
                TimeSpan.FromMinutes(cacheConfig.EagerFingerprintIncorporationNagleIntervalMinutes),
                cacheConfig.EagerFingerprintIncorporationNagleBatchSize);
        }
    }
}
