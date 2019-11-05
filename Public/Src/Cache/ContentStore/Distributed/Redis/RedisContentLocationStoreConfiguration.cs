// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Configuration properties for <see cref="RedisContentLocationStore"/>
    /// </summary>
    public class RedisContentLocationStoreConfiguration : LocalLocationStoreConfiguration
    {
        /// <summary>
        /// Gets or sets size of batch calls to Redis.
        /// </summary>
        public int RedisBatchPageSize { get; set; } = RedisContentLocationStoreConstants.DefaultBatchSize;

        /// <summary>
        /// Gets or sets minimum replica count to determine safe eviction during distributed eviction.
        /// </summary>
        public int MinReplicaCountToSafeEvict { get; set; } = RedisContentLocationStoreConstants.DefaultMinReplicaCountToSafeEvict;

        /// <summary>
        /// Gets or sets minimum replica count to determine immediate eviction during distributed eviction.
        /// </summary>
        public int MinReplicaCountToImmediateEvict { get; set; } = RedisContentLocationStoreConstants.DefaultMinReplicaCountToImmediateEvict;

        /// <summary>
        /// The time before machines are marked as expired and locations are eligible for garbage collection from the local database
        /// </summary>
        public TimeSpan MachineExpiry { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// The interval at which cluster state should be mirror by master machine
        /// </summary>
        public TimeSpan ClusterStateMirrorInterval => MachineExpiry.Multiply(0.5);

        /// <summary>
        /// Indicates whether cluster state should be mirrored to secondary redis
        /// </summary>
        public bool MirrorClusterState { get; set; } = true;

        /// <summary>
        /// Indicates whether optimistic form of RegisterLocalLocation is used which uses a single set if not exists operation
        /// to set size, local machine bit, and expiry. This falls back to multi-operation version with distinct operations for
        /// setting size, local machine bit, and expiry.
        /// </summary>
        public bool UseOptimisticRegisterLocalLocation { get; set; } = true;

        /// <summary>
        /// Default configuration instance.
        /// </summary>
        public static RedisContentLocationStoreConfiguration Default { get; } = new RedisContentLocationStoreConfiguration();

        /// <summary>
        /// Configuration of redis garbage collection.
        /// </summary>
        public RedisGarbageCollectionConfiguration GarbageCollectionConfiguration { get; set; }

        /// <nodoc />
        public bool GarbageCollectionEnabled => GarbageCollectionConfiguration != null;

        /// <summary>
        /// Expiry time for all blobs stored in Redis.
        /// </summary>
        public int BlobExpiryTimeMinutes { get; set; } = 0;

        /// <summary>
        /// Max size for storing blobs in the ContentLocationStore
        /// </summary>
        public long MaxBlobSize { get; set; } = 1024 * 4;

        /// <summary>
        /// Max capacity for blobs in the ContentLocationStore
        /// </summary>
        public long MaxBlobCapacity { get; set; } = 1024 * 1024 * 1024;

        /// <summary>
        /// Amount of entries to compute evictability metric for in a single pass. The larger this is, the faster the
        /// candidate pool fills up, but also the slower it is to produce a candidate. Helps control how fast we need
        /// to produce candidates.
        /// </summary>
        public int EvictionWindowSize { get; set; } = 500;

        /// <summary>
        /// Amount of entries to compute evictability metric for before determining eviction order. The larger this is,
        /// the slower and more resources eviction takes, but also the more accurate it becomes.
        /// </summary>
        /// <remarks>
        /// Two pools are kept in memory at the same time, so we effectively keep double the amount of data in memory.
        /// </remarks>
        public int EvictionPoolSize { get; set; } = 5000;

        /// <summary>
        /// The minimum age a candidate for eviction must be older than to be evicted. If the candidate's age is not older
        /// then we simply ignore it for eviction and trace information to help us determine why the candidate is nominated for eviction
        /// with such a younge age.
        /// <remarks>
        /// Default to zero time to allow all candidates to pass, when we want to test for eviction min age we can configure for it.
        /// </remarks>
        /// </summary>
        public TimeSpan EvictionMinAge { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Fraction of the pool considered trusted to be in the accurate order.
        /// </summary>
        /// <remarks>
        /// Estimated by looking into the percentage of files we remove of the total content store. Means we remove
        /// at most 76 entries per iteration when we stabilize.
        /// </remarks>
        public float EvictionRemovalFraction { get; set; } = 0.015355f;

        /// <summary>
        /// Returns true if Redis can be used for storing small files.
        /// </summary>
        public bool AreBlobsSupported => BlobExpiryTimeMinutes > 0 && MaxBlobCapacity > 0 && MaxBlobSize > 0;

        /// <summary>
        /// Indicates the mode used when writing content locations
        /// </summary>
        public ContentLocationMode WriteMode { get; set; } = ContentLocationMode.Redis;

        /// <summary>
        /// Indicates the mode used when reading content locations
        /// </summary>
        public ContentLocationMode ReadMode { get; set; } = ContentLocationMode.Redis;

        /// <nodoc />
        public bool HasWriteMode(ContentLocationMode mode)
        {
            return (WriteMode & mode) == mode;
        }

        /// <nodoc />
        public bool HasReadMode(ContentLocationMode mode)
        {
            return (ReadMode & mode) == mode;
        }

        /// <nodoc />
        public bool HasReadOrWriteMode(ContentLocationMode mode)
        {
            return HasReadMode(mode) || HasWriteMode(mode);
        }
    }

    /// <summary>
    /// Configuration class for redis garbage collection process.
    /// </summary>
    public class RedisGarbageCollectionConfiguration
    {
        /// <summary>
        /// Time between garbage collections.
        /// </summary>
        public TimeSpan GarbageCollectionInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Gets the name of the key which controls the least for redis garbage collection
        /// </summary>
        public string GarbageCollectionLeaseKey { get; set; } = "RedisGcRoleLeaseKey";

        /// <summary>
        /// Number of iterations between throttling and progress reporting.
        /// </summary>
        public int IterationsInBatch { get; set; } = 50;

        /// <summary>
        /// The delay between GC batches.
        /// </summary>
        public TimeSpan DelayBetweenBatches { get; set; } = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Number of entries retrieved during one scan redis call.
        /// </summary>
        public int ScanBatchSize { get; set; } = 1000;

        /// <summary>
        /// The delay between entries last access time and the GC time when an empty entry is considered eligible for garbage collection.
        /// </summary>
        /// <remarks>
        /// Non-zero time here is important for work-arounding the issue with distributed eviction.
        /// Currently, the client may clear the last bit of the entry and later during distributed eviction process it may decided to add the bit back.
        /// If we'll remove the entry in that time window, the entry will be lost forever.
        /// To avoid this race condition, only empty records that were accessed longer than 5 minutes are considered eligible for deletion.
        /// </remarks>
        public TimeSpan MaximumEntryLastAccessTime { get; set; } = TimeSpan.FromMinutes(5);
    }
}
