// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.VstsInterfaces;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Newtonsoft.Json;

namespace BuildXL.Cache.MemoizationStore.VstsInterfaces.Blob
{
#pragma warning disable SA1402 // File may only contain a single class
#pragma warning disable SA1649 // File name must match first type name
    /// <summary>
    /// represents a ContentHashList with metadata pertaining to the VSTS service.
    /// </summary>
    [DataContract]
    public class BlobContentHashListWithCacheMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BlobContentHashListWithCacheMetadata"/> class.
        /// </summary>
        public BlobContentHashListWithCacheMetadata(
            BlobContentHashListWithDeterminism contentHashListWithDeterminism,
            DateTime? contentHashListExpirationUtc,
            ContentAvailabilityGuarantee contentGuarantee,
            byte[] hashOfExistingContentHashList = null)
        {
            if (contentHashListExpirationUtc != null && contentHashListExpirationUtc.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Time to live must be an absolute UTC date time.");
            }

            ContentHashListWithDeterminism = contentHashListWithDeterminism;
            ContentHashListExpirationUtc = contentHashListExpirationUtc;
            ContentGuarantee = contentGuarantee;
            HashOfExistingContentHashList = hashOfExistingContentHashList;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobContentHashListWithCacheMetadata"/> class.
        /// </summary>
        public BlobContentHashListWithCacheMetadata(BlobContentHashListWithDeterminism contentHashListWithDeterminism, DateTime contentHashListExpirationUtc)
            : this(contentHashListWithDeterminism, contentHashListExpirationUtc, ContentAvailabilityGuarantee.NoContentBackedByCache)
        {
        }

        private BlobContentHashListWithCacheMetadata()
        {
        }

        /// <summary>
        /// Gets the determinism for the contenthashlist.
        /// </summary>
        [IgnoreDataMember]
        public CacheDeterminism Determinism
        {
            get
            {
                if (ContentHashListExpirationUtc.HasValue)
                {
                    return CacheDeterminism.ViaCache(ContentHashListWithDeterminism.DeterminismGuid, ContentHashListExpirationUtc.Value);
                }

                return CacheDeterminism.None;
            }
        }

        /// <summary>
        /// Gets the content hash list.
        /// </summary>
        [DataMember]
        public BlobContentHashListWithDeterminism ContentHashListWithDeterminism { get; private set; }

        /// <summary>
        /// Gets or sets the expiration time in UTC.
        /// </summary>
        [DataMember]
        private DateTime? ContentHashListExpirationUtc { get; set; }

        /// <summary>
        /// Gets or sets the content availability guarantee.
        /// </summary>
        [DataMember]
        public ContentAvailabilityGuarantee ContentGuarantee { get; set; }

        /// <summary>
        /// Gets the ContentHash of the entries that make up this ContentHashlist.
        /// </summary>
        /// <remarks>Used in add or update semantics to replace existing hashlists.</remarks>
        [DataMember]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public byte[] HashOfExistingContentHashList { get; private set; }

        /// <summary>
        /// Returns the effective expiration UTC time factoring in content availability.
        /// </summary>
        public DateTime? GetEffectiveExpirationTimeUtc()
        {
            if (ContentGuarantee == ContentAvailabilityGuarantee.AllContentBackedByCache)
            {
                return ContentHashListExpirationUtc;
            }

            return null;
        }

        /// <summary>
        /// Returns the effective expiration UTC time factoring in content availability.
        /// </summary>
        public DateTime? GetRawExpirationTimeUtc() => ContentHashListExpirationUtc;
    }

    /// <summary>
    /// The response from the service for a content hash list request.
    /// </summary>
    [DataContract]
    public class BlobContentHashListResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BlobContentHashListResponse"/> class.
        /// </summary>
        public BlobContentHashListResponse(
            BlobContentHashListWithCacheMetadata contentHashListWithCacheMetadata,
            IDictionary<BlobIdentifier, ExpirableUri> blobIdsToUris)
        {
            ContentHashListWithCacheMetadata = contentHashListWithCacheMetadata;
            BlobDownloadUris = blobIdsToUris?.ToDictionary(
                blobId => blobId.Key.ValueString,
                blobKvp => blobKvp.Value.NotNullUri);
        }

        [JsonConstructor]
        private BlobContentHashListResponse()
        {
        }

        /// <summary>
        /// Gets the content hash list with cache metadata.
        /// </summary>
        [DataMember]
        public BlobContentHashListWithCacheMetadata ContentHashListWithCacheMetadata { get; private set; }

