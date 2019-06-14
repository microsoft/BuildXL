// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Result of the Evict call.
    /// </summary>
    public class EvictResult : BoolResult
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="EvictResult"/> class.
        /// </summary>
        public EvictResult(long evictedSize, long evictedFiles, long pinnedSize, DateTime lastAccessTime, bool successfullyEvictedHash, long replicaCount)
        {
            EvictedSize = evictedSize;
            EvictedFiles = evictedFiles;
            PinnedSize = pinnedSize;
            LastAccessTime = lastAccessTime;
            SuccessfullyEvictedHash = successfullyEvictedHash;
            ReplicaCount = replicaCount;
            Age = DateTime.UtcNow - LastAccessTime;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="EvictResult"/> class.
        /// </summary>
        public EvictResult(string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="EvictResult"/> class.
        /// </summary>
        public EvictResult(Exception exception, string message = null)
            : base(exception, message)
        {
        }

        /// <summary>
        ///     Gets number of bytes evicted.
        /// </summary>
        public long EvictedSize { get; }

        /// <summary>
        ///     Gets number of files evicted.
        /// </summary>
        public long EvictedFiles { get; }

        /// <summary>
        ///     Gets byte count remaining pinned across all replicas for associated content hash.
        /// </summary>
        public long PinnedSize { get; }

        /// <summary>
        ///     Gets content's last-access time.
        /// </summary>
        public DateTime LastAccessTime { get; }

        /// <summary>
        ///     Gets a value indicating whether or not the hash was fully evicted.
        /// </summary>
        public bool SuccessfullyEvictedHash { get; }

        /// <summary>
        ///     Gets number of locations content exists at in the data center.
        /// </summary>
        public long ReplicaCount { get; }

        /// <summary>
        ///     Gets a value indicating the age of the hash at the time when the result was created.
        /// </summary>
        public TimeSpan Age { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded
                ? $"Success Size={EvictedSize} Files={EvictedFiles} Pinned={PinnedSize} LastAccessTime={LastAccessTime} ReplicaCount={ReplicaCount} Age={Age}"
                : GetErrorString();
        }
    }
}
