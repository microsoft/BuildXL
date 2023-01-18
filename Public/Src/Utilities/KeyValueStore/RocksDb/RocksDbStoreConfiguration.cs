// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using RocksDbSharp;

namespace BuildXL.Engine.Cache.KeyValueStores
{
    /// <summary>
    /// Contains a set of options for RocksDb database.
    /// </summary>
    public record RocksDbStoreConfiguration(string StoreDirectory)
    {
        /// <summary>
        /// The directory containing the key-value store.
        /// </summary>
        public string StoreDirectory { get; } = StoreDirectory;

        /// <summary>
        /// Whether the default column should be key-tracked.
        /// This will create two columns for the same data,
        /// one with just keys and the other with key and value.
        /// </summary>
        public bool DefaultColumnKeyTracked { get; init; }

        /// <summary>
        /// The names of any additional column families in the key-value store.
        /// If no additional column families are provided, all entries will be stored
        /// in the default column.
        /// Column families are analogous to tables in relational databases.
        /// </summary>
        public IEnumerable<string>? AdditionalColumns { get; init; }

        /// <summary>
        /// The names of any additional column families in the key-value store that
        /// should also be key-tracked. This will create two columns for the same data,
        /// one with just keys and the other with key and value.
        /// Column families are analogous to tables in relational databases.
        /// </summary>
        public IEnumerable<string>? AdditionalKeyTrackedColumns { get; init; }

        /// <summary>
        /// Specifies merge operators by column name
        /// </summary>
        public Dictionary<string, MergeOperator> MergeOperators { get; init; } = new Dictionary<string, MergeOperator>();

        /// <summary>
        /// Whether the database should be opened read-only. This prevents modifications and
        /// creating unnecessary metadata files related to write sessions.
        /// </summary>
        public bool ReadOnly { get; init; }

        /// <summary>
        /// If a store already exists at the given directory, whether any columns that mismatch the the columns that were passed into the constructor
        /// should be dropped. This will cause data loss and can only be applied in read-write mode.
        /// </summary>
        public bool DropMismatchingColumns { get; init; }

        /// <summary>
        /// Number of files to keep before deletion when rotating logs.
        /// </summary>
        public ulong? RotateLogsNumFiles { get; init; }

        /// <summary>
        /// Maximum log file size before rotating.
        /// </summary>
        public ulong? RotateLogsMaxFileSizeBytes { get; init; }

        /// <summary>
        /// Maximum log age before rotating.
        /// </summary>
        public TimeSpan? RotateLogsMaxAge { get; init; }

        /// <summary>
        /// Opens RocksDb for bulk data loading.
        /// </summary>
        /// <remarks>
        /// This does the following (see https://github.com/facebook/rocksdb/wiki/RocksDB-FAQ):
        /// 
        ///  1) Uses vector memtable
        ///  2) Sets options.max_background_flushes to at least 4
        ///  3) Disables automatic compaction, sets options.level0_file_num_compaction_trigger, 
        ///     options.level0_slowdown_writes_trigger and options.level0_stop_writes_trigger to very large numbers
        ///     
        /// Note that a manual compaction CompactRange(byte[], byte[], string) needs to 
        /// be triggered afterwards. If not, reads will be extremely slow. Keep in mind that the manual compaction
        /// that should follow will likely take a long time, so this may not be useful for some applications.
        /// </remarks>
        public bool OpenBulkLoad { get; init; }

        /// <summary>
        /// Applies the options mentioned in https://github.com/facebook/rocksdb/wiki/Speed-Up-DB-Open.
        /// </summary>
        public bool FastOpen { get; init; }

        /// <summary>
        /// Enables RocksDb statistics getting dumped to the LOG. Useful only for performance debugging.
        /// </summary>
        public bool EnableStatistics { get; init; }

        /// <summary>
        /// Disables automatic background compactions.
        /// </summary>
        public bool DisableAutomaticCompactions { get; init; }

        /// <summary>
        /// Disabled by default due to performance impact.
        /// 
        /// Disable the write ahead log to reduce disk IO. The write ahead log is used to recover the store on 
        /// crashes, so a crash will lose some writes. Writes will be made in-memory only until the write buffer
        /// size is reached and then they will be flushed to storage files.
        /// 
        /// See: https://github.com/facebook/rocksdb/wiki/Write-Ahead-Log
        /// </summary>
        public bool EnableWriteAheadLog { get; init; }

        /// <summary>
        /// Disabled by default due to performance impact.
        /// 
        /// The DB won't wait for fsync return before acknowledging the write as successful. This affects
        /// correctness, because a write may be ACKd before it is actually on disk, but it is much faster.
        /// </summary>
        public bool EnableFSync { get; init; }

        /// <summary>
        /// Enable dynamic level target sizes.
        /// 
        /// This helps keep space amplification at a factor of ~1.111 by manipulating the level sizes dynamically.
        /// 
        /// See: https://rocksdb.org/blog/2015/07/23/dynamic-level.html
        /// See: https://rockset.com/blog/how-we-use-rocksdb-at-rockset/ (under Dynamic Level Target Sizes)
        /// See: https://github.com/facebook/rocksdb/wiki/Leveled-Compaction#level_compaction_dynamic_level_bytes-is-true
        /// </summary>
        public bool LeveledCompactionDynamicLevelTargetSizes { get; init; }

        /// <summary>
        /// Enable RocksDb compression.
        ///
        /// The same compression algorithm is applied to all column families, across all levels.
        /// </summary>
        public Compression? Compression { get; init; }

        /// <summary>
        /// Whether to use 'SetTotalOrderSeek' option during database enumeration.
        /// </summary>
        /// <remarks>
        /// We noticed that NOT setting this flag during full database scan can yield to weird result when some database entries are not produced during database scan
        /// but still exist in the database.
        /// </remarks>
        public bool UseReadOptionsWithSetTotalOrderSeekInDbEnumeration { get; init; } = true;

