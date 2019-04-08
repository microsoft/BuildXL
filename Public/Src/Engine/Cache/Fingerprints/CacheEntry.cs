// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities.Collections;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// A single cache entry, stored under some <see cref="StrongContentFingerprint"/>.
    /// A cache entry contains a list of content hashes, presumably referring to content available in some content-addressable store.
    /// We distinguish one special entry in that list - <see cref="MetadataHash"/> - which may refer to some structured data (provenance
    /// information for the entry, filename labels for the <see cref="ReferencedContent"/>, etc.).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct CacheEntry
    {
        /// <nodoc />
        public readonly ContentHash MetadataHash;

        /// <nodoc />
        public readonly ArrayView<ContentHash> ReferencedContent;

        /// <summary>
        /// The cache where the entry ref was published to
        /// </summary>
        /// <remarks>
        /// Can be null when the cache entry is being used to point to new content that is not cached.
        /// </remarks>
        public readonly string OriginatingCache;

        /// <nodoc />
        public CacheEntry(ContentHash metadataHash, string originatingCache, ArrayView<ContentHash> referencedContent)
        {
            MetadataHash = metadataHash;
            ReferencedContent = referencedContent;
            OriginatingCache = originatingCache;
        }

        /// <summary>
        /// Creates a <see cref="CacheEntry"/> from a *non-empty* array of all referenced content.
        /// The first array member is designated as the <see cref="MetadataHash"/>; the (possibly empty)
        /// remainder is used as the <see cref="ReferencedContent"/>
        /// </summary>
        public static CacheEntry FromArray(ReadOnlyArray<ContentHash> metadataAndReferencedContent, string originatingCache)
        {
            Contract.Requires(metadataAndReferencedContent.Length > 0);
            return new CacheEntry(
                metadataAndReferencedContent[0],
                originatingCache,
                metadataAndReferencedContent.GetSubView(1));
        }

        /// <summary>
        /// Creates an array of hashes representing this entry (including <see cref="MetadataHash"/>).
        /// This is the inverse of <see cref="FromArray"/>.
        /// </summary>
        public ContentHash[] ToArray()
        {
            ContentHash[] result = new ContentHash[1 + ReferencedContent.Length];
            result[0] = MetadataHash;
            for (int i = 0; i < ReferencedContent.Length; i++)
            {
                result[i + 1] = ReferencedContent[i];
            }

            return result;
        }

        /// <summary>
        /// Creates an array of hashes representing this entry (including <see cref="MetadataHash"/>).
        /// The provided function translates each hash to some target type inline (rather than needing
        /// a temporary array of <see cref="ContentHash"/>).
        /// </summary>
        public T[] ToArray<T>(Func<ContentHash, T> project)
        {
            T[] result = new T[1 + ReferencedContent.Length];
            result[0] = project(MetadataHash);
            for (int i = 0; i < ReferencedContent.Length; i++)
            {
                result[i + 1] = project(ReferencedContent[i]);
            }

            return result;
        }
    }
}
