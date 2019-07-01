// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
        /// <summary>
        ///     Result of the Delete call.
        /// </summary>
        public class DeleteResult : BoolResult
        {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DeleteResult"/> class.
        /// </summary>
        public DeleteResult(ContentHash contentHash, long evictedSize, long pinnedSize, long replicaCount)
        {
            ContentHash = contentHash;
            EvictedSize = evictedSize;
            PinnedSize = pinnedSize;
            ReplicaCount = replicaCount;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeleteResult"/> class.
        /// </summary>
        public DeleteResult(string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeleteResult"/> class.
        /// </summary>
        public DeleteResult(Exception exception, string message = null)
            : base(exception, message)
        {
        }

        /// <summary>
        ///     Gets the deleted hash.
        /// </summary>
        public ContentHash ContentHash { get; }

        /// <summary>
        ///     Gets number of bytes evicted.
        /// </summary>
        public long EvictedSize { get; }

        /// <summary>
        ///     Gets byte count remaining pinned across all replicas for associated content hash.
        /// </summary>
        public long PinnedSize { get; }

        /// <summary>
        ///     Gets number of locations content exists at in the data center.
        /// </summary>
        public long ReplicaCount { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded
                ? $"Success Hash={ContentHash} Size={EvictedSize} Pinned={PinnedSize} ReplicaCount={ReplicaCount}"
                : GetErrorString();
        }
    }
}
