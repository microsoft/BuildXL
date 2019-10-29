// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        Task<BoolResult> UnregisterAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken token);

        /// <summary>
        /// Computes content hashes with effective last access time sorted in LRU manner.
        /// </summary>
        IEnumerable<ContentHashWithLastAccessTimeAndReplicaCount> GetHashesInEvictionOrder(
            Context context,
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo);
    }
}
