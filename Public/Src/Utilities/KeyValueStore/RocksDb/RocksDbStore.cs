// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using RocksDbSharp;

namespace BuildXL.Engine.Cache.KeyValueStores
{
    /// <summary>
    /// Persistent key-value store built on <see cref="RocksDb"/>.
    /// Only accessible through <see cref="KeyValueStoreAccessor"/> to enforce exception handling and safe disposal.
    /// </summary>
    public class RocksDbStore : IDisposable
    {
        /// <summary>
        /// The current version of the <see cref="RocksDbStore"/>.
        /// This should increase when something fundamental to the rocksdb settings churn (i.e. compression library,
        /// column family handling, settings that change how data is stored).
        /// </summary>
        public const int Version = 1;

        /// <summary>
        /// The directory containing the key-value store.
        /// </summary>
        private readonly string m_storeDirectory;

        /// <summary>
        /// RocksDb store.
        /// </summary>
        private readonly RocksDb m_store;

        /// <summary>
        /// Gets the underlying database
        /// </summary>
        public RocksDb Database => m_store;

        /// <summary>
        /// Maps from column family name to <see cref="ColumnFamilyInfo"/>.
        /// </summary>
        private readonly Dictionary<string, ColumnFamilyInfo> m_columns;

        /// <summary>
        /// Empty value.
        /// </summary>
        private static readonly byte[] s_emptyValue = Array.Empty<byte>();

        private readonly Snapshot? m_snapshot;

        private readonly ReadOptions? m_readOptions;

        private readonly ColumnFamilyInfo m_defaultColumnFamilyInfo;

        private readonly bool m_openBulkLoad;

        /// <summary>
        /// For <see cref="ColumnFamilies"/> that have <see cref="ColumnFamilyInfo.UseKeyTracking"/> as true,
        /// the suffix to add to the name of the column family to create a corresponding column family
        /// that contains the same keys but with <see cref="s_emptyValue"/>s.
        /// The key column family can be accessed through <see cref="ColumnFamilyInfo.KeyHandle"/>
        /// </summary>
        private const string KeyColumnSuffix = ".Keys";

        /// <summary>
        /// Defines the maximum size of batches written as a part of garbage collection.
        /// Generally, the larger the batch, the faster the garbage collection.
        /// </summary>
        public const int GarbageCollectionBatchSize = 10000;

        /// <summary>
        /// Information related to a specific <see cref="ColumnFamilies"/>.
        /// Column families are analogous to tables in relational databases.
        /// </summary>
        private record struct ColumnFamilyInfo
        {
            /// <summary>
            /// Accessor to the <see cref="RocksDb"/> column.
            /// </summary>
            public ColumnFamilyHandle Handle;

            /// <summary>
            /// Whether the column's keys should be tracked independently
            /// of the value. This allows for existence checks without pulling
            /// the entire value into memory.
            /// </summary>
            public bool UseKeyTracking;

            /// <summary>
            /// Provides access to the column used for tracking keys.
            /// Null if <see cref="UseKeyTracking"/> is false.
            /// </summary>
            public ColumnFamilyHandle? KeyHandle;

            /// <summary>
            /// Options for column family. Need to keep reference to prevent
            /// premature garbage collection since values are called from
            /// native code.
            /// </summary>
            internal ColumnFamilyOptions Options;
        }

        /// <summary>
        /// Default <see cref="RocksDb"/> options being used.
        /// </summary>
        private readonly DefaultOptions m_defaults;

        private readonly RocksDbStoreConfiguration? m_options;

        /// <summary>
        /// Encapsulates <see cref="RocksDb"/> options that should be set.
        /// </summary>
        private struct DefaultOptions
        {
            public DbOptions DbOptions;

            public WriteOptions WriteOptions;

            public Func<string?, ColumnFamilyOptions> CreateColumnFamilyOptions;
        }

