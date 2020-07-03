// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Represents access local store for a distributed content store.
    /// </summary>
    public interface ILocalContentStore
    {
        /// <summary>
        /// Enumerate all content currently in the cache. Returns list of hashes and their respective size.
        /// </summary>
        Task<IReadOnlyList<ContentInfo>> GetContentInfoAsync(CancellationToken token);

        /// <summary>
        /// Gets whether the local content store contains the content specified by the hash
        /// </summary>
        bool Contains(ContentHash hash);

        /// <summary>
        /// Gets the information about the content hash if present
        /// </summary>
        bool TryGetContentInfo(ContentHash hash, out ContentInfo info);

        /// <summary>
        /// Updates the last access time for the given piece of content with the given value if newer than registered last access time
        /// </summary>
        void UpdateLastAccessTimeIfNewer(ContentHash hash, DateTime newLastAccessTime);
    }

    /// <summary>
    /// Represents distributed location store.
    /// </summary>
    public interface IDistributedLocationStore
    {
        /// <summary>
        /// Returns true if the instance supports <see cref="GetHashesInEvictionOrder"/>.
        /// </summary>
        bool CanComputeLru { get; }

        /// <summary>
        /// Unregisters <paramref name="contentHashes"/> for the current machine.
        /// </summary>
        Task<BoolResult> UnregisterAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken token, TimeSpan? minEffectiveAge = null);

        /// <summary>
        /// Computes content hashes with effective last access time sorted in LRU manner.
        /// </summary>
        IEnumerable<ContentEvictionInfo> GetHashesInEvictionOrder(
            Context context,
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo);
    }
}
