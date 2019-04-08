// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using BuildXL.Cache.MemoizationStore.VstsInterfaces.Blob;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
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
        private readonly IContentSession _blobContentSession;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobBuildCacheContentHashListAdapter"/> class.
        /// </summary>
        public BlobBuildCacheContentHashListAdapter(IBlobBuildCacheHttpClient buildCacheHttpClient, IContentSession blobContentSession)
        {
            _buildCacheHttpClient = buildCacheHttpClient;
            _blobContentSession = blobContentSession;
        }

        /// <inheritdoc />
        public async Task<ObjectResult<IEnumerable<SelectorAndContentHashListWithCacheMetadata>>> GetSelectorsAsync(
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
                        BlobContentHashListCache.Instance.AddValue(
                            cacheNamespace,
                            new StrongFingerprint(weakFingerprint, selectorAndPossible.Selector),
                            selectorAndPossible.ContentHashList);

                        if (selectorAndPossible.ContentHashList.ContentHashListWithDeterminism.MetadataBlobDownloadUri != null)
                        {
                            AddDownloadUriToCache(selectorAndPossible.ContentHashList.ContentHashListWithDeterminism);
                        }
                    }

                    selectorsToReturn.Add(
                        new SelectorAndContentHashListWithCacheMetadata(
                            selectorAndPossible.Selector,
                            null));
                }

                return new ObjectResult<IEnumerable<SelectorAndContentHashListWithCacheMetadata>>(selectorsToReturn);
            }
            catch (Exception ex)
            {
                return new ObjectResult<IEnumerable<SelectorAndContentHashListWithCacheMetadata>>(ex);
            }
        }

        /// <inheritdoc />
        public async Task<ObjectResult<ContentHashListWithCacheMetadata>> GetContentHashListAsync(Context context, string cacheNamespace, StrongFingerprint strongFingerprint)
        {
            try
            {
                BlobContentHashListWithCacheMetadata blobCacheMetadata;
                if (!BlobContentHashListCache.Instance.TryGetValue(cacheNamespace, strongFingerprint, out blobCacheMetadata))
                {
                    BlobContentHashListResponse blobResponse = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                            context,
                            "GetContentHashList",
                            innerCts => _buildCacheHttpClient.GetContentHashListAsync(cacheNamespace, strongFingerprint),
                            CancellationToken.None)
                        .ConfigureAwait(false);

                    blobCacheMetadata = blobResponse.ContentHashListWithCacheMetadata;
                    DownloadUriCache.Instance.BulkAddDownloadUris(blobResponse.BlobDownloadUris);
                    AddDownloadUriToCache(blobCacheMetadata.ContentHashListWithDeterminism);
                }

                // Currently expect the Blob-based service to return null on misses,
                // but the other catches have been left for safety/compat.
                if (blobCacheMetadata == null)
                {
                    return new ObjectResult<ContentHashListWithCacheMetadata>(EmptyContentHashList);
                }

                return await UnpackBlobContentHashListAsync(context, blobCacheMetadata);
            }
            catch (ContentBagNotFoundException)
            {
                return new ObjectResult<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (CacheServiceException ex) when (ex.ReasonCode == CacheErrorReasonCode.ContentHashListNotFound)
            {
                return new ObjectResult<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (VssServiceResponseException serviceEx) when (serviceEx.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return new ObjectResult<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (Exception ex)
            {
                return new ObjectResult<ContentHashListWithCacheMetadata>(ex);
            }
        }
        /// <inheritdoc />
        public async Task<ObjectResult<ContentHashListWithCacheMetadata>> AddContentHashListAsync(
            Context context,
            string cacheNamespace,
            StrongFingerprint strongFingerprint,
            ContentHashListWithCacheMetadata valueToAdd)
        {
            try
            {
                Func<System.IO.Stream, System.Threading.CancellationToken, Task<StructResult<ContentHash>>> putStreamFunc =
                    async (stream, cts) =>
                    {
                        PutResult putResult = await _blobContentSession.PutStreamAsync(context, HashType.Vso0, stream, cts);
                        if (putResult.Succeeded)
                        {
                            return new StructResult<ContentHash>(putResult.ContentHash);
                        }

                        return new StructResult<ContentHash>(putResult);
                    };

                StructResult<ContentHash> blobIdOfContentHashListResult =
                    await BlobContentHashListExtensions.PackInBlob(
                        putStreamFunc,
                        valueToAdd.ContentHashListWithDeterminism);

                if (!blobIdOfContentHashListResult.Succeeded)
                {
                    return new ObjectResult<ContentHashListWithCacheMetadata>(blobIdOfContentHashListResult);
                }

                var blobContentHashListWithDeterminism =
                    new BlobContentHashListWithDeterminism(
                        valueToAdd.ContentHashListWithDeterminism.Determinism.EffectiveGuid,
                        BlobIdentifierToContentHashExtensions.ToBlobIdentifier(blobIdOfContentHashListResult.Data));

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
                        blobContentHashListWithCacheMetadata),
                    CancellationToken.None).ConfigureAwait(false);
                DownloadUriCache.Instance.BulkAddDownloadUris(addResult.BlobDownloadUris);

                // add succeeded but returned an empty contenthashlistwith cache metadata. correct this.
                if (addResult.ContentHashListWithCacheMetadata == null)
                {
                    return
                        new ObjectResult<ContentHashListWithCacheMetadata>(
                            new ContentHashListWithCacheMetadata(
                               new ContentHashListWithDeterminism(null, blobContentHashListWithCacheMetadata.Determinism),
                               blobContentHashListWithCacheMetadata.GetEffectiveExpirationTimeUtc(),
                              blobContentHashListWithCacheMetadata.ContentGuarantee));
                }
                else
                {
                    return await UnpackBlobContentHashListAsync(context, addResult.ContentHashListWithCacheMetadata);
                }
            }
            catch (Exception ex)
            {
                return new ObjectResult<ContentHashListWithCacheMetadata>(ex);
            }
        }

        private static void AddDownloadUriToCache(BlobContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            if (contentHashListWithDeterminism.MetadataBlobDownloadUri != null)
            {
                DownloadUriCache.Instance.AddDownloadUri(
                    contentHashListWithDeterminism.BlobIdentifier.ToContentHash(),
                    new PreauthenticatedUri(contentHashListWithDeterminism.MetadataBlobDownloadUri, EdgeType.Unknown)); // EdgeType value shouldn't matter because we don't use it.
            }
        }

        private async Task<ObjectResult<ContentHashListWithCacheMetadata>> UnpackBlobContentHashListAsync(Context context, BlobContentHashListWithCacheMetadata blobCacheMetadata)
        {
            Contract.Assert(blobCacheMetadata != null);
            if (blobCacheMetadata.ContentHashListWithDeterminism.BlobIdentifier == null)
            {
                return new ObjectResult<ContentHashListWithCacheMetadata>(
                    new ContentHashListWithCacheMetadata(
                        new ContentHashListWithDeterminism(null, blobCacheMetadata.Determinism), blobCacheMetadata.GetEffectiveExpirationTimeUtc(), blobCacheMetadata.ContentGuarantee, blobCacheMetadata.HashOfExistingContentHashList));
            }

            BlobIdentifier blobId = blobCacheMetadata.ContentHashListWithDeterminism.BlobIdentifier;

            Func<ContentHash, CancellationToken, Task<ObjectResult<Stream>>> openStreamFunc = async (hash, cts) =>
            {
                OpenStreamResult openStreamResult = await _blobContentSession.OpenStreamAsync(context, hash, cts);
                if (openStreamResult.Succeeded)
                {
                    return new ObjectResult<Stream>(openStreamResult.Stream);
                }

                return new ObjectResult<Stream>(openStreamResult);
            };
            StructResult<ContentHashListWithDeterminism> contentHashListResult =
                await BlobContentHashListExtensions.UnpackFromBlob(
                    openStreamFunc,
                    blobId);

            if (contentHashListResult.Succeeded)
            {
                var contentHashListWithCacheMetadata = new ContentHashListWithCacheMetadata(
                    contentHashListResult.Data,
                    blobCacheMetadata.GetRawExpirationTimeUtc(),
                    blobCacheMetadata.ContentGuarantee,
                    blobCacheMetadata.HashOfExistingContentHashList);
                return new ObjectResult<ContentHashListWithCacheMetadata>(contentHashListWithCacheMetadata);
            }
            else
            {
                return new ObjectResult<ContentHashListWithCacheMetadata>(contentHashListResult);
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