        /// <summary>
        /// Provides access to and/or creates a RocksDb persistent key-value store.
        /// </summary>
        public RocksDbStore(RocksDbStoreConfiguration configuration)
        {
            m_storeDirectory = configuration.StoreDirectory;
            m_openBulkLoad = configuration.OpenBulkLoad;

            m_defaults.DbOptions = new DbOptions()
                .SetCreateIfMissing(true)
                .SetCreateMissingColumnFamilies(true)
                // The background compaction threads run in low priority, so they should not hamper the rest of
                // the system. The number of cores in the system is what we want here according to official docs,
                // and we are setting this to the number of logical processors, which may be higher.
                // See: https://github.com/facebook/rocksdb/wiki/RocksDB-Tuning-Guide#parallelism-options
#if !PLATFORM_OSX
                .SetMaxBackgroundCompactions(Environment.ProcessorCount)
                .SetMaxBackgroundFlushes(1)
#else
                // The memtable uses significant chunks of available system memory on macOS, we increase the number
                // of background flushing threads (low priority) and set the DB write buffer size. This allows for
                // up to 128 MB in memtables across all column families before we flush to disk.
                .SetMaxBackgroundCompactions(Environment.ProcessorCount / 4)
                .SetMaxBackgroundFlushes(Environment.ProcessorCount / 4)
                .SetDbWriteBufferSize(128 << 20)
#endif
                .IncreaseParallelism(Environment.ProcessorCount / 2);

            if (configuration.EnableStatistics)
            {
                m_defaults.DbOptions.EnableStatistics();
            }

            if (configuration.OpenBulkLoad)
            {
                m_defaults.DbOptions.PrepareForBulkLoad();
            }

            // Maximum number of information log files
            if (configuration.RotateLogsNumFiles != null)
            {
                m_defaults.DbOptions.SetKeepLogFileNum(configuration.RotateLogsNumFiles.Value);
            }

            // Do not rotate information logs based on file size
            if (configuration.RotateLogsMaxFileSizeBytes != null)
            {
                m_defaults.DbOptions.SetMaxLogFileSize(configuration.RotateLogsMaxFileSizeBytes.Value);
            }

            // How long before we rotate the current information log file
            if (configuration.RotateLogsMaxAge != null)
            {
                m_defaults.DbOptions.SetLogFileTimeToRoll((ulong)configuration.RotateLogsMaxAge.Value.Seconds);
            }

            if (configuration.FastOpen)
            {
                // max_file_opening_threads is defaulted to 16, so no need to update here.
                RocksDbSharp.Native.Instance.rocksdb_options_set_skip_stats_update_on_db_open(m_defaults.DbOptions.Handle, true);
            }

            if (configuration.DisableAutomaticCompactions)
            {
                m_defaults.DbOptions.SetDisableAutoCompactions(1);
            }

            // A small comment on things tested that did not work:
            //  * SetAllowMmapReads(true) and SetAllowMmapWrites(true) produce a dramatic performance drop
            //  * SetUseDirectReads(true) disables the OS cache, and although that's good for random point lookups,
            //    it produces a dramatic performance drop otherwise.

            m_defaults.WriteOptions = new WriteOptions()
                .DisableWal(configuration.EnableWriteAheadLog ? 0 : 1)
                .SetSync(configuration.EnableFSync);

            var blockBasedTableOptions = new BlockBasedTableOptions()
                // Use a bloom filter to help reduce read amplification on point lookups. 10 bits per key yields a
                // ~1% false positive rate as per the RocksDB documentation. This builds one filter per SST, which
                // means its optimized for not having a key.
                .SetFilterPolicy(BloomFilterPolicy.Create(10, false))
                // Use a hash index in SST files to speed up point lookup.
                .SetIndexType(BlockBasedTableIndexType.Hash)
                // Whether to use the whole key or a prefix of it (obtained through the prefix extractor below).
                // Since the prefix extractor is a no-op, better performance is achieved by turning this off (i.e.
                // setting it to true).
                .SetWholeKeyFiltering(true)
                // Changes the format of the sst files we produce to be smaller and more efficient (albeit 
                // backwards incompatible).
                // See: https://rocksdb.org/blog/2019/03/08/format-version-4.html
                // See: https://github.com/facebook/rocksdb/blob/master/include/rocksdb/table.h#L297
                .SetFormatVersion(4);

            m_defaults.CreateColumnFamilyOptions = name =>
            {
                var options = new ColumnFamilyOptions()
#if PLATFORM_OSX
                    // As advised by the official documentation, LZ4 is the preferred compression algorithm, our RocksDB
                    // dynamic library has been compiled to support this on macOS. Fallback to Snappy on other systems (default).
                    .SetCompression(Compression.Lz4)
#endif
                    .SetBlockBasedTableFactory(blockBasedTableOptions)
                    .SetPrefixExtractor(SliceTransform.CreateNoOp())
                    .SetLevelCompactionDynamicLevelBytes(configuration.LeveledCompactionDynamicLevelTargetSizes);

                if (configuration.Compression != null)
                {
                    options.SetCompression(configuration.Compression.Value);
                }

                if (configuration.MergeOperators?.TryGetValue(name ?? ColumnFamilies.DefaultName, out var mergeOperator) == true)
                {
                    options.SetMergeOperator(mergeOperator);
                }

                return options;
            };

            m_columns = new Dictionary<string, ColumnFamilyInfo>();

            // The columns that exist in the store on disk may not be in sync with the columns being passed into the constructor
            HashSet<string> existingColumns;
            try
            {
                existingColumns = new HashSet<string>(RocksDb.ListColumnFamilies(m_defaults.DbOptions, m_storeDirectory));
            }
            catch (RocksDbException)
            {
                // If there is no existing store, an exception will be thrown, ignore it
                existingColumns = new HashSet<string>();
            }

            // In read-only mode, open all existing columns in the store without attempting to validate it against the expected column families
            if (configuration.ReadOnly)
            {
                var columnFamilies = new ColumnFamilies();
                foreach (var name in existingColumns)
                {
                    var options = m_defaults.CreateColumnFamilyOptions(name);
                    m_columns[name] = new ColumnFamilyInfo() { Options = options };
                    columnFamilies.Add(name, options);
                }

                m_store = RocksDb.OpenReadOnly(m_defaults.DbOptions, m_storeDirectory, columnFamilies, errIfLogFileExists: false);
            }
            else
            {
                // For read-write mode, column families may be added, so set up column families schema
                var additionalColumns = configuration.AdditionalColumns ?? CollectionUtilities.EmptyArray<string>();
                var columnsSchema = new HashSet<string>(additionalColumns);

                // Default column
                columnsSchema.Add(ColumnFamilies.DefaultName);

                // For key-tracked column families, create two columns:
                // 1: Normal column of { key : value }
                // 2: Key-tracking column of { key : empty-value }
                if (configuration.DefaultColumnKeyTracked)
                {
                    // To be robust to the RocksDB-selected default column name changing,
                    // just name the default column's key-tracking column KeyColumnSuffix
                    columnsSchema.Add(KeyColumnSuffix);
                }

                var additionalKeyTrackedColumns = configuration.AdditionalKeyTrackedColumns ?? CollectionUtilities.EmptyArray<string>();
                foreach (var name in additionalKeyTrackedColumns)
                {
                    columnsSchema.Add(name);
                    columnsSchema.Add(name + KeyColumnSuffix);
                }

                // Figure out which columns are not part of the schema
                var outsideSchemaColumns = new List<string>(existingColumns.Except(columnsSchema));

                // RocksDB requires all columns in the store to be opened in read-write mode, so merge existing columns
                // with the columns schema that was passed into the constructor
                existingColumns.UnionWith(columnsSchema);

                var columnFamilies = new ColumnFamilies();
                foreach (var name in existingColumns)
                {
                    var options = m_defaults.CreateColumnFamilyOptions(name);
                    m_columns[name] = new ColumnFamilyInfo() { Options = options };
                    columnFamilies.Add(name, options);
                }

                m_store = RocksDb.Open(m_defaults.DbOptions, m_storeDirectory, columnFamilies);

                // Provide an opportunity to update the store to the new column family schema
                if (configuration.DropMismatchingColumns)
                {
                    foreach (var name in outsideSchemaColumns)
                    {
                        m_store.DropColumnFamily(name);
                        existingColumns.Remove(name);
                    }
                }
            }

            var userFacingColumns = existingColumns.Where(name => !name.EndsWith(KeyColumnSuffix));

            foreach (var name in userFacingColumns)
            {
                var isKeyTracked = existingColumns.Contains(name + KeyColumnSuffix);
                m_columns[name] = m_columns.GetOrDefault(name) with
                {
                    Handle = m_store.GetColumnFamily(name),
                    UseKeyTracking = isKeyTracked,
                    KeyHandle = isKeyTracked ? m_store.GetColumnFamily(name + KeyColumnSuffix) : null,
                };
            }

            m_columns.TryGetValue(ColumnFamilies.DefaultName, out m_defaultColumnFamilyInfo);
            m_options = configuration;
        }

