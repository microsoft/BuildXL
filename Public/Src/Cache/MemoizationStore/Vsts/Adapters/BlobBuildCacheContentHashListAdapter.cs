// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using BuildXL.Cache.MemoizationStore.VstsInterfaces.Blob;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.WebApi;
using BlobIdentifier = BuildXL.Cache.ContentStore.Hashing.BlobIdentifier;

namespace BuildXL.Cache.MemoizationStore.Vsts.Adapters
{
    /// <summary>
    /// An adapter for the service for blob based contenthashlists.
    /// </summary>
    public class BlobBuildCacheContentHashListAdapter : IContentHashListAdapter
    {
        private static readonly ContentHashListWithCacheMetadata EmptyContentHashList = new ContentHashListWithCacheMetadata(
                   new ContentHashListWithDeterminism(null, CacheDeterminism.None),
                   null,
                   ContentAvailabilityGuarantee.NoContentBackedByCache);

        private readonly IBlobBuildCacheHttpClient _buildCacheHttpClient;
        private readonly IBackingContentSession _blobContentSession;
        private readonly bool _includeDownloadUris;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobBuildCacheContentHashListAdapter"/> class.
        /// </summary>
        public BlobBuildCacheContentHashListAdapter(IBlobBuildCacheHttpClient buildCacheHttpClient, IBackingContentSession blobContentSession, bool includeDownloadUris)
        {
            _buildCacheHttpClient = buildCacheHttpClient;
            _blobContentSession = blobContentSession;
            _includeDownloadUris = includeDownloadUris;
        }

        /// <inheritdoc />
        public async Task<Result<IEnumerable<SelectorAndContentHashListWithCacheMetadata>>> GetSelectorsAsync(
            Context context,
            string cacheNamespace,
            Fingerprint weakFingerprint,
            int maxSelectorsToFetch)
        {
            try
            {
                BlobSelectorsResponse selectorsResponse = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                    context,
                    "GetSelectors",
                    innerCts => _buildCacheHttpClient.GetSelectors(
                        cacheNamespace,
                        weakFingerprint,
                        maxSelectorsToFetch),
                    CancellationToken.None);
                var selectorsToReturn = new List<SelectorAndContentHashListWithCacheMetadata>();
                foreach (
                    BlobSelectorAndContentHashList selectorAndPossible in selectorsResponse.SelectorsAndPossibleContentHashLists)
                {
                    if (selectorAndPossible.ContentHashList != null)
                    {
                        if (selectorAndPossible.ContentHashList.Determinism.IsDeterministic)
                        {
                            BlobContentHashListCache.Instance.AddValue(
                                cacheNamespace,
                                new StrongFingerprint(weakFingerprint, selectorAndPossible.Selector),
                                selectorAndPossible.ContentHashList);
                        }

                        AddDownloadUriToCache(selectorAndPossible.ContentHashList.ContentHashListWithDeterminism);
                    }

                    selectorsToReturn.Add(
                        new SelectorAndContentHashListWithCacheMetadata(
                            selectorAndPossible.Selector,
                            null));
                }

                return new Result<IEnumerable<SelectorAndContentHashListWithCacheMetadata>>(selectorsToReturn);
            }
            catch (Exception ex)
            {
                return new Result<IEnumerable<SelectorAndContentHashListWithCacheMetadata>>(ex);
            }
        }

