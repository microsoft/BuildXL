// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Tracing;
using BuildXL.Cache.MemoizationStore.Vsts.Adapters;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    ///     ICacheSession for BuildCacheCache.
    /// </summary>
    public class BuildCacheSession : BuildCacheReadOnlySession, ICacheSession
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BuildCacheSession"/> class.
        /// </summary>
        /// <param name="fileSystem">Filesystem used to read/write files.</param>
        /// <param name="name">Session name.</param>
        /// <param name="implicitPin">Policy determining whether or not content should be automatically pinned on adds or gets.</param>
        /// <param name="cacheNamespace">The namespace of the cache being communicated with.</param>
        /// <param name="cacheId">The id of the cache being communicated with.</param>
        /// <param name="contentHashListAdapter">Backing BuildCache http client.</param>
        /// <param name="backingContentSession">Backing BlobStore content session.</param>
        /// <param name="maxFingerprintSelectorsToFetch">Maximum number of selectors to enumerate for a GetSelectors call.</param>
        /// <param name="minimumTimeToKeepContentHashLists">Minimum time-to-live for created or referenced ContentHashLists.</param>
        /// <param name="rangeOfTimeToKeepContentHashLists">Range of time beyond the minimum for the time-to-live of created or referenced ContentHashLists.</param>
        /// <param name="fingerprintIncorporationEnabled">Feature flag to enable fingerprints incorporation</param>
        /// <param name="maxDegreeOfParallelismForIncorporateRequests">Throttle the number of fingerprints chunks sent in parallel</param>
        /// <param name="maxFingerprintsPerIncorporateRequest">Max fingerprints allowed per chunk</param>
        /// <param name="writeThroughContentSession">Optional write-through session to allow writing-behind to BlobStore</param>
        /// <param name="sealUnbackedContentHashLists">If true, the client will attempt to seal any unbacked ContentHashLists that it sees.</param>
        /// <param name="overrideUnixFileAccessMode">If true, overrides default Unix file access modes when placing files.</param>
        /// <param name="tracer">A tracer for logging calls</param>
        public BuildCacheSession(
            IAbsFileSystem fileSystem,
            string name,
            ImplicitPin implicitPin,
            string cacheNamespace,
            Guid cacheId,
            IContentHashListAdapter contentHashListAdapter,
            IContentSession backingContentSession,
            int maxFingerprintSelectorsToFetch,
            TimeSpan minimumTimeToKeepContentHashLists,
            TimeSpan rangeOfTimeToKeepContentHashLists,
            bool fingerprintIncorporationEnabled,
            int maxDegreeOfParallelismForIncorporateRequests,
            int maxFingerprintsPerIncorporateRequest,
            IContentSession writeThroughContentSession,
            bool sealUnbackedContentHashLists,
            bool overrideUnixFileAccessMode,
            BuildCacheCacheTracer tracer)
            : base(
                fileSystem,
                name,
                implicitPin,
                cacheNamespace,
                cacheId,
                contentHashListAdapter,
                backingContentSession,
                maxFingerprintSelectorsToFetch,
                minimumTimeToKeepContentHashLists,
                rangeOfTimeToKeepContentHashLists,
                fingerprintIncorporationEnabled,
                maxDegreeOfParallelismForIncorporateRequests,
                maxFingerprintsPerIncorporateRequest,
                writeThroughContentSession,
                sealUnbackedContentHashLists,
                overrideUnixFileAccessMode,
                tracer)
        {
        }

        /// <inheritdoc />
        public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return AddOrGetContentHashListCall.RunAsync(Tracer.MemoizationStoreTracer, new OperationContext(context, cts), strongFingerprint, async () =>
                {
                    // TODO: Split this out into separate implementations for WriteThrough vs. WriteBehind (bug 1365340)
                    if (WriteThroughContentSession == null)
                    {
                        // Guaranteed content is currently only available for BlobSessions. (bug 144396)
                        ContentAvailabilityGuarantee guarantee =
                            BackingContentSession is BlobContentSession
                                ? ContentAvailabilityGuarantee.AllContentBackedByCache
                                : ContentAvailabilityGuarantee.NoContentBackedByCache;

                        return await AddOrGetContentHashListAsync(
                            context,
                            strongFingerprint,
                            contentHashListWithDeterminism,
                            guarantee).ConfigureAwait(false);
                    }

                    // Ensure that the content exists somewhere before trying to add
                    if (!await EnsureContentIsAvailableAsync(
                        context, contentHashListWithDeterminism.ContentHashList.Hashes, cts, urgencyHint).ConfigureAwait(false))
                    {
                        return new AddOrGetContentHashListResult(
                            "Referenced content must exist in the cache before a new content hash list is added.");
                    }

                    DateTime expirationUtc = FingerprintTracker.GenerateNewExpiration();
                    var valueToAdd = new ContentHashListWithCacheMetadata(
                        contentHashListWithDeterminism,
                        expirationUtc,
                        ContentAvailabilityGuarantee.NoContentBackedByCache);

                    DateTime? rawExpiration = null;
                    const int addLimit = 3;
                    for (int addAttempts = 0; addAttempts < addLimit; addAttempts++)
                    {
                        var debugString = $"Adding contentHashList=[{valueToAdd.ContentHashListWithDeterminism.ContentHashList}] " +
                                            $"determinism=[{valueToAdd.ContentHashListWithDeterminism.Determinism}] to VSTS with " +
                                            $"contentAvailabilityGuarantee=[{valueToAdd.ContentGuarantee}] and expirationUtc=[{expirationUtc}]";
                        Tracer.Debug(context, debugString);
                        ObjectResult<ContentHashListWithCacheMetadata> responseObject =
                            await ContentHashListAdapter.AddContentHashListAsync(
                                context,
                                CacheNamespace,
                                strongFingerprint,
                                valueToAdd).ConfigureAwait(false);

                        if (!responseObject.Succeeded)
                        {
                            return new AddOrGetContentHashListResult(responseObject);
                        }

                        ContentHashListWithCacheMetadata response = responseObject.Data;
                        var inconsistencyErrorMessage = CheckForResponseInconsistency(response);
                        if (inconsistencyErrorMessage != null)
                        {
                            return new AddOrGetContentHashListResult(inconsistencyErrorMessage);
                        }

                        rawExpiration = response.GetRawExpirationTimeUtc();

                        ContentHashList contentHashListToReturn =
                            UnpackContentHashListAfterAdd(contentHashListWithDeterminism.ContentHashList, response);
                        CacheDeterminism determinismToReturn = UnpackDeterminism(response, CacheId);

                        bool needToUpdateExistingValue = await CheckNeedToUpdateExistingValueAsync(
                            context,
                            response,
                            contentHashListToReturn,
                            cts,
                            urgencyHint).ConfigureAwait(false);
                        if (!needToUpdateExistingValue)
                        {
                            SealIfNecessaryAfterUnbackedAddOrGet(context, strongFingerprint, contentHashListWithDeterminism, response);
                            FingerprintTracker.Track(strongFingerprint, rawExpiration);
                            return new AddOrGetContentHashListResult(
                                new ContentHashListWithDeterminism(contentHashListToReturn, determinismToReturn));
                        }

                        var hashOfExistingContentHashList = response.HashOfExistingContentHashList;
                        Tracer.Debug(context, $"Attempting to replace unbacked value with hash {hashOfExistingContentHashList.ToHex()}");
                        valueToAdd = new ContentHashListWithCacheMetadata(
                            contentHashListWithDeterminism,
                            expirationUtc,
                            ContentAvailabilityGuarantee.NoContentBackedByCache,
                            hashOfExistingContentHashList
                        );
                    }

                    Tracer.Warning(
                        context,
                        $"Lost the AddOrUpdate race {addLimit} times against unbacked values. Returning as though the add succeeded for now.");
                    FingerprintTracker.Track(strongFingerprint, rawExpiration);
                    return new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None));
                });
        }

        private async Task<bool> CheckNeedToUpdateExistingValueAsync(
            Context context,
            ContentHashListWithCacheMetadata cacheMetadata,
            ContentHashList contentHashListToReturn,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return cacheMetadata != null &&
                   cacheMetadata.GetEffectiveExpirationTimeUtc() == null &&
                   contentHashListToReturn != null &&
                   (!await EnsureContentIsAvailableAsync(context, contentHashListToReturn.Hashes, cts, urgencyHint).ConfigureAwait(false));
        }

        private void SealIfNecessaryAfterUnbackedAddOrGet(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism addedValue,
            ContentHashListWithCacheMetadata cacheMetadata)
        {
            if (cacheMetadata == null)
            {
                // Our unbacked Add won the race, so the value is implicitly unbacked in VSTS
                // Seal the added value
                SealInTheBackground(context, strongFingerprint, addedValue);
            }
            else if (cacheMetadata.GetEffectiveExpirationTimeUtc() == null)
            {
                // Value is explicitly unbacked in VSTS
                if (cacheMetadata.ContentHashListWithDeterminism.ContentHashList != null)
                {
                    // Our Add lost the race, so seal the existing value
                    SealInTheBackground(context, strongFingerprint, cacheMetadata.ContentHashListWithDeterminism);
                }
                else
                {
                    // Our Add won the race, so seal the added value
                    var valueToSeal = new ContentHashListWithDeterminism(
                        addedValue.ContentHashList, cacheMetadata.ContentHashListWithDeterminism.Determinism);
                    SealInTheBackground(
                        context,
                        strongFingerprint,
                        valueToSeal);
                }
            }
        }

        /// <inheritdoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(
            Context context,
            IEnumerable<Task<StrongFingerprint>> strongFingerprints,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return IncorporateStrongFingerprintsCall.RunAsync(Tracer.MemoizationStoreTracer, context, async () =>
            {
                // BuildCache remembers fingerprints for all content that has been fetched or added through it's APIs.
                // However, the build may end up satisfying some fingerprints without using BuildCache. The build or
                // other cache instances in the cache topology can track these "other" fingerprints and ask that the
                // fingerprints be included in the cache session using the incoporate API. BuildCache will extend the
                // expiration of the fingerprints and mapped content, as if they had just been published.
                foreach (var strongFingerprintTask in strongFingerprints)
                {
                    var strongFingerprint = await strongFingerprintTask.ConfigureAwait(false);

                    // The Incorporate API currently does allow passing the expiration, so we can't pass it here.
                    FingerprintTracker.Track(strongFingerprint);
                }

                return BoolResult.Success;
            });
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return PutFileCall<ContentSessionTracer>.RunAsync(Tracer.ContentSessionTracer, new OperationContext(context), path, realizationMode, hashType, trustedHash: false, () =>
                    WriteThroughContentSession != null
                    ? WriteThroughContentSession.PutFileAsync(context, hashType, path, realizationMode, cts, urgencyHint)
                    : BackingContentSession.PutFileAsync(context, hashType, path, realizationMode, cts, urgencyHint));
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return PutFileCall<ContentSessionTracer>.RunAsync(Tracer.ContentSessionTracer, new OperationContext(context), path, realizationMode, contentHash, trustedHash: false, () =>
                    WriteThroughContentSession != null
                    ? WriteThroughContentSession.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint)
                    : BackingContentSession.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint));
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context,
            HashType hashType,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return PutStreamCall<ContentSessionTracer>.RunAsync(Tracer.ContentSessionTracer, new OperationContext(context), hashType, () =>
                WriteThroughContentSession != null
                ? WriteThroughContentSession.PutStreamAsync(context, hashType, stream, cts, urgencyHint)
                : BackingContentSession.PutStreamAsync(context, hashType, stream, cts, urgencyHint));
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context,
            ContentHash contentHash,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return PutStreamCall<ContentSessionTracer>.RunAsync(Tracer.ContentSessionTracer, new OperationContext(context), contentHash, () =>
                WriteThroughContentSession != null
                ? WriteThroughContentSession.PutStreamAsync(context, contentHash, stream, cts, urgencyHint)
                : BackingContentSession.PutStreamAsync(context, contentHash, stream, cts, urgencyHint));
        }

        // ReSharper disable once RedundantOverridenMember

        /// <summary>
        /// Dispose native resources.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        private async Task<bool> EnsureContentIsAvailableAsync(
            Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint)
        {
            bool missingContent = false;

            var tasks = await PinAsync(context, contentHashes, cts, urgencyHint);

            foreach (var task in tasks)
            {
                var pinResult = (await task).Item;
                missingContent |= !pinResult.Succeeded;
            }

            return !missingContent;
        }
    }
}