        /// <summary>
        /// Gets the column family handle with the given name
        /// </summary>
        public ColumnFamilyHandle GetColumn(string? name)
        {
            return GetColumnFamilyInfo(name).Handle;
        }

        /// <summary>
        /// Constructor to create a snapshot
        /// </summary>
        private RocksDbStore(
            RocksDbStore rocksDbStore)
        {
            m_storeDirectory = rocksDbStore.m_storeDirectory;
            m_defaults = rocksDbStore.m_defaults;
            m_columns = rocksDbStore.m_columns;
            m_store = rocksDbStore.m_store;
            m_snapshot = m_store.CreateSnapshot();
            m_readOptions = new ReadOptions().SetSnapshot(m_snapshot);
        }

        /// <summary>
        /// This should be used when writing to the RocksDbStore to ensure that the
        /// default write option settings are used whenever writes occur.
        /// </summary>
        /// <param name="writeBatch"></param>
        private void WriteInternal(WriteBatch writeBatch)
        {
            m_store.Write(writeBatch, m_defaults.WriteOptions);
        }

        /// <inheritdoc />
        public void Put(string key, string value, string? columnFamilyName = null)
        {
            Put(StringToBytes(key), StringToBytes(value), columnFamilyName);
        }

        /// <inheritdoc />
        public void Put(byte[] key, byte[] value, string? columnFamilyName = null)
        {
            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

            using (var writeBatch = new WriteBatch())
            {
                AddPutOperation(writeBatch, columnFamilyInfo, key, value);
                WriteInternal(writeBatch);
            }
        }

