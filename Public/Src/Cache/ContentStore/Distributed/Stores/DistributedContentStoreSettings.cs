// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;

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
        /// Files smaller than this should use the untrusted hash.
        /// </summary>
        public long TrustedHashFileSizeBoundary { get; set; } = -1;

        /// <summary>
        /// Whether the underlying content store should be told to trust a hash when putting content.
        /// </summary>
        /// <remarks>
        /// When trusted, then distributed file copier will hash the file and the store won't re-hash the file.
        /// </remarks>
        public bool UseTrustedHash(long fileSize)
        {
            // Only use trusted hash for files greater than _trustedHashFileSizeBoundary. Over a few weeks of data collection, smaller files appear to copy and put faster using the untrusted variant.
            return fileSize >= TrustedHashFileSizeBoundary;
        }

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
        /// Maximum number of concurrent proactive copies.
        /// </summary>
        public int MaxConcurrentProactiveCopyOperations { get; set; } = 512;

        /// <summary>
        /// Maximum number of files to copy locally in parallel for a given operation
        /// </summary>
        public int ParallelCopyFilesLimit { get; set; } = DefaultParallelCopyFilesLimit;

        /// <summary>
        /// Delays for retries for file copies
        /// </summary>
        public IReadOnlyList<TimeSpan> RetryIntervalForCopies { get; set; } = CacheCopierDefaultRetryIntervals;

        /// <summary>
        /// Controls the maximum total number of copy retry attempts
        /// </summary>
        public int MaxRetryCount { get; set; } = 32;

        /// <summary>
        /// Indicates whether proactive copies will trace successful results
        /// </summary>
        public bool TraceProactiveCopy { get; set; } = false;

        /// <summary>
        /// The mode in which proactive copy should run
        /// </summary>
        public ProactiveCopyMode ProactiveCopyMode { get; set; } = ProactiveCopyMode.Disabled;

        /// <summary>
        /// Whether to perform a proactive copy after putting a file.
        /// </summary>
        public bool ProactiveCopyOnPut { get; set; } = true;

        /// <summary>
        /// Whether to perform a proactive copy after copying because of a pin.
        /// </summary>
        public bool ProactiveCopyOnPin { get; set; } = false;

        /// <summary>
        /// Whether to push the content. If disabled, the copy will be requested and the target machine then will pull.
        /// </summary>
        public bool PushProactiveCopies { get; set; } = false;

        /// <summary>
        /// Whether to use the preferred locations for proactive copies.
        /// </summary>
        public bool ProactiveCopyUsePreferredLocations { get; set; } = false;

        /// <summary>
        /// Should only be used for testing to inline the operations like proactive copy.
        /// </summary>
        public bool InlineOperationsForTests { get; set; } = false;

        /// <summary>
        /// Maximum number of locations which should trigger a proactive copy.
        /// </summary>
        public int ProactiveCopyLocationsThreshold { get; set; } = 3;

        /// <summary>
        /// Whether to reject push copies based on whether we've evicted something younger recently.
        /// </summary>
        public bool ProactiveCopyRejectOldContent { get; set; } = false;

        /// <summary>
        /// Number of copy attempts which should be restricted in its number or replicas.
        /// </summary>
        public int CopyAttemptsWithRestrictedReplicas { get; set; } = 0;

        /// <summary>
        /// Number of replicas to attempt when a copy is being restricted.
        /// </summary>
        public int RestrictedCopyReplicaCount { get; set; } = 3;

        /// <summary>
        /// Time before a proactive copy times out.
        /// </summary>
        public TimeSpan TimeoutForProactiveCopies { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Whether to enable proactive replication
        /// </summary>
        public bool EnableProactiveReplication { get; set; } = false;

        /// <summary>
        /// The interval between proactive replication interations
        /// </summary>
        public TimeSpan ProactiveReplicationInterval { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Minimum delay between individual content proactive replications.
        /// </summary>
        public TimeSpan DelayForProactiveReplication { get; set; } = TimeSpan.FromMinutes(0.5);

        /// <summary>
        /// The maximum amount of copies allowed per proactive replication invocation.
        /// </summary>
        public int ProactiveReplicationCopyLimit { get; set; } = 5;
        
        /// <summary>
        /// The amount of time for nagling GetBulk (locations) for proactive copy operations
        /// </summary>
        public TimeSpan ProactiveCopyGetBulkInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// The size of nagle batch for proactive copy get bulk
        /// </summary>
        public int ProactiveCopyGetBulkBatchSize { get; set; } = 20;

        /// <summary>
        /// Defines pinning behavior
        /// </summary>
        public PinConfiguration PinConfiguration { get; set; }

        /// <nodoc />
        public static DistributedContentStoreSettings DefaultSettings { get; } = new DistributedContentStoreSettings();

        /// <summary>
        /// Maximum number of PutFile and PlaceFile operations that can happen concurrently.
        /// </summary>
        public int MaximumConcurrentPutAndPlaceFileOperations { get; set; } = 512;

        /// <summary>
        /// Indicates whether a post initialization task is set to complete after startup to force local eviction to wait
        /// for distributed store initialization to complete.
        /// </summary>
        public bool SetPostInitializationCompletionAfterStartup { get; set; } = true;

        /// <summary>
        /// Indicates whether repair handling logic is enabled which removes a machine from Redis when a repair operation is triggered.
        /// </summary>
        public bool EnableRepairHandling { get; set; } = true;

        /// <summary>
        /// The amount of time added per replica for distributed eviction effective age computation.
        /// </summary>
        public int? ReplicaCreditInMinutes { get; set; }

        /// <summary>
        /// The batch size used by the location store
        /// </summary>
        public int LocationStoreBatchSize { get; set; }

        /// <summary>
        /// Max size for storing blobs in the ContentLocationStore
        /// </summary>
        public long MaxBlobSize { get; set; }

        /// <summary>
        /// Returns true if Redis can be used for storing small files.
        /// </summary>
        public bool AreBlobsSupported { get; set; }
    }
}