        /// <inheritdoc />
        public async Task<Result<ContentHashListWithCacheMetadata>> GetContentHashListAsync(
            Context context,
            string cacheNamespace,
            StrongFingerprint strongFingerprint)
        {
            try
            {
                if (!BlobContentHashListCache.Instance.TryGetValue(cacheNamespace, strongFingerprint, out var blobCacheMetadata))
                {
                    BlobContentHashListResponse blobResponse = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                            context,
                            "GetContentHashList",
                            innerCts => _buildCacheHttpClient.GetContentHashListAsync(cacheNamespace, strongFingerprint, _includeDownloadUris),
                            CancellationToken.None)
                        .ConfigureAwait(false);

                    _blobContentSession.UriCache.BulkAddDownloadUris(blobResponse.BlobDownloadUris);
                    blobCacheMetadata = blobResponse.ContentHashListWithCacheMetadata;
                    AddDownloadUriToCache(blobCacheMetadata.ContentHashListWithDeterminism);
                }

                // Currently expect the Blob-based service to return null on misses,
                // but the other catches have been left for safety/compat.
                if (blobCacheMetadata == null)
                {
                    return new Result<ContentHashListWithCacheMetadata>(EmptyContentHashList);
                }

                return await UnpackBlobContentHashListAsync(context, blobCacheMetadata);
            }
            catch (ContentBagNotFoundException)
            {
                return new Result<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (CacheServiceException ex) when (ex.ReasonCode == CacheErrorReasonCode.ContentHashListNotFound)
            {
                return new Result<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (VssServiceResponseException serviceEx) when (serviceEx.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return new Result<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (Exception ex)
            {
                return new Result<ContentHashListWithCacheMetadata>(ex);
            }
        }
        /// <inheritdoc />
        public async Task<Result<ContentHashListWithCacheMetadata>> AddContentHashListAsync(
            Context context,
            string cacheNamespace,
            StrongFingerprint strongFingerprint,
            ContentHashListWithCacheMetadata valueToAdd,
            bool forceUpdate)
        {
            try
            {
                Func<System.IO.Stream, System.Threading.CancellationToken, Task<Result<ContentHash>>> putStreamFunc =
                    async (stream, cts) =>
                    {
                        PutResult putResult = await _blobContentSession.PutStreamAsync(context, HashType.Vso0, stream, cts);
                        if (putResult.Succeeded)
                        {
                            return new Result<ContentHash>(putResult.ContentHash);
                        }

                        return new Result<ContentHash>(putResult);
                    };

                Result<ContentHash> blobIdOfContentHashListResult =
                    await BlobContentHashListExtensions.PackInBlob(
                        putStreamFunc,
                        valueToAdd.ContentHashListWithDeterminism);

                if (!blobIdOfContentHashListResult.Succeeded)
                {
                    return new Result<ContentHashListWithCacheMetadata>(blobIdOfContentHashListResult);
                }

                var blobContentHashListWithDeterminism =
                    new BlobContentHashListWithDeterminism(
                        valueToAdd.ContentHashListWithDeterminism.Determinism.EffectiveGuid,
                        BuildXL.Cache.ContentStore.Hashing.BlobIdentifierHelperExtensions.ToBlobIdentifier(blobIdOfContentHashListResult.Value));

                var blobContentHashListWithCacheMetadata = new BlobContentHashListWithCacheMetadata(
                    blobContentHashListWithDeterminism,
                    valueToAdd.GetRawExpirationTimeUtc(),
                    valueToAdd.ContentGuarantee,
                    valueToAdd.HashOfExistingContentHashList);

                BlobContentHashListResponse addResult = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                    context,
                    "AddContentHashList",
                    innerCts => _buildCacheHttpClient.AddContentHashListAsync(
                        cacheNamespace,
                        strongFingerprint,
                        blobContentHashListWithCacheMetadata,
                        forceUpdate),
                    CancellationToken.None).ConfigureAwait(false);
                _blobContentSession.UriCache.BulkAddDownloadUris(addResult.BlobDownloadUris);

                // add succeeded but returned an empty contenthashlistwith cache metadata. correct this.
                if (addResult.ContentHashListWithCacheMetadata == null)
                {
                    return
                        new Result<ContentHashListWithCacheMetadata>(
                            new ContentHashListWithCacheMetadata(
                               new ContentHashListWithDeterminism(null, blobContentHashListWithCacheMetadata.Determinism),
                               blobContentHashListWithCacheMetadata.GetRawExpirationTimeUtc(),
                               blobContentHashListWithCacheMetadata.ContentGuarantee));
                }
                else
                {
                    return await UnpackBlobContentHashListAsync(context, addResult.ContentHashListWithCacheMetadata);
                }
            }
            catch (Exception ex)
            {
                return new Result<ContentHashListWithCacheMetadata>(ex);
            }
        }

        private void AddDownloadUriToCache(BlobContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            if (contentHashListWithDeterminism.MetadataBlobDownloadUri != null)
            {
                _blobContentSession.UriCache.AddDownloadUri(
                    contentHashListWithDeterminism.BlobIdentifier.ToContentHash(),
                    new PreauthenticatedUri(contentHashListWithDeterminism.MetadataBlobDownloadUri, EdgeType.Unknown)); // EdgeType value shouldn't matter because we don't use it.
            }
        }

        private async Task<Result<ContentHashListWithCacheMetadata>> UnpackBlobContentHashListAsync(Context context, BlobContentHashListWithCacheMetadata blobCacheMetadata)
        {
            Contract.Assert(blobCacheMetadata != null);
            if (blobCacheMetadata.ContentHashListWithDeterminism.BlobIdentifier == null)
            {
                return new Result<ContentHashListWithCacheMetadata>(
                    new ContentHashListWithCacheMetadata(
                        new ContentHashListWithDeterminism(null, blobCacheMetadata.Determinism),
                        blobCacheMetadata.GetRawExpirationTimeUtc(),
                        blobCacheMetadata.ContentGuarantee,
                        blobCacheMetadata.HashOfExistingContentHashList));
            }

            BlobIdentifier blobId = blobCacheMetadata.ContentHashListWithDeterminism.BlobIdentifier;

            Func<ContentHash, CancellationToken, Task<Result<Stream>>> openStreamFunc = async (hash, cts) =>
            {
                OpenStreamResult openStreamResult = await _blobContentSession.OpenStreamAsync(context, hash, cts);
                if (openStreamResult.Succeeded)
                {
                    return new Result<Stream>(openStreamResult.Stream);
                }

                return new Result<Stream>(openStreamResult);
            };

            Result<ContentHashListWithDeterminism> contentHashListResult =
                await BlobContentHashListExtensions.UnpackFromBlob(
                    openStreamFunc,
                    blobId);

            if (contentHashListResult.Succeeded)
            {
                var contentHashListWithCacheMetadata = new ContentHashListWithCacheMetadata(
                    contentHashListResult.Value,
                    blobCacheMetadata.GetRawExpirationTimeUtc(),
                    blobCacheMetadata.ContentGuarantee,
                    blobCacheMetadata.HashOfExistingContentHashList);
                return new Result<ContentHashListWithCacheMetadata>(contentHashListWithCacheMetadata);
            }
            else
            {
                return new Result<ContentHashListWithCacheMetadata>(contentHashListResult);
            }
        }

        /// <inheritdoc />
        public Task IncorporateStrongFingerprints(
            Context context,
            string cacheNamespace,
            IncorporateStrongFingerprintsRequest incorporateStrongFingerprintsRequest)
        {
            return ArtifactHttpClientErrorDetectionStrategy.ExecuteAsync(
                context,
                "IncorporateStrongFingerprints",
                () => _buildCacheHttpClient.IncorporateStrongFingerprints(cacheNamespace, incorporateStrongFingerprintsRequest),
                CancellationToken.None);
        }
    }
}