        /// <summary>
        /// Whether to use 'SetTotalOrderSeek' option during database garbage collection.
        /// </summary>
        /// <remarks>
        /// We noticed that NOT setting this flag during full database scan can yield to weird result when some database entries are not produced during database scan
        /// but still exist in the database.
        /// </remarks>
        public bool UseReadOptionsWithSetTotalOrderSeekInGarbageCollection { get; init; } = true;

        /// <summary>
        /// Log handler that will be called once rocksdb traces a message.
        /// </summary>
        public LogLineCallback? HandleLogMessage { get; set; }

        /// <nodoc />
        public LogLevel LogLevel { get; set; } = LogLevel.Info;

        /// <nodoc />
        public RocksDbPerformanceConfiguration PerformanceConfiguration { get; set; } = new RocksDbPerformanceConfiguration();

        /// <summary>
        /// Crashes an application if the <see cref="RocksDbStore"/> was not properly disposed.
        /// </summary>
        public bool FailIfNotDisposed { get; set; } = false;
    }

    /// <summary>
    /// A set of performance tuning options for RocksDb.
    /// </summary>
    public record RocksDbPerformanceConfiguration
    {
        /// <summary>
        /// Column family-specific performance options.
        /// </summary>
        public IReadOnlyDictionary<string, ColumnFamilyPerformanceConfiguration>? ColumnFamilyPerformanceSettings { get; init; }

        /// <summary>
        /// Gets and sets the number of background threads for flush and compaction.
        /// </summary>
        /// <remarks>
        /// This option takes precedence over <see cref="BackgroundCompactionCoreMultiplier"/>.
        /// </remarks>
        public int? BackgroundCompactionThreadCount { get; set; }

        /// <summary>
        /// Gets and sets the multiplier to compute the degree of parallelism based on the number of cores.
        /// </summary>
        /// <remarks>
        /// This option is used only if <see cref="BackgroundCompactionThreadCount"/> property is not set explicitly.
        /// </remarks>
        public double BackgroundCompactionCoreMultiplier { get; set; } = 0.5;

        /// <summary>
        /// Gets the degree of parallelism based on either <see cref="BackgroundCompactionCoreMultiplier"/> or <see cref="BackgroundCompactionThreadCount"/>.
        /// </summary>
        public int GetBackgroundCompactionActualThreadCount()
        {
            if (BackgroundCompactionThreadCount != null)
            {
                return BackgroundCompactionThreadCount.Value;
            }

            return (int)(Environment.ProcessorCount * BackgroundCompactionCoreMultiplier);
        }

        /// <summary>
        /// Gets and sets the max number of background compaction jobs submitted to the default LOW priority thread pool.
        /// </summary>
        /// <remarks>
        /// If you're increasing this, also consider increasing the number of background threads as well.
        /// </remarks>
        public int MaxBackgroundCompactions { get; init; } = DefaultMaxBackgroundCompactions;

        /// <nodoc />
        public ulong? DbWriteBufferSize { get; init; } =
#if !PLATFORM_OSX
            null;
#else
            // The memtable uses significant chunks of available system memory on macOS, we increase the number
            // of background flushing threads (low priority) and set the DB write buffer size. This allows for
            // up to 128 MB in memtables across all column families before we flush to disk.
            128 << 20;
#endif

        /// <nodoc />
        public static int DefaultMaxBackgroundCompactions =

#if !PLATFORM_OSX
            Environment.ProcessorCount;
#else
            Environment.ProcessorCount / 4;
#endif

        /// <summary>
        /// Sets the maximum number of concurrent background memtable flush jobs, submitted to the HIGH priority thread pool.
        /// </summary>
        /// <remarks>
        /// If you're increasing this, also consider increasing number of threads in
        /// HIGH priority thread pool. For more information, see
        /// Env::SetBackgroundThreads
        /// Default: 1
        /// </remarks>
        public int MaxBackgroundFlushes { get; init; } = DefaultMaxBackgroundFlushes;

        /// <nodoc />
        public static int DefaultMaxBackgroundFlushes =

#if !PLATFORM_OSX
            1;
#else
            Environment.ProcessorCount / 4;
#endif

        /// <nodoc />
        public int? MaxSubCompactions { get; init; } // Not used for now. Needs to be exposed in RocksDbSharp layer first.

        /// <inheritdoc />
        public override string ToString()
        {
            var result = new StringBuilder();

            PrintMembers(result);

            result.Append($", ActualDegreeOfParallelism = {GetBackgroundCompactionActualThreadCount()}");

            string nullOrEmpty = ColumnFamilyPerformanceSettings is null
                ? "null"
                : (ColumnFamilyPerformanceSettings.Count == 0 ? "Empty" : string.Empty);
            result.Append($"ColumnFamilyPerformanceSettings: {nullOrEmpty}");
            
            if (ColumnFamilyPerformanceSettings is not null)
            {
                foreach (var kvp in ColumnFamilyPerformanceSettings)
                {
                    result.Append($"{kvp.Key}: {kvp.Value}");
                }
            }

            return result.ToString();
        }
    }

    /// <summary>
    /// A set of performance tuning options for column family.
    /// </summary>
    public record ColumnFamilyPerformanceConfiguration
    {
        /// <nodoc />
        public int? ColumnFamilyWriteBufferSize { get; init; } //64Mb by default

        /// <nodoc />
        public int? ColumnFamilyLevel0StopWritesTrigger { get; init; }

        /// <nodoc />
        public int? ColumnFamilyLevel0SlowdownWritesTrigger { get; init; }

        /// <nodoc />
        public int? MaxBytesForLevelBase { get; init; }
    }
}