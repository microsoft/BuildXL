// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.NuCache.InMemory;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.Host.Configuration;
using RocksDbSharp;
using static BuildXL.Utilities.ConfigurationHelper;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
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
        /// Whether to log evicted metadata
        /// </summary>
        public bool MetadataGarbageCollectionLogEnabled { get; set; } = false;

        /// <summary>
        /// Maximum allowed size of the Metadata column family.
        /// </summary>
        public double MetadataGarbageCollectionMaximumSizeMb { get; set; } = 20_000;

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
        /// Specifies whether the context operation guid is used when logging entry operations
        /// </summary>
        public bool TraceOperations { get; set; } = true;

        /// <summary>
        /// Ges or sets log level from RocksDb emitted to Kusto.
        /// Null - the tracing is off.
        /// </summary>
        public LogLevel? RocksDbTracingLevel { get; set; }

        /// <summary>
        /// Specifies whether to trace touches or not.
        /// Tracing touches is expensive in terms of the amount of traffic to Kusto and in terms of memory traffic.
        /// </summary>
        public bool TraceTouches { get; set; } = true;

        /// <summary>
        /// Specifies whether to trace the cases when the call to SetMachineExistence didn't change the database's state.
        /// </summary>
        public bool TraceNoStateChangeOperations { get; set; } = false;

        /// <summary>
        /// Specifies whether to filter inactive machines when getting the locations information.
        /// </summary>
        /// <remarks>
        /// Historically, the filtering logic was implemented in the database level and not on LocalLocationStore level.
        /// But filtering out inactive machines in the database means that the same logic should be duplicated for the locations obtained from global store.
        /// Plus this approach makes it harder to trace filtered out machines as well.
        /// This flag is used for backwards compatibility reasons and will be removed once the LLS-based filtering is fully rolled out.
        /// </remarks>
        public bool FilterInactiveMachines { get; set; } = true;

        /// <summary>
        /// True if RocksDb merge operators are used for the content.
        /// </summary>
        public bool UseMergeOperators { get; set; }
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
    public class RocksDbContentLocationDatabaseConfiguration : ContentLocationDatabaseConfiguration
    {
        /// <inheritdoc />
        public RocksDbContentLocationDatabaseConfiguration(AbsolutePath storeLocation) => StoreLocation = storeLocation;

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
        /// Whether to use 'SetTotalOrderSeek' option during database enumeration.
        /// </summary>
        /// <remarks>
        /// Setting this flag is important in order to get the correct behavior for content enumeration of the database.
        /// When the prefix extractor is used by calling SetIndexType(BlockBasedTableIndexType.Hash) and SetPrefixExtractor(SliceTransform.CreateNoOp())
        /// then the full database enumeration may return already removed keys or the previous version for some values.
        ///
        /// Not setting this flag was causing issues during reconciliation because the database enumeration was producing values for already removed keys
        /// and some keys were missing.
        /// </remarks>
        public bool UseReadOptionsWithSetTotalOrderSeekInDbEnumeration { get; set; } = true;

        /// <summary>
        /// Whether to use 'SetTotalOrderSeek' option during database garbage collection.
        /// </summary>
        /// <remarks>
        /// See the remarks section for <see cref="UseReadOptionsWithSetTotalOrderSeekInDbEnumeration"/>.
        /// </remarks>
        public bool UseReadOptionsWithSetTotalOrderSeekInGarbageCollection { get; set; } = true;

        /// <summary>
        /// Whether to use RocksDb merge operator for content location entries.
        /// </summary>
        public bool UseMergeOperatorForContentLocations { get; set; } = false;

        /// <nodoc />
        public RocksDbPerformanceSettings? RocksDbPerformanceSettings { get; set; } = null;

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
                LogsBackupPath = logsBackupPath,
            };

            configuration.RocksDbPerformanceSettings = settings.RocksDbPerformanceSettings;

            ApplyIfNotNull(settings.ContentLocationDatabaseRocksDbTracingLevel, v => configuration.RocksDbTracingLevel = (LogLevel)v);
            ApplyIfNotNull(settings.TraceStateChangeDatabaseOperations, v => configuration.TraceOperations = v);
            ApplyIfNotNull(settings.TraceNoStateChangeDatabaseOperations, v => configuration.TraceNoStateChangeOperations = v);

            ApplyIfNotNull(settings.ContentLocationDatabaseGcIntervalMinutes, v => configuration.GarbageCollectionInterval = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(settings.ContentLocationDatabaseGarbageCollectionConcurrent, v => configuration.GarbageCollectionConcurrent = v);
            ApplyIfNotNull(settings.ContentLocationDatabaseMetadataGarbageCollectionMaximumSizeMb, v => configuration.MetadataGarbageCollectionMaximumSizeMb = v);
            ApplyIfNotNull(settings.ContentLocationDatabaseMetadataGarbageCollectionLogEnabled, v => configuration.MetadataGarbageCollectionLogEnabled = v);

            ApplyIfNotNull(settings.ContentLocationDatabaseOpenReadOnly, v => configuration.OpenReadOnly = (v && !settings.IsMasterEligible));
            ApplyIfNotNull(settings.UseMergeOperatorForContentLocations, v => configuration.UseMergeOperatorForContentLocations = v);

            if (settings.ContentLocationDatabaseLogsBackupEnabled)
            {
                configuration.LogsBackupPath = logsBackupPath;
            }
            
            ApplyIfNotNull(settings.ContentLocationDatabaseLogsBackupRetentionMinutes, v => configuration.LogsRetention = TimeSpan.FromMinutes(v));

            ApplyIfNotNull(settings.ContentLocationDatabaseEnumerateSortedKeysFromStorageBufferSize, v => configuration.EnumerateSortedKeysFromStorageBufferSize = v);
            ApplyIfNotNull(settings.ContentLocationDatabaseEnumerateEntriesWithSortedKeysFromStorageBufferSize, v => configuration.EnumerateEntriesWithSortedKeysFromStorageBufferSize = v);

            ApplyIfNotNull(settings.ContentLocationDatabaseUseReadOptionsWithSetTotalOrderSeekInDbEnumeration, v => configuration.UseReadOptionsWithSetTotalOrderSeekInDbEnumeration = v);
            ApplyIfNotNull(settings.ContentLocationDatabaseUseReadOptionsWithSetTotalOrderSeekInGarbageCollection, v => configuration.UseReadOptionsWithSetTotalOrderSeekInGarbageCollection = v);

            // If filtering is not happening on LLS layer, then it should still happen here.
            ApplyIfNotNull(settings.ShouldFilterInactiveMachinesInLocalLocationStore, v => configuration.FilterInactiveMachines = !v);

            return configuration;
        }
    }
}