        /// <inheritdoc />
        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, string? columnFamilyName = null)
        {
            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);
            m_store.Put(key, value, columnFamilyInfo.Handle, m_defaults.WriteOptions);
        }

        /// <inheritdoc />
        public void PutMultiple(IEnumerable<(string key, string value, string? columnFamilyName)> entries) =>
            PutMultiple(entries.Select(e => (StringToBytes(e.key), StringToBytes(e.value), e.columnFamilyName)));

        /// <inheritdoc />
        public void PutMultiple(IEnumerable<(byte[] key, byte[] value, string? columnFamilyName)> entries)
        {
            using (var writeBatch = new WriteBatch())
            {
                foreach (var entry in entries)
                {
                    AddPutOperation(writeBatch, GetColumnFamilyInfo(entry.columnFamilyName), entry.key, entry.value);
                }

                WriteInternal(writeBatch);
            }
        }

        /// <inheritdoc />
        public void Merge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, string? columnFamilyName = null)
        {
            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);
            m_store.Merge(key, value, columnFamilyInfo.Handle, m_defaults.WriteOptions);
        }

        /// <inheritdoc />
        public void ApplyBatch<TData>(in TData data, string? columnFamilyName, Action<WriteBatch, TData, ColumnFamilyHandle> apply)
        {
            ColumnFamilyInfo batchColumnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

            using (var writeBatch = new WriteBatch())
            {
                apply(writeBatch, data, batchColumnFamilyInfo.Handle);
                WriteInternal(writeBatch);
            }
        }

        /// <summary>
        /// Adds a put operation for a key to a <see cref="WriteBatch"/>. These are not written
        /// to the store by this function, just added to the <see cref="WriteBatch"/>.
        /// </summary>
        private static void AddPutOperation(WriteBatch writeBatch, ColumnFamilyInfo columnFamilyInfo, byte[] key, byte[] value)
        {
            if (key is null)
            {
                throw new RocksDbSharpException("Attempt to insert key value pair with null key");
            }

            writeBatch.Put(key, (uint)key.Length, value, (uint)value.Length, columnFamilyInfo.Handle);

            if (columnFamilyInfo.UseKeyTracking)
            {
                writeBatch.Put(key, s_emptyValue, columnFamilyInfo.KeyHandle);
            }
        }

        /// <nodoc />
        public void ApplyBatch(IEnumerable<KeyValuePair<byte[], byte[]?>> map, string? columnFamilyName = null)
        {
            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);
            using (var writeBatch = new WriteBatch())
            {
                foreach (var keyValuePair in map)
                {
                    if (keyValuePair.Value == null)
                    {
                        AddDeleteOperation(writeBatch, columnFamilyInfo, keyValuePair.Key);
                    }
                    else
                    {
                        AddPutOperation(writeBatch, columnFamilyInfo, keyValuePair.Key, keyValuePair.Value);
                    }
                }
                WriteInternal(writeBatch);
            }
        }

        /// <nodoc />
        public void ApplyBatch(IEnumerable<KeyValuePair<string, string?>> map, string? columnFamilyName = null)
        {
            ApplyBatch(map.Select(kvp => new KeyValuePair<byte[], byte[]?>(StringToBytes(kvp.Key), StringToBytes(kvp.Value))), columnFamilyName);
        }

        /// <inheritdoc />
        public bool TryGetValue(string key, [NotNullWhen(true)] out string? value, string? columnFamilyName = null)
        {
            if (key is null)
            {
                throw new RocksDbSharpException("Attempt to fetch key value pair with empty key");
            }

            bool keyFound = TryGetValue(StringToBytes(key), out var valueInBytes, columnFamilyName);
            value = BytesToString(valueInBytes);
            return keyFound;
        }

        /// <inheritdoc />
        public bool TryGetValue(ReadOnlySpan<byte> key, [NotNullWhen(true)] out byte[]? value, string? columnFamilyName = null)
        {
            value = m_store.Get(key, GetColumnFamilyInfo(columnFamilyName).Handle, readOptions: m_readOptions);
            return value != null;
        }

        /// <inheritdoc />
        public long TryReadValue(ReadOnlySpan<byte> key, Span<byte> valueBuffer, string? columnFamilyName = null)
        {
            return m_store.Get(key, valueBuffer, GetColumnFamilyInfo(columnFamilyName).Handle, readOptions: m_readOptions);
        }

        /// <summary>
        /// Gets the pinned value for a given <paramref name="key"/>.
        /// </summary>
        public bool TryGetPinnableValue(ReadOnlySpan<byte> key, [NotNullWhen(true)] out RocksDbPinnableSpan? value, string? columnFamilyName = null)
        {
            value = m_store.UnsafeGetPinnable(key, GetColumnFamilyInfo(columnFamilyName).Handle, readOptions: m_readOptions);
            return value != null;
        }

        /// <inheritdoc />
        public void Remove(string key, string? columnFamilyName = null)
        {
            if (key is null)
            {
                throw new RocksDbSharpException("Attempt to remove an empty key");
            }

            Remove(StringToBytes(key), columnFamilyName);
        }

        /// <inheritdoc />
        public void Remove(byte[] key, string? columnFamilyName = null)
        {
            if (key is null)
            {
                throw new RocksDbSharpException("Attempt to remove an empty key");
            }

            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

            using (var writeBatch = new WriteBatch())
            {
                AddDeleteOperation(writeBatch, columnFamilyInfo, key);
                WriteInternal(writeBatch);
            }
        }

        /// <inheritdoc />
        public void Remove(ReadOnlySpan<byte> key, string? columnFamilyName = null)
        {
            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

            using (var writeBatch = new WriteBatch())
            {
                AddDeleteOperation(writeBatch, columnFamilyInfo, key);
                WriteInternal(writeBatch);
            }
        }

        /// <inheritdoc />
        public void RemoveBatch(IEnumerable<string> keys, IEnumerable<string?>? columnFamilyNames = null)
        {
            RemoveBatch(keys, key => StringToBytes(key), columnFamilyNames: columnFamilyNames);
        }

        /// <inheritdoc />
        public void RemoveBatch(IEnumerable<byte[]> keys, IEnumerable<string?>? columnFamilyNames = null)
        {
            RemoveBatch(keys, key => key, columnFamilyNames: columnFamilyNames);
        }

        private void RemoveBatch<TKey>(IEnumerable<TKey> keys, Func<TKey, byte[]> convertKey, IEnumerable<string?>? columnFamilyNames = null)
        {
            var columnsInfo = new List<ColumnFamilyInfo>();
            if (columnFamilyNames == null)
            {
                columnsInfo.Add(GetColumnFamilyInfo(null));
            }
            else
            {
                foreach (var column in columnFamilyNames)
                {
                    columnsInfo.Add(GetColumnFamilyInfo(column));
                }
            }

            using (var writeBatch = new WriteBatch())
            {
                foreach (var key in keys)
                {
                    var bytesKey = convertKey.Invoke(key);
                    foreach (var columnInfo in columnsInfo)
                    {
                        AddDeleteOperation(writeBatch, columnInfo, bytesKey);
                    }
                }

                WriteInternal(writeBatch);
            }
        }

        /// <summary>
        /// Adds a delete operation for a key to a <see cref="WriteBatch"/>. These are not written
        /// to the store by this function, just added to the <see cref="WriteBatch"/>.
        /// </summary>
        private void AddDeleteOperation(WriteBatch writeBatch, ColumnFamilyInfo columnFamilyInfo, ReadOnlySpan<byte> key)
        {
            writeBatch.Delete(key, columnFamilyInfo.Handle);

            if (columnFamilyInfo.UseKeyTracking)
            {
                writeBatch.Delete(key, columnFamilyInfo.KeyHandle);
            }
        }

        /// <inheritdoc />
        public bool Contains(string key, string? columnFamilyName = null)
        {
            return Contains(StringToBytes(key), columnFamilyName);
        }

        /// <inheritdoc />
        public bool Contains(ReadOnlySpan<byte> key, string? columnFamilyName = null)
        {
            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

            if (columnFamilyInfo.UseKeyTracking)
            {
                return m_store.Get(key, columnFamilyInfo.KeyHandle, readOptions: m_readOptions) != null;
            }

            return m_store.Get(key, columnFamilyInfo.Handle, readOptions: m_readOptions) != null;
        }

        /// <inheritdoc />
        public void SaveCheckpoint(string targetDirectory)
        {
            using (var checkpoint = m_store.Checkpoint())
            {
                checkpoint.Save(targetDirectory);
            }
        }

        /// <inheritdoc />
        public GarbageCollectResult GarbageCollect(
            Func<string, bool> canCollect,
            string? columnFamilyName = null,
            IEnumerable<string?>? additionalColumnFamilies = null,
            CancellationToken cancellationToken = default,
            string? startValue = null)
        {
            return GarbageCollect(
                // BytesToString 
                canCollect: (byte[] key) => canCollect(BytesToString(key)),
                columnFamilyName: columnFamilyName,
                additionalColumnFamilies: additionalColumnFamilies,
                cancellationToken: cancellationToken,
                startValue: StringToBytes(startValue));
        }

        /// <inheritdoc />
        public GarbageCollectResult GarbageCollect(
            Func<byte[], bool> canCollect,
            string? columnFamilyName = null,
            IEnumerable<string?>? additionalColumnFamilies = null,
            CancellationToken cancellationToken = default,
            byte[]? startValue = null)
        {
            return GarbageCollectByKeyValue(i => canCollect(i.Key().ToArray()), columnFamilyName, additionalColumnFamilies, cancellationToken, startValue);
        }

        /// <inheritdoc />
        public GarbageCollectResult GarbageCollectByKeyValue(
            Func<Iterator, bool> canCollect,
            string? columnFamilyName = null,
            IEnumerable<string?>? additionalColumnFamilies = null,
            CancellationToken cancellationToken = default,
            byte[]? startValue = null)
        {
            var gcResult = new GarbageCollectResult
            {
                BatchSize = GarbageCollectionBatchSize,
                ReachedEnd = false,
            };

            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

            var columnFamilyHandleToUse = columnFamilyInfo.Handle;

            // According to RocksDB documentation, an iterator always loads both the key
            // and the value. To avoid this, when possible, we use the key-tracked column with just keys
            // and empty values and use that for eviction to avoid the load cost of full content.
            if (columnFamilyInfo.UseKeyTracking)
            {
                columnFamilyHandleToUse = columnFamilyInfo.KeyHandle;
            }

            var keysToRemove = new List<byte[]>();
            var primaryColumn = new string?[] { columnFamilyName };
            var columnsToUse = additionalColumnFamilies == null ? primaryColumn : additionalColumnFamilies.Concat(primaryColumn).ToArray();

            using (Iterator iterator = NewIteratorForGarbageCollection(columnFamilyHandleToUse))
            {
                if (startValue != null)
                {
                    iterator.Seek(startValue);
                }
                else
                {
                    iterator.SeekToFirst();
                }

                gcResult.ReachedEnd = !iterator.Valid();
                while (!gcResult.ReachedEnd && !cancellationToken.IsCancellationRequested)
                {
                    gcResult.TotalCount++;
                    bool canCollectResult = canCollect(iterator);

                    if (canCollectResult)
                    {
                        var bytesKey = iterator.Key();
                        keysToRemove.Add(bytesKey.ToArray());
                    }

                    iterator.Next();
                    gcResult.ReachedEnd = !iterator.Valid();

                    if (keysToRemove.Count == GarbageCollectionBatchSize
                        || (gcResult.ReachedEnd && keysToRemove.Count > 0))
                    {
                        removeKeys();
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested && keysToRemove.Count > 0)
            {
                // Removing the accumulated keys if the iteration was canceled.
                removeKeys();
            }

            gcResult.Canceled = cancellationToken.IsCancellationRequested;
            return gcResult;

            void removeKeys()
            {
                var startTime = TimestampUtilities.Timestamp;
                // Remove the key across all specified columns
                RemoveBatch(keysToRemove, columnFamilyNames: columnsToUse);

                var duration = TimestampUtilities.Timestamp - startTime;

                if (duration > gcResult.MaxBatchEvictionTime)
                {
                    gcResult.MaxBatchEvictionTime = duration;
                }

                gcResult.LastKey = keysToRemove.Last();
                gcResult.RemovedCount += keysToRemove.Count;
                keysToRemove.Clear();
            }
        }

        private Iterator NewIteratorForGarbageCollection(ColumnFamilyHandle? columnFamilyHandleToUse)
        {
            var readOptions = m_readOptions ?? ((m_options == null || m_options.UseReadOptionsWithSetTotalOrderSeekInGarbageCollection)
                ? new ReadOptions().SetTotalOrderSeek(true)
                : null);

            return m_store.NewIterator(columnFamilyHandleToUse, readOptions);
        }

        /// <inheritdoc />
        public GarbageCollectResult GarbageCollect(Func<byte[], byte[], bool> canCollect, string? columnFamilyName = null, CancellationToken cancellationToken = default, byte[]? startValue = null)
        {
            var gcResult = new GarbageCollectResult()
            {
                // The implementation below ignores batching and removes keys one by one
                BatchSize = 1,
                ReachedEnd = false,
            };

            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

            var columnFamilyHandleToUse = columnFamilyInfo.Handle;

            using (var iterator = NewIteratorForGarbageCollection(columnFamilyHandleToUse))
            {
                if (startValue != null)
                {
                    iterator.Seek(startValue);
                }
                else
                {
                    iterator.SeekToFirst();
                }

                gcResult.ReachedEnd = !iterator.Valid();
                while (!gcResult.ReachedEnd && !cancellationToken.IsCancellationRequested)
                {
                    var startTime = TimestampUtilities.Timestamp;
                    gcResult.TotalCount++;
                    var bytesKey = iterator.Key().ToArray();
                    var bytesValue = iterator.Value().ToArray();

                    gcResult.LastKey = bytesKey;
                    if (canCollect(bytesKey, bytesValue))
                    {
                        Remove(bytesKey, columnFamilyName);
                        gcResult.RemovedCount++;
                    }

                    iterator.Next();
                    gcResult.ReachedEnd = !iterator.Valid();

                    var duration = TimestampUtilities.Timestamp - startTime;
                    if (duration > gcResult.MaxBatchEvictionTime)
                    {
                        gcResult.MaxBatchEvictionTime = duration;
                    }
                }
            }

            gcResult.Canceled = cancellationToken.IsCancellationRequested;
            return gcResult;
        }

        [return: NotNullIfNotNull(parameterName: "str")]
        private byte[]? StringToBytes(string? str)
        {
            if (str == null)
            {
                return null;
            }

            return Encoding.UTF8.GetBytes(str);
        }

        [return: NotNullIfNotNull(parameterName: "bytes")]
        private string? BytesToString(byte[]? bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            return Encoding.UTF8.GetString(bytes);
        }

        private ColumnFamilyInfo GetColumnFamilyInfo(string? columnFamilyName)
        {
            if (columnFamilyName == null && m_defaultColumnFamilyInfo.Handle != null)
            {
                return m_defaultColumnFamilyInfo;
            }

            return GetColumnFamilyInfoSlow(columnFamilyName);
        }

        private ColumnFamilyInfo GetColumnFamilyInfoSlow(string? columnFamilyName)
        {
            columnFamilyName ??= ColumnFamilies.DefaultName;

            if (m_columns.TryGetValue(columnFamilyName, out var result) 
                && result.Handle.Handle != IntPtr.Zero)
            {
                return result;
            }

            throw new KeyNotFoundException($"The given column family '{columnFamilyName}' does not exist.");
        }

        /// <summary>
        /// Disposes.
        /// </summary>
        public void Dispose()
        {
            if (m_snapshot == null)
            {
                // The db instance was opened in bulk load mode. Issue a manual compaction on Dispose.
                // See https://github.com/facebook/rocksdb/wiki/RocksDB-FAQ for more details
                if (m_openBulkLoad)
                {
                    foreach (var columnFamilyName in m_columns.Keys)
                    {
                        CompactRange((byte[]?)null, null, columnFamilyName);
                    }
                }

                m_store.Dispose();
            }
            else
            {
                // If this is snapshot of another store, only dispose the m_snapshot object.
                // The m_store object cannot be disposed more than once.
                // We would wait for the non-snapshot dbstore to dispose the m_store object.
                m_snapshot.Dispose();
            }
        }

        /// <summary>
        /// Clones a new <see cref="RocksDbStore"/> by creating a snapshot
        /// </summary>
        public RocksDbStore CreateSnapshot()
        {
            return new RocksDbStore(this);
        }

        /// <inheritdoc />
        /// <remarks>
        /// See https://github.com/facebook/rocksdb/blob/master/include/rocksdb/db.h#L547 for a list of all the
        /// valid properties.
        /// </remarks>
        public string GetProperty(string propertyName, string? columnFamilyName = null)
        {
            return m_store.GetProperty(propertyName, GetColumnFamilyInfo(columnFamilyName).Handle);
        }

        /// <summary>
        /// A callback-based for push-based prefix lookup (prefix search) implementation.
        /// </summary>
        /// <remarks>
        /// When a callback returns false, the search is done.
        /// </remarks>
        public delegate bool ObserveKeyValuePairCallback<in TState>(TState state, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);

        /// <summary>
        /// A callback-based for push-based prefix lookup (prefix search) implementation.
        /// </summary>
        /// <remarks>
        /// When a callback returns false, the search is done.
        /// </remarks>
        public delegate bool ObserveKeyCallback<in TState>(TState state, ReadOnlySpan<byte> key);

        /// <nodoc />
        public void PrefixLookup<TState>(TState state, ReadOnlySpan<byte> prefix, string? columnFamilyName, ObserveKeyValuePairCallback<TState> observeCallback)
        {
            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);
            var readOptions = new ReadOptions().SetTotalOrderSeek(true);

            using (var iterator = m_store.NewIterator(columnFamilyInfo.Handle, readOptions))
            {
                if (prefix.Length == 0)
                {
                    iterator.SeekToFirst();
                }
                else
                {
                    iterator.Seek(prefix);
                }

                while (iterator.Valid())
                {
                    var key = iterator.Key();
                    if (!StartsWith(prefix, key))
                    {
                        break;
                    }

                    if (!observeCallback(state, key, iterator.Value()))
                    {
                        break;
                    }

                    iterator.Next();
                }
            }
        }

        /// <nodoc />
        public void PrefixKeyLookup<TState>(TState state, ReadOnlySpan<byte> prefix, string? columnFamilyName, ObserveKeyCallback<TState> observeCallback)
        {
            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);
            var readOptions = new ReadOptions().SetTotalOrderSeek(true);

            using (var iterator = m_store.NewIterator(columnFamilyInfo.Handle, readOptions))
            {
                if (prefix.Length == 0)
                {
                    iterator.SeekToFirst();
                }
                else
                {
                    iterator.Seek(prefix);
                }

                while (iterator.Valid())
                {
                    var key = iterator.Key();
                    if (!StartsWith(prefix, key))
                    {
                        break;
                    }

                    if (!observeCallback(state, key))
                    {
                        break;
                    }

                    iterator.Next();
                }
            }
        }

        /// <nodoc />
        public IEnumerable<KeyValuePair<byte[], byte[]>> PrefixSearch(byte[]? prefix, string? columnFamilyName = null)
        {
            // TODO(jubayard): there are multiple ways to implement prefix search in RocksDB. In particular, they
            // have a prefix seek API (see: https://github.com/facebook/rocksdb/wiki/Prefix-Seek-API-Changes ).
            // However, it requires certain options to be set on the column family, so it could be problematic. We
            // just use a simpler way. Could change if any performance issues arise out of this decision.
            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);
            var readOptions = new ReadOptions().SetTotalOrderSeek(true);

            using (var iterator = m_store.NewIterator(columnFamilyInfo.Handle, readOptions))
            {
                if (prefix == null || prefix.Length == 0)
                {
                    iterator.SeekToFirst();
                }
                else
                {
                    iterator.Seek(prefix);
                }

                while (iterator.Valid())
                {
                    var key = iterator.Key().ToArray();
                    if (!StartsWith(prefix, key))
                    {
                        break;
                    }

                    yield return new KeyValuePair<byte[], byte[]>(key, iterator.Value().ToArray());

                    iterator.Next();
                }
            }
        }

        /// <inheritdoc />
        public IterateDbContentResult IterateDbContent(
            Action<Iterator> onNextItem,
            string? columnFamilyName,
            byte[]? startValue,
            CancellationToken token)
        {
            return IterateDbContentCore(
                iterator =>
                {
                    if (startValue == null || startValue.Length == 0)
                    {
                        iterator.SeekToFirst();
                    }
                    else
                    {
                        iterator.Seek(startValue);
                    }
                },
                onNextItem,
                columnFamilyName,
                token);
        }

        /// <inheritdoc />
        public IterateDbContentResult IterateDbContent(
            Action<Iterator> onNextItem,
            string? columnFamilyName,
            string? startValue,
            CancellationToken token)
        {
            return IterateDbContentCore(
                iterator =>
                {
                    if (string.IsNullOrEmpty(startValue))
                    {
                        iterator.SeekToFirst();
                    }
                    else
                    {
                        iterator.Seek(startValue);
                    }
                },
                onNextItem,
                columnFamilyName,
                token);
        }

        private IterateDbContentResult IterateDbContentCore(
            Action<Iterator> seekIfNeeded,
            Action<Iterator> onNextItem,
            string? columnFamilyName,
            CancellationToken token)
        {
            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

            var readOptions = (m_options == null || m_options.UseReadOptionsWithSetTotalOrderSeekInDbEnumeration) ? new ReadOptions().SetTotalOrderSeek(true) : null;

            using (var iterator = m_store.NewIterator(columnFamilyInfo.Handle, readOptions))
            {
                seekIfNeeded(iterator);

                while (iterator.Valid() && !token.IsCancellationRequested)
                {
                    onNextItem(iterator);

                    iterator.Next();
                }
            }

            return new IterateDbContentResult() { ReachedEnd = !token.IsCancellationRequested, Canceled = token.IsCancellationRequested, };
        }

        /// <nodoc />
        public static bool StartsWith(byte[]? prefix, byte[] key)
        {
            if (prefix == null || prefix.Length == 0)
            {
                return true;
            }

            if (prefix.Length > key.Length)
            {
                return false;
            }

            for (int i = 0; i < prefix.Length; ++i)
            {
                if (key[i] != prefix[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <nodoc />
        public static bool StartsWith(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> key)
        {
            if (key.Length == 0)
            {
                return true;
            }

            if (prefix.Length > key.Length)
            {
                return false;
            }

            var prefixToSearch = key.Slice(start: 0, length: prefix.Length);
            return prefix.SequenceEqual(prefixToSearch);
        }

        /// <inheritdoc />
        public void CompactRange(string? start, string? limit, string? columnFamilyName = null)
        {
            CompactRange(StringToBytes(start), StringToBytes(limit), columnFamilyName);
        }

        /// <inheritdoc />
        public void CompactRange(byte[]? start, byte[]? limit, string? columnFamilyName = null)
        {
            var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

            // We need to use the instance directly because RocksDbSharp does not handle the case where full range compaction is desired.
            RocksDbSharp.Native.Instance.rocksdb_compact_range_cf(
                db: m_store.Handle,
                column_family: columnFamilyInfo.Handle.Handle,
                start_key: start,
                start_key_len: new UIntPtr((ulong)(start?.GetLongLength(0) ?? 0)),
                limit_key: limit,
                limit_key_len: new UIntPtr((ulong)(limit?.GetLongLength(0) ?? 0)));
        }

        /// <nodoc />
        public void CreateColumnFamily(string columnFamily)
        {
            var options = m_columns.TryGetValue(columnFamily, out var info) ? info.Options : m_defaults.CreateColumnFamilyOptions(columnFamily);
            var handle = m_store.CreateColumnFamily(options, columnFamily);
            m_columns[columnFamily] = info with { Handle = handle, Options = options };
        }

        /// <nodoc />
        public void DropColumnFamily(string columnFamily)
        {
            var family = GetColumnFamilyInfo(columnFamily);
            m_columns.Remove(columnFamily);
            m_store.DropColumnFamily(columnFamily);
            ((IDisposable)family.Handle).Dispose();
        }

        /// <nodoc />
        public SstFileWriter CreateSstFileWriter(string path, string? columnFamilyName = null)
        {
            var sstFileWriter = new SstFileWriter(ioOptions: m_defaults.CreateColumnFamilyOptions(columnFamilyName));
            sstFileWriter.Open(path);
            return sstFileWriter;
        }
    } // RocksDbStore
}
