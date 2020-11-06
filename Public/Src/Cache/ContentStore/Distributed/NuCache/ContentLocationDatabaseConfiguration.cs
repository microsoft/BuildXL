// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed.NuCache.InMemory;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.Host.Configuration;
using static BuildXL.Utilities.ConfigurationHelper;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <nodoc />
    public enum MetadataGarbageCollectionStrategy
    {
        /// <summary>
        /// Always keep at most <see cref="ContentLocationDatabaseConfiguration.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep"/> entries, using
        /// LRU policy.
        /// </summary>
        CapacityBound,

        /// <summary>
        /// Attempt to keep as many entries possible, as long as they take no more than
        /// <see cref="ContentLocationDatabaseConfiguration.MetadataGarbageCollectionMaximumSizeMb"/>.
        /// </summary>
        DiskSizeBound,
    }

    /// <summary>
    /// Configuration type for <see cref="ContentLocationDatabase"/> family of types.
    /// </summary>
    public abstract class ContentLocationDatabaseConfiguration
    {
        /// <summary>
        /// The touch interval for database entries.
        /// NOTE: This is set internally to the same value as <see cref="LocalLocationStoreConfiguration.TouchFrequency"/>
        /// </summary>
        internal TimeSpan TouchFrequency { get; set; }

        /// <summary>
        /// Whether to run garbage collection content and metadata GC concurrently
        /// </summary>
        public bool GarbageCollectionConcurrent { get; set; } = false;

        /// <summary>
        /// Interval between garbage collecting unreferenced content location entries and metadata in a local database.
        /// </summary>
        public TimeSpan GarbageCollectionInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Whether to enable garbage collection of metadata
        /// </summary>
        public bool MetadataGarbageCollectionEnabled { get; set; } = false;

        /// <summary>
        /// Maximum number of metadata entries to keep after garbage collection.
        ///
        /// Only useful when:
        /// - <see cref="MetadataGarbageCollectionEnabled"/> is true
        /// - <see cref="MetadataGarbageCollectionStrategy"/> is MaximumEntries
        /// </summary>
        public int MetadataGarbageCollectionMaximumNumberOfEntriesToKeep { get; set; } = 500_000;

        /// <summary>
        /// Maximum allowed size of the Metadata column family.
        ///
        /// Only useful when:
        /// - <see cref="MetadataGarbageCollectionEnabled"/> is true
        /// - <see cref="MetadataGarbageCollectionStrategy"/> is MaximumSize
        /// </summary>
        public double MetadataGarbageCollectionMaximumSizeMb { get; set; } = 20_000;

        /// <summary>
        /// Metadata garbage collection strategy to use.
        /// </summary>
        public MetadataGarbageCollectionStrategy MetadataGarbageCollectionStrategy { get; set; } = MetadataGarbageCollectionStrategy.CapacityBound;

        /// <summary>
        /// Whether to clean the DB when it is corrupted.
        /// </summary>
        /// <remarks>
        /// Should be false for Content, true for Metadata.
        /// </remarks>
        public bool OnFailureDeleteExistingStoreAndRetry { get; set; } = false;

        /// <summary>
        /// Specifies whether the context operation guid is used when logging entry operations
        /// </summary>
        public bool UseContextualEntryOperationLogging { get; set; } = false;

        /// <summary>
        /// Specifies whether to trace touches or not.
        /// Tracing touches is expensive in terms of the amount of traffic to Kusto and in terms of memory traffic.
        /// </summary>
        public bool TraceTouches { get; set; } = true;
    }

    /// <summary>
    /// Configuration type for <see cref="MemoryContentLocationDatabase"/>.
    /// </summary>
    public sealed class MemoryContentLocationDatabaseConfiguration : ContentLocationDatabaseConfiguration
    {
        // No members. This is a marker type 
    }

    /// <summary>
    /// Supported heuristics for full range compaction
    /// </summary>
    public enum FullRangeCompactionStrategy
    {
        /// <summary>
        /// Compacts the entire key range at once
        /// </summary>
        EntireRange = 0,
        /// <summary>
        /// Splits the key range on byte increments, performs compaction on each range at a specified time period
        /// </summary>
        ByteIncrements = 1,
        /// <summary>
        /// Splits the key range on word increments, performs compaction on each range at a specified time period
        /// </summary>
        /// <remarks>
        /// The vast majority of content entries start with the hash type, which is a single byte, matching
        /// the hash type. Hence doing byte increments means we compact the entire content database at once, which
        /// beats the purpose of this feature.
        /// </remarks>
        WordIncrements = 2,
    }

    /// <summary>
    /// Configuration type for <see cref="RocksDbContentLocationDatabase"/>.
    /// </summary>
    public sealed class RocksDbContentLocationDatabaseConfiguration : ContentLocationDatabaseConfiguration
    {
        /// <inheritdoc />
        public RocksDbContentLocationDatabaseConfiguration(AbsolutePath storeLocation)
        {
            StoreLocation = storeLocation;
        }

        /// <summary>
        /// The directory containing the key-value store.
        /// </summary>
        public AbsolutePath StoreLocation { get; }

        /// <summary>
        /// Testing purposes only. Used to set location to load initial database data from.
        /// NOTE: This disables <see cref="CleanOnInitialize"/>
        /// </summary>
        public AbsolutePath? TestInitialCheckpointPath { get; set; }

        /// <summary>
        /// Gets whether the database is cleared on initialization. (Defaults to true because the standard use case involves restoring from checkpoint after initialization)
        /// </summary>
        public bool CleanOnInitialize { get; set; } = true;

        /// <summary>
        /// Whether the database should be open in read only mode. This will cause all write operations on the DB to
        /// fail.
        /// </summary>
        public bool OpenReadOnly { get; set; } = false;

        /// <summary>
        /// Specifies a opaque value which can be used to determine if database can be reused when <see cref="CleanOnInitialize"/> is false.
        /// </summary>
        /// <remarks>
        /// Do NOT change the default from null. The epoch is not stored by default, so the value is read as null. If
        /// you change this, open -> close -> open will always fail due to non-matching epoch values.
        /// </remarks>
        public string? Epoch { get; set; } = null;

        /// <summary>
        /// Time between full range compactions. These help keep the size of the DB instance down to a minimum.
        /// 
        /// Required because of our workload tends to generate a lot of short-lived entries, which clutter the deeper
        /// levels of the RocksDB LSM tree.
        /// </summary>
        public TimeSpan FullRangeCompactionInterval { get; set; } = Timeout.InfiniteTimeSpan;

        /// <summary>
        /// When <see cref="FullRangeCompactionInterval"/> is enabled, this tunes compactions so that they happen on
        /// small parts of the range instead of the whole thing at once.
        ///
        /// This makes compactions change small subsets of SST files instead of all at once. By doing this, we help
        /// the incremental checkpointing system by not forcing it to transfer a lot of data at once.
        /// </summary>
        public FullRangeCompactionStrategy FullRangeCompactionVariant { get; set; }

        /// <summary>
        /// When doing <see cref="FullRangeCompactionStrategy.ByteIncrements"/>, how much to increment per compaction.
        /// </summary>
        public byte FullRangeCompactionByteIncrementStep { get; set; } = 1;

        /// <summary>
        /// Whether to enable long-term log keeping. Should only be true for servers, where we can keep a lot of logs.
        /// </summary>
        public bool LogsKeepLongTerm { get; set; }

        /// <summary>
        /// Log retention path for the ContentLocationDatabase. When the database is loaded, logs from the old
        /// instance are backed up into a separate folder.
        ///
        /// If null, then the back up is not performed.
        /// </summary>
        public AbsolutePath? LogsBackupPath { get; set; }

        /// <summary>
        /// When logs backup is enabled, the maximum time logs are kept since their creation date.
        /// </summary>
        public TimeSpan LogsRetention { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// Number of keys to buffer on <see cref="RocksDbContentLocationDatabase.EnumerateSortedKeysFromStorage(ContentStore.Tracing.Internal.OperationContext)"/>
        /// </summary>
        public long EnumerateSortedKeysFromStorageBufferSize { get; set; } = 100_000;

        /// <summary>
        /// Number of keys to buffer on <see cref="RocksDbContentLocationDatabase.EnumerateEntriesWithSortedKeysFromStorage(ContentStore.Tracing.Internal.OperationContext, ContentLocationDatabase.EnumerationFilter, bool)"/>
        /// </summary>
        public long EnumerateEntriesWithSortedKeysFromStorageBufferSize { get; set; } = 100_000;

        /// <summary>
        /// Enable dynamic level target sizes.
        /// 
        /// This helps keep space amplification at a factor of ~1.111 by manipulating the level sizes dynamically.
        /// 
        /// See: https://rocksdb.org/blog/2015/07/23/dynamic-level.html
        /// See: https://rockset.com/blog/how-we-use-rocksdb-at-rockset/ (under Dynamic Level Target Sizes)
        /// See: https://github.com/facebook/rocksdb/wiki/Leveled-Compaction#level_compaction_dynamic_level_bytes-is-true
        /// </summary>
        public bool EnableDynamicLevelTargetSizes { get; set; }

        /// <nodoc />
        public static RocksDbContentLocationDatabaseConfiguration FromDistributedContentSettings(
            DistributedContentSettings settings,
            AbsolutePath databasePath,
            AbsolutePath? logsBackupPath,
            bool logsKeepLongTerm)
        {
            var configuration = new RocksDbContentLocationDatabaseConfiguration(databasePath)
            {
                LogsKeepLongTerm = logsKeepLongTerm,
                UseContextualEntryOperationLogging = settings.UseContextualEntryDatabaseOperationLogging,
                TraceTouches = settings.TraceTouches,
                MetadataGarbageCollectionEnabled = settings.EnableDistributedCache,
                LogsBackupPath = logsBackupPath,
            };

            ApplyIfNotNull(settings.ContentLocationDatabaseGcIntervalMinutes, v => configuration.GarbageCollectionInterval = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(settings.ContentLocationDatabaseGarbageCollectionConcurrent, v => configuration.GarbageCollectionConcurrent = v);
            ApplyEnumIfNotNull<MetadataGarbageCollectionStrategy>(settings.ContentLocationDatabaseMetadataGarbageCollectionStrategy, nameof(settings.ContentLocationDatabaseMetadataGarbageCollectionStrategy), v => configuration.MetadataGarbageCollectionStrategy = v);
            ApplyIfNotNull(settings.ContentLocationDatabaseMetadataGarbageCollectionMaximumSizeMb, v => configuration.MetadataGarbageCollectionMaximumSizeMb = v);
            ApplyIfNotNull(settings.MaximumNumberOfMetadataEntriesToStore, v => configuration.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep = v);

            ApplyIfNotNull(settings.ContentLocationDatabaseOpenReadOnly, v => configuration.OpenReadOnly = v && !settings.IsMasterEligible);

            ApplyIfNotNull(
                settings.FullRangeCompactionIntervalMinutes,
                v =>
                {
                    if (v < 0)
                    {
                        throw new ArgumentException($"`{nameof(settings.FullRangeCompactionIntervalMinutes)}` must be greater than or equal to 0");
                    }

                    if (v > 0)
                    {
                        configuration.FullRangeCompactionInterval = TimeSpan.FromMinutes(v);
                    }
                });

            ApplyEnumIfNotNull<FullRangeCompactionStrategy>(
                settings.FullRangeCompactionVariant,
                nameof(settings.FullRangeCompactionVariant),
                v => configuration.FullRangeCompactionVariant = v);
            ApplyIfNotNull(
                settings.FullRangeCompactionByteIncrementStep,
                v => configuration.FullRangeCompactionByteIncrementStep = v);

            ApplyIfNotNull(settings.ContentLocationDatabaseEnableDynamicLevelTargetSizes, v => configuration.EnableDynamicLevelTargetSizes = v);

            if (settings.ContentLocationDatabaseLogsBackupEnabled)
            {
                configuration.LogsBackupPath = logsBackupPath;
            }
            ApplyIfNotNull(settings.ContentLocationDatabaseLogsBackupRetentionMinutes, v => configuration.LogsRetention = TimeSpan.FromMinutes(v));

            ApplyIfNotNull(settings.ContentLocationDatabaseEnumerateSortedKeysFromStorageBufferSize, v => configuration.EnumerateSortedKeysFromStorageBufferSize = v);
            ApplyIfNotNull(settings.ContentLocationDatabaseEnumerateEntriesWithSortedKeysFromStorageBufferSize, v => configuration.EnumerateEntriesWithSortedKeysFromStorageBufferSize = v);

            return configuration;
        }
    }
}