        /// <summary>
        /// Gets the download URI mappings for the blobs in a contenthashlist.
        /// </summary>
        [DataMember]
        public IDictionary<string, Uri> BlobDownloadUris { get; private set; }
    }

    /// <summary>
    /// Selector and Possible Content Hash lists returned by the service.
    /// </summary>
    [DataContract]
    public struct BlobContentHashListWithDeterminism : IEquatable<BlobContentHashListWithDeterminism>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BlobContentHashListWithDeterminism"/> struct.
        /// </summary>
        public BlobContentHashListWithDeterminism(Guid value, BlobIdentifier blobId)
            : this(value, blobId, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobContentHashListWithDeterminism"/> struct.
        /// </summary>
        public BlobContentHashListWithDeterminism(Guid value, BlobIdentifier blobId, Uri downloadUri)
        {
            ContentHashListMetadataBlobId = blobId?.ValueString;
            ContentHashListMetadataBlobDownloadUriString = downloadUri?.AbsoluteUri;
            Guid = value.ToString();
        }

        /// <summary>
        /// Gets the content hash list blob id.
        /// </summary>
        [IgnoreDataMember]
        public BlobIdentifier BlobIdentifier => ContentHashListMetadataBlobId == null ? null : BlobIdentifier.Deserialize(ContentHashListMetadataBlobId);

        /// <summary>
        /// Gets the determinism guid.
        /// </summary>
        [IgnoreDataMember]
        public Guid DeterminismGuid => new Guid(Guid);

        /// <summary>
        /// Gets the download URi for the metadata blob.
        /// </summary>
        [IgnoreDataMember]
        public Uri MetadataBlobDownloadUri
            => ContentHashListMetadataBlobDownloadUriString == null ? null : new Uri(ContentHashListMetadataBlobDownloadUriString);

        /// <summary>
        /// Gets or sets the content hash list blob id.
        /// </summary>
        [DataMember]
        private string ContentHashListMetadataBlobId { get; set; }

        /// <summary>
        /// Gets or sets The download URI for the content hashlist blob.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1044:PropertiesShouldNotBeWriteOnly")]
        [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
        [DataMember]
        private string ContentHashListMetadataBlobDownloadUriString { get; set; }

        /// <summary>
        /// Gets or sets the Determinism guid string
        /// </summary>
        [DataMember]
        private string Guid { get; set; }

        /// <summary>
        /// Sets the downloadURI for the contenthashlist.
        /// </summary>
        public void SetContentHashListMetadataBlobDownloadUriString(Uri downloadUri)
        {
            ContentHashListMetadataBlobDownloadUriString = downloadUri?.AbsoluteUri;
        }

        /// <inheritdoc />
        public bool Equals(BlobContentHashListWithDeterminism other)
        {
            if (!string.Equals(ContentHashListMetadataBlobId, other.ContentHashListMetadataBlobId, StringComparison.OrdinalIgnoreCase))
            {
                if (ContentHashListMetadataBlobId == null || other.ContentHashListMetadataBlobId == null)
                {
                    return false;
                }
            }

            if (!string.Equals(ContentHashListMetadataBlobDownloadUriString, other.ContentHashListMetadataBlobDownloadUriString, StringComparison.OrdinalIgnoreCase))
            {
                if (ContentHashListMetadataBlobDownloadUriString == null || other.ContentHashListMetadataBlobDownloadUriString == null)
                {
                    return false;
                }
            }

            return string.Equals(Guid, other.Guid, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (ContentHashListMetadataBlobId?.GetHashCode() ?? 0) ^ (ContentHashListMetadataBlobDownloadUriString?.GetHashCode() ?? 0) ^ (Guid?.GetHashCode() ?? 0);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"ContentHashListBlobId=[{ContentHashListMetadataBlobId}], Determinism={Guid}, DownloadUri={ContentHashListMetadataBlobDownloadUriString}";
        }

        /// <nodoc />
        public static bool operator ==(BlobContentHashListWithDeterminism left, BlobContentHashListWithDeterminism right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(BlobContentHashListWithDeterminism left, BlobContentHashListWithDeterminism right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Gets the response to a selectors request from the service.
    /// </summary>
    [DataContract]
    public class BlobSelectorsResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BlobSelectorsResponse"/> class.
        /// </summary>
        public BlobSelectorsResponse(
            IReadOnlyList<BlobSelectorAndContentHashList> selectorsAndPossibleContentHashLists)
        {
            SelectorsAndPossibleContentHashLists = selectorsAndPossibleContentHashLists;
        }

        /// <summary>
        /// Gets the selectors and possible content hash lists.
        /// </summary>
        [DataMember]
        public IReadOnlyList<BlobSelectorAndContentHashList> SelectorsAndPossibleContentHashLists { get; private set; }
    }

    /// <summary>
    /// Selector and Possible Content Hash lists returned by the service.
    /// </summary>
    [DataContract]
    public class BlobSelectorAndContentHashList
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BlobSelectorAndContentHashList"/> class.
        /// </summary>
        public BlobSelectorAndContentHashList(
            Selector selector,
            BlobContentHashListWithCacheMetadata contentHashList)
        {
            Selector = selector;
            ContentHashList = contentHashList;
        }

        /// <summary>
        /// Gets the selector.
        /// </summary>
        [JsonConverter(typeof(SelectorConverter))]
        [DataMember]
        public Selector Selector { get; private set; }

        /// <summary>
        /// Gets the content hash list.
        /// </summary>
        [DataMember]
        public BlobContentHashListWithCacheMetadata ContentHashList { get; private set; }
    }
}
