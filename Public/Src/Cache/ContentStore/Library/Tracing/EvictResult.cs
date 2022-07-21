// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

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
        public EvictResult(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="EvictResult"/> class.
        /// </summary>
        public EvictResult(Exception exception, string? message = null)
            : base(exception, message)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="EvictResult"/> class.
        /// </summary>
        public EvictResult(ContentHashWithLastAccessTimeAndReplicaCount info, long evictedSize, long evictedFiles, long pinnedSize, bool successfullyEvictedHash)
        {
            EvictedSize = evictedSize;
            EvictedFiles = evictedFiles;
            PinnedSize = pinnedSize;
            EvictionInfo = info.EvictionInfo;
            SuccessfullyEvictedHash = successfullyEvictedHash;
            CreationTime = DateTime.UtcNow;
        }

        /// <nodoc />
        public EvictResult(ResultBase other, string message)
            : base(other, message)
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
        /// The eviction info
        /// </summary>
        public ContentEvictionInfo EvictionInfo { get; }

        /// <summary>
        ///     Gets content's original last-access time.
        /// </summary>
        public DateTime LastAccessTime => EvictionInfo.LastAccessTime;

        /// <summary>
        /// The effective last access time of the content
        /// </summary>
        public DateTime? EffectiveLastAccessTime => EvictionInfo.EffectiveLastAccessTime;

        /// <summary>
        ///     Gets a value indicating whether or not the hash was fully evicted.
        /// </summary>
        public bool SuccessfullyEvictedHash { get; }

        /// <summary>
        ///     Gets number of locations content exists at in the data center.
        /// </summary>
        public long ReplicaCount => EvictionInfo.ReplicaCount;

        /// <summary>
        ///     Gets a value indicating the age of the hash at the time when the result was created.
        /// </summary>
        public DateTime CreationTime { get; }

        /// <summary>
        ///     Convert to the <see cref="DeleteResult"/> class.
        /// </summary>
        public DeleteResult ToDeleteResult(ContentHash contentHash)
        {
            if (Exception != null)
            {
                return new DeleteResult(DeleteResult.ResultCode.Error, Exception, ErrorMessage);
            }

            if (Succeeded)
            {
                if (!SuccessfullyEvictedHash)
                {
                    return new DeleteResult(DeleteResult.ResultCode.ContentNotFound, contentHash, EvictedSize);
                }

                return new DeleteResult(DeleteResult.ResultCode.Success, contentHash, EvictedSize);
            }

            // !HasException && Succeeded && !SuccessfulyEvictedHash
            return new DeleteResult(DeleteResult.ResultCode.ContentNotDeleted, contentHash, EvictedSize);
        }

        /// <nodoc />
        public TimeSpan Age => EvictionInfo.Age;

        /// <nodoc />
        public TimeSpan? EffectiveAge => EvictionInfo.EffectiveAge;

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded
                ? $"Success Size={EvictedSize} Files={EvictedFiles} Pinned={PinnedSize} LastAccessTime={LastAccessTime} Age={EvictionInfo.Age} ReplicaCount={ReplicaCount} EffectiveLastAccessTime={EffectiveLastAccessTime} EffectiveAge={EvictionInfo.EffectiveAge} Preferred={EvictionInfo.EvictionPreferred}"
                : GetErrorString();
        }
    }
}
