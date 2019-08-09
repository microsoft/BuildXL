// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.Sessions;

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// Configuration object for <see cref="DistributedContentCopier{T}"/> and <see cref="DistributedContentStore{T}"/> classes.
    /// </summary>
    public sealed class DistributedContentStoreSettings
    {
        /// <summary>
        /// Default value for <see cref="ParallelCopyFilesLimit"/>
        /// </summary>
        public const int DefaultParallelCopyFilesLimit = 8;

        /// <summary>
        /// Default buffer size for file transfer of small files via FsServer in CopyToAsync.
        /// 4KB was selected because it is the default buffer size for a FileStream.
        /// </summary>
        public const int DefaultSmallBufferSize = 4096;

        /// <summary>
        /// Default buffer size for file transfer of large files via FsServer in CopyToAsync.
        /// 64KB was selected because it is significantly larger than 4KB (the original default buffer size), is a power of 2,
        /// and below the boundary for being placed in the large object heap (80KB).
        /// </summary>
        public const int DefaultLargeBufferSize = 64 * 1024;

        /// <summary>
        /// Delays for retries for file copies
        /// </summary>
        private static readonly List<TimeSpan> CacheCopierDefaultRetryIntervals = new List<TimeSpan>()
        {
            // retry the first 2 times quickly.
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(200),

            // then back-off exponentially.
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),

            // Borrowed from Empirical CacheV2 determined to be appropriate for general remote server restarts.
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120),
        };

        /// <summary>
        /// Default for <see cref="_assumeAvailableReplicaCount"/>
        /// </summary>
        public const int DefaultAssumeAvailableReplicaCount = 3;

        /// <summary>
        /// For file existence check, perform a quick check initially that allows iteration
        /// over multiple replicas first.
        /// </summary>
        public static readonly TimeSpan FileExistenceTimeoutFastPath = TimeSpan.FromSeconds(2);

        /// <summary>
        /// following a failure in a fast file existence check, allow the client
        /// to wait longer for file existences.
        /// </summary>
        public static readonly TimeSpan FileExistenceTimeoutSlowPath = TimeSpan.FromSeconds(20);

        /// <summary>
        /// The maximum time to spend doing verifications of content location records for one hash.
        /// </summary>
        public static readonly TimeSpan VerifyTimeout = FileExistenceTimeoutSlowPath;

        private int? _proactiveReplicationParallelism = null;
        private int? _assumeAvailableReplicaCount = null;
        //    PinConfiguration pinConfiguration = null,

        /// <summary>
        /// File copy replication parallelism.
        /// </summary>
        public int ProactiveReplicationParallelism
        {
            get => _proactiveReplicationParallelism.GetValueOrDefault(Environment.ProcessorCount);
            set => _proactiveReplicationParallelism = value;
        }

        /// <summary>
        /// Number of replicas at or above the content is considered available (when pin better is disabled)
        /// </summary>
        public int AssumeAvailableReplicaCount
        {
            get => Math.Max(_assumeAvailableReplicaCount ?? DefaultAssumeAvailableReplicaCount, 1);
            set => _assumeAvailableReplicaCount = value;
        }

        /// <summary>
        /// Clean random files at root
        /// </summary>
        public bool CleanRandomFilesAtRoot { get; set; } = false;

        /// <summary>
        /// Whether the underlying content store should be told to trust a hash when putting content.
        /// </summary>
        public bool UseTrustedHash { get; set; } = false;

        /// <summary>
        /// Whether the shortcuts for streaming, placing, and pinning the empty file are used.
        /// </summary>
        public bool EmptyFileHashShortcutEnabled { get; set; } = false;

        /// <summary>
        /// Files smaller than this should use the untrusted hash.
        /// </summary>
        public long TrustedHashFileSizeBoundary { get; set; } = -1;

        /// <summary>
        /// Files longer than this will be hashed concurrently with the download.
        /// All bytes downloaded before this boundary is met will be hashed inline.
        /// </summary>
        public long ParallelHashingFileSizeBoundary { get; set; }

        /// <summary>
        /// Maximum number of concurrent distributed copies.
        /// </summary>
        public int MaxConcurrentCopyOperations { get; set; } = 512;

        /// <summary>
        /// Maximum number of files to copy locally in parallel for a given operation
        /// </summary>
        public int ParallelCopyFilesLimit { get; set; } = DefaultParallelCopyFilesLimit;

        /// <summary>
        /// Delays for retries for file copies
        /// </summary>
        public IReadOnlyList<TimeSpan> RetryIntervalForCopies { get; set; } = CacheCopierDefaultRetryIntervals;

        /// <summary>
        /// Whether proactive copies are enabled.
        /// </summary>
        public bool EnableProactiveCopy { get; set; } = false;

        /// <summary>
        /// Maximum number of locations which should trigger a proactive copy.
        /// </summary>
        public int ProactiveCopyLocationsThreshold { get; set; } = 1;

        /// <summary>
        /// Defines pinning behavior
        /// </summary>
        public PinConfiguration PinConfiguration { get; set; } // Can be null.

        /// <nodoc />
        public static DistributedContentStoreSettings DefaultSettings { get; } = new DistributedContentStoreSettings();
    }
}
