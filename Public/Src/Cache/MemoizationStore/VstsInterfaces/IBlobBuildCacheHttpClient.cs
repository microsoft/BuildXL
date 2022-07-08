// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces.Blob;

namespace BuildXL.Cache.MemoizationStore.VstsInterfaces
{
    /// <summary>
    /// The interface representing an http client to talk to a VSTS build cache service using blobs for the metadata documents.
    /// </summary>
    public interface IBlobBuildCacheHttpClient : IBuildCacheHttpClientCommon
    {
        /// <summary>
        /// Gets a content bag from the build cache service given a strong fingerprint for a particular request context.
        /// This represents
        /// 1) opaque bag with serialized bytes that can be transferred to the consumer.
        /// 2) a set of download URIs for content for the blobs that are contained by the content bag.
        /// </summary>
        /// <remarks>
        /// When `includeDownloadUris` is `true`, `BlobContentHashListResponse.BlobDownloadUris` is populated with
        /// SAS URIs to download the content referenced in the CHL. Otherwise, the cache will have to call
        /// BlobStore to get the download Uris.  Setting this to `true` is most helpful when the client will end up
        /// needing to download all the content.  Right now, it's basically always helpful because the cache will
        /// make point queries for each and every blob in CHL, so a CHL of 1000 files could end up with 1000
        /// round-trips to AzDO.
        /// </remarks>
        Task<BlobContentHashListResponse> GetContentHashListAsync(
            string cacheNamespace,
            StrongFingerprint strongFingerprint,
            bool includeDownloadUris);

        /// <summary>
        /// Adds a content bag to the L3 cache store. Adding a content bag also means
        /// 1) Referencing the blobbed items that belong to the content bag
        /// 2) Adding the content bag to the store
        /// 3) Adding a fingerprint selector that leads to this content bag being found
        /// </summary>
        Task<BlobContentHashListResponse> AddContentHashListAsync(
            string cacheNamespace,
            StrongFingerprint strongFingerprint,
            BlobContentHashListWithCacheMetadata contentHashList,
            bool forceUpdate);

        /// <summary>
        /// Returns a set of fingerprintSelectors that match the weak fingerprint being requested.
        /// Does not return any content bags associated with that fingerprint selector.
        /// Returns a maximum of maxSelectorsToFetch selectors.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        Task<BlobSelectorsResponse> GetSelectors(
            string cacheNamespace,
            Fingerprint weakFingerprint,
            bool includeDownloadUris,
            int maxSelectorsToFetch);

        /// <summary>
        /// Returns a set of fingerprintSelectors that match the weak fingerprint being requested.
        /// Returns all associated fingerprintselectors, and returns no content bags.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        Task<BlobSelectorsResponse> GetSelectors(string cacheNamespace, Fingerprint weakFingerprint, bool includeDownloadUris);
    }
}
