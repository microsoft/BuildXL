// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using Microsoft.VisualStudio.Services.WebApi;

namespace BuildXL.Cache.MemoizationStore.Vsts.Adapters
{
    /// <summary>
    /// An adapter to talk to the VSTS Build Cache Service with items.
    /// </summary>
    public class ItemBuildCacheContentHashListAdapter : IContentHashListAdapter
    {
        private static readonly ContentHashListWithCacheMetadata EmptyContentHashList = new ContentHashListWithCacheMetadata(
                   new ContentHashListWithDeterminism(null, CacheDeterminism.None),
                   null,
                   ContentAvailabilityGuarantee.NoContentBackedByCache);

        private readonly IBuildCacheHttpClient _buildCacheHttpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemBuildCacheContentHashListAdapter"/> class.
        /// </summary>
        public ItemBuildCacheContentHashListAdapter(IBuildCacheHttpClient buildCacheHttpClient)
        {
            _buildCacheHttpClient = buildCacheHttpClient;
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
                var selectorsResponse = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                    context,
                    "GetSelectors",
                    innerCts => _buildCacheHttpClient.GetSelectors(
                        cacheNamespace,
                        weakFingerprint,
                        maxSelectorsToFetch), CancellationToken.None);
                var selectorsToReturn = new List<SelectorAndContentHashListWithCacheMetadata>();
                foreach (
                    SelectorAndPossibleContentHashListResponse selectorAndPossible in selectorsResponse.SelectorsAndPossibleContentHashLists
                )
                {
                    if (selectorAndPossible.ContentHashList != null)
                    {
                        DownloadUriCache.Instance.BulkAddDownloadUris(selectorAndPossible.ContentHashList.BlobDownloadUris);
                    }

                    selectorsToReturn.Add(
                        new SelectorAndContentHashListWithCacheMetadata(
                            selectorAndPossible.Selector,
                            selectorAndPossible.ContentHashList?.ContentHashListWithCacheMetadata));
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
                ContentHashListResponse response =
                    await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                        context,
                        "GetContentHashList",
                        innerCts => _buildCacheHttpClient.GetContentHashListAsync(cacheNamespace, strongFingerprint),
                        CancellationToken.None).ConfigureAwait(false);

                DownloadUriCache.Instance.BulkAddDownloadUris(response.BlobDownloadUris);

                // our response should never be null.
                if (response.ContentHashListWithCacheMetadata != null)
                {
                    return new ObjectResult<ContentHashListWithCacheMetadata>(response.ContentHashListWithCacheMetadata);
                }

                return new ObjectResult<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (CacheServiceException ex) when (ex.ReasonCode == CacheErrorReasonCode.ContentHashListNotFound)
            {
                return new ObjectResult<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (ContentBagNotFoundException)
            {
                return new ObjectResult<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (VssServiceResponseException serviceEx) when (serviceEx.HttpStatusCode == HttpStatusCode.NotFound)
            {
                // Currently expect the Item-based service to return VssServiceResponseException on misses,
                // but the other catches have been left for safety/compat.
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
                ContentHashListResponse addResult = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                    context,
                    "AddContentHashList",
                    innerCts => _buildCacheHttpClient.AddContentHashListAsync(
                        cacheNamespace,
                        strongFingerprint,
                        valueToAdd), CancellationToken.None).ConfigureAwait(false);
                DownloadUriCache.Instance.BulkAddDownloadUris(addResult.BlobDownloadUris);

                // add succeeded but returned an empty contenthashlistwith cache metadata. correct this.
                if (addResult.ContentHashListWithCacheMetadata == null)
                {
                    return
                        new ObjectResult<ContentHashListWithCacheMetadata>(
                            new ContentHashListWithCacheMetadata(
                                new ContentHashListWithDeterminism(null, valueToAdd.ContentHashListWithDeterminism.Determinism),
                                valueToAdd.GetEffectiveExpirationTimeUtc(),
                                valueToAdd.ContentGuarantee,
                                valueToAdd.HashOfExistingContentHashList));
                }
                else if (addResult.ContentHashListWithCacheMetadata.ContentHashListWithDeterminism.ContentHashList != null
                         && addResult.ContentHashListWithCacheMetadata.HashOfExistingContentHashList == null)
                {
                    return new ObjectResult<ContentHashListWithCacheMetadata>(
                        new ContentHashListWithCacheMetadata(
                            addResult.ContentHashListWithCacheMetadata.ContentHashListWithDeterminism,
                            addResult.ContentHashListWithCacheMetadata.GetEffectiveExpirationTimeUtc(),
                            addResult.ContentHashListWithCacheMetadata.ContentGuarantee,
                            addResult.ContentHashListWithCacheMetadata.ContentHashListWithDeterminism.ContentHashList.GetHashOfHashes()));
                }
                else
                {
                    return new ObjectResult<ContentHashListWithCacheMetadata>(addResult.ContentHashListWithCacheMetadata);
                }
            }
            catch (Exception ex)
            {
                return new ObjectResult<ContentHashListWithCacheMetadata>(ex);
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
