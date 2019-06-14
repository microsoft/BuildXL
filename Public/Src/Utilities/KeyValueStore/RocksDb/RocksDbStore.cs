// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using RocksDbSharp;

namespace BuildXL.Engine.Cache.KeyValueStores
{
    public partial class KeyValueStoreAccessor : IDisposable
    {
        /// <summary>
        /// Persistent key-value store built on <see cref="RocksDbSharp.RocksDb"/>.
        /// Only accessible through <see cref="KeyValueStoreAccessor"/> to enforce exception handling and safe disposal.
        /// </summary>
        private class RocksDbStore : IBuildXLKeyValueStore, IDisposable
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
            /// Maps from column family name to <see cref="ColumnFamilyInfo"/>.
            /// </summary>
            private readonly Dictionary<string, ColumnFamilyInfo> m_columns;

            /// <summary>
            /// Empty value.
            /// </summary>
            private static readonly byte[] s_emptyValue = new byte[0];

            private readonly Snapshot m_snapshot;

            private readonly ReadOptions m_readOptions;

            private readonly ColumnFamilyInfo m_defaultColumnFamilyInfo;

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
            private struct ColumnFamilyInfo
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
                public ColumnFamilyHandle KeyHandle;
            }

            /// <summary>
            /// Default <see cref="RocksDb"/> options being used.
            /// </summary>
            private readonly DefaultOptions m_defaults;

            /// <summary>
            /// Encapsulates <see cref="RocksDb"/> options that should be set.
            /// </summary>
            private struct DefaultOptions
            {
                public DbOptions DbOptions;

                public WriteOptions WriteOptions;

                public ColumnFamilyOptions ColumnFamilyOptions;
            }

            /// <summary>
            /// Provides access to and/or creates a RocksDb persistent key-value store.
            /// </summary>
            /// <param name="storeDirectory">
            /// The directory containing the key-value store.
            /// </param>
            /// <param name="defaultColumnKeyTracked">
            /// Whether the default column should be key-tracked. 
            /// This will create two columns for the same data,
            /// one with just keys and the other with key and value.
            /// </param>
            /// <param name="additionalColumns">
            /// The names of any additional column families in the key-value store.
            /// If no additional column families are provided, all entries will be stored
            /// in the default column.
            /// Column families are analogous to tables in relational databases.
            /// </param>
            /// <param name="additionalKeyTrackedColumns">
            /// The names of any additional column families in the key-value store that
            /// should also be key-tracked. This will create two columns for the same data,
            /// one with just keys and the other with key and value.
            /// Column families are analogous to tables in relational databases.
            /// </param>
            /// <param name="readOnly">
            /// Whether the database should be opened read-only. This prevents modifications and
            /// creating unnecessary metadata files related to write sessions.
            /// </param>
            /// <param name="dropMismatchingColumns">
            /// If a store already exists at the given directory, whether any columns that mismatch the the columns that were passed into the constructor
            /// should be dropped. This will cause data loss and can only be applied in read-write mode.
            /// </param>
            public RocksDbStore(
                string storeDirectory,
                bool defaultColumnKeyTracked = false,
                IEnumerable<string> additionalColumns = null,
                IEnumerable<string> additionalKeyTrackedColumns = null,
                bool readOnly = false,
                bool dropMismatchingColumns = false)
            {
                m_storeDirectory = storeDirectory;

                m_defaults.DbOptions = new DbOptions()
                    .SetCreateIfMissing(true)
                    .SetCreateMissingColumnFamilies(true)
                    // The background compaction threads run in low priority, so they should not hamper the rest of
                    // the system. The number of cores in the system is what we want here according to official docs,
                    // and we are setting this to the number of logical processors, which may be higher.
                    .SetMaxBackgroundCompactions(Environment.ProcessorCount)
                    .SetMaxBackgroundFlushes(1)
                    .IncreaseParallelism(Environment.ProcessorCount / 2)
                    // Ensure we have performance statistics for profiling
                    .EnableStatistics();

                // A small comment on things tested that did not work:
                //  * SetAllowMmapReads(true) and SetAllowMmapWrites(true) produce a dramatic performance drop
                //  * SetUseDirectReads(true) disables the OS cache, and although that's good for random point lookups,
                //    it produces a dramatic performance drop otherwise.

                m_defaults.WriteOptions = new WriteOptions()
                    // Disable the write ahead log to reduce disk IO. The write ahead log
                    // is used to recover the store on crashes, so a crash will lose some writes.
                    // Writes will be made in-memory only until the write buffer size
                    // is reached and then they will be flushed to storage files.
                    .DisableWal(1)
                    // This option is off by default, but just making sure that the C# wrapper 
                    // doesn't change anything. The idea is that the DB won't wait for fsync to
                    // return before acknowledging the write as successful. This affects 
                    // correctness, because a write may be ACKd before it is actually on disk,
                    // but it is much faster.
                    .SetSync(false);


                var blockBasedTableOptions = new BlockBasedTableOptions()
                    // Use a bloom filter to help reduce read amplification on point lookups. 10 bits per key yields a
                    // ~1% false positive rate as per the RocksDB documentation. This builds one filter per SST, which
                    // means its optimized for not having a key.
                    .SetFilterPolicy(BloomFilterPolicy.Create(10, false))
                    // Use a hash index in SST files to speed up point lookup.
                    .SetIndexType(BlockBasedTableIndexType.HashSearch)
                    // Whether to use the whole key or a prefix of it (obtained through the prefix extractor below).
                    // Since the prefix extractor is a no-op, better performance is achieved by turning this off (i.e.
                    // setting it to true).
                    .SetWholeKeyFiltering(true);

                m_defaults.ColumnFamilyOptions = new ColumnFamilyOptions()
                    .SetBlockBasedTableFactory(blockBasedTableOptions)
                    .SetPrefixExtractor(SliceTransform.CreateNoOp());

                m_columns = new Dictionary<string, ColumnFamilyInfo>();

                additionalColumns = additionalColumns ?? CollectionUtilities.EmptyArray<string>();
                additionalKeyTrackedColumns = additionalKeyTrackedColumns ?? CollectionUtilities.EmptyArray<string>();

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
                if (readOnly)
                {
                    var columnFamilies = new ColumnFamilies();
                    foreach (var name in existingColumns)
                    {
                        columnFamilies.Add(name, m_defaults.ColumnFamilyOptions);
                    }

                    m_store = RocksDb.OpenReadOnly(m_defaults.DbOptions, m_storeDirectory, columnFamilies, errIfLogFileExists: false);
                }
                else
                {
                    // For read-write mode, column families may be added, so set up column families schema
                    var columnsSchema = new HashSet<string>(additionalColumns);
                
                    // Default column
                    columnsSchema.Add(ColumnFamilies.DefaultName);

                    // For key-tracked column familiies, create two columns:
                    // 1: Normal column of { key : value }
                    // 2: Key-tracking column of { key : empty-value }
                    if (defaultColumnKeyTracked)
                    {
                        // To be robust to the RocksDB-selected default column name changing,
                        // just name the default column's key-tracking column KeyColumnSuffix
                        columnsSchema.Add(KeyColumnSuffix);
                    }

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
                        columnFamilies.Add(name, m_defaults.ColumnFamilyOptions);
                    }
 
                    m_store = RocksDb.Open(m_defaults.DbOptions, m_storeDirectory, columnFamilies);

                    // Provide an opportunity to update the store to the new column family schema
                    if (dropMismatchingColumns)
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
                    m_columns.Add(name, new ColumnFamilyInfo()
                    {
                        Handle = m_store.GetColumnFamily(name),
                        UseKeyTracking = isKeyTracked,
                        KeyHandle = isKeyTracked ? m_store.GetColumnFamily(name + KeyColumnSuffix) : null,
                    });
                }

                m_columns.TryGetValue(ColumnFamilies.DefaultName, out m_defaultColumnFamilyInfo);
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
            public void Put(string key, string value, string columnFamilyName = null)
            {
                Put(StringToBytes(key), StringToBytes(value), columnFamilyName);
            }

            /// <inheritdoc />
            public void Put(byte[] key, byte[] value, string columnFamilyName = null)
            {
                var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

                using (var writeBatch = new WriteBatch())
                {
                    AddPutOperation(writeBatch, columnFamilyInfo, key, value);
                    WriteInternal(writeBatch);
                }
            }

            /// <summary>
            /// Adds a put operation for a key to a <see cref="WriteBatch"/>. These are not written
            /// to the store by this function, just added to the <see cref="WriteBatch"/>.
            /// </summary>
            private static void AddPutOperation(WriteBatch writeBatch, ColumnFamilyInfo columnFamilyInfo, byte[] key, byte[] value)
            {
                writeBatch.Put(key, (uint)key.Length, value, (uint)value.Length, columnFamilyInfo.Handle);

                if (columnFamilyInfo.UseKeyTracking)
                {
                    writeBatch.Put(key, s_emptyValue, columnFamilyInfo.KeyHandle);
                }
            }

            /// <inheritdoc />
            public void ApplyBatch(IEnumerable<string> keys, IEnumerable<string> values, string columnFamilyName = null)
            {
                ApplyBatch(keys.Select(k => StringToBytes(k)), values.Select(v => StringToBytes(v)), columnFamilyName);
            }

            /// <inheritdoc />
            public void ApplyBatch(IEnumerable<byte[]> keys, IEnumerable<byte[]> values, string columnFamilyName = null)
            {
                var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

                using (var writeBatch = new WriteBatch())
                {
                    foreach (var keyValuePair in keys.Zip(values, (k, v) => (k, v)))
                    {
                        if (keyValuePair.v == null)
                        {
                            AddDeleteOperation(writeBatch, columnFamilyInfo, keyValuePair.k);
                        } else
                        {
                            AddPutOperation(writeBatch, columnFamilyInfo, keyValuePair.k, keyValuePair.v);
                        }
                    }

                    WriteInternal(writeBatch);
                }
            }

            /// <inheritdoc />
            public bool TryGetValue(string key, out string value, string columnFamilyName = null)
            {
                bool keyFound = TryGetValue(StringToBytes(key), out var valueInBytes, columnFamilyName);
                value = BytesToString(valueInBytes);
                return keyFound;
            }

            /// <inheritdoc />
            public bool TryGetValue(byte[] key, out byte[] value, string columnFamilyName = null)
            {
                value = m_store.Get(key, GetColumnFamilyInfo(columnFamilyName).Handle, readOptions: m_readOptions);
                return value != null;
            }

            /// <inheritdoc />
            public void Remove(string key, string columnFamilyName = null)
            {
                Remove(StringToBytes(key), columnFamilyName);
            }

            /// <inheritdoc />
            public void Remove(byte[] key, string columnFamilyName = null)
            {
                var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

                using (var writeBatch = new WriteBatch())
                {
                    AddDeleteOperation(writeBatch, columnFamilyInfo, key);
                    WriteInternal(writeBatch);
                }
            }

            /// <inheritdoc />
            public void RemoveBatch(IEnumerable<string> keys, IEnumerable<string> columnFamilyNames = null)
            {
                RemoveBatch(keys, key => StringToBytes(key), columnFamilyNames: columnFamilyNames);
            }


            /// <inheritdoc />
            public void RemoveBatch(IEnumerable<byte[]> keys, IEnumerable<string> columnFamilyNames = null)
            {
                RemoveBatch(keys, key => key, columnFamilyNames: columnFamilyNames);
            }

            private void RemoveBatch<TKey>(IEnumerable<TKey> keys, Func<TKey, byte[]> convertKey, IEnumerable<string> columnFamilyNames = null)
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
            private void AddDeleteOperation(WriteBatch writeBatch, ColumnFamilyInfo columnFamilyInfo, byte[] key)
            {
                writeBatch.Delete(key, columnFamilyInfo.Handle);

                if (columnFamilyInfo.UseKeyTracking)
                {
                    writeBatch.Delete(key, columnFamilyInfo.KeyHandle);
                }
            }

            /// <inheritdoc />
            public bool Contains(string key, string columnFamilyName = null)
            {
                return Contains(StringToBytes(key), columnFamilyName);
            }

            /// <inheritdoc />
            public bool Contains(byte[] key, string columnFamilyName = null)
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
                string columnFamilyName = null,
                IEnumerable<string> additionalColumnFamilies = null,
                CancellationToken cancellationToken = default,
                string startValue = null)
            {
                return GarbageCollect(
                    canCollect: (key) => canCollect(BytesToString(key)), 
                    columnFamilyName: columnFamilyName, 
                    additionalColumnFamilies: additionalColumnFamilies, 
                    cancellationToken: cancellationToken, 
                    startValue: StringToBytes(startValue));
            }

            /// <inheritdoc />
            public GarbageCollectResult GarbageCollect(
                Func<byte[], bool> canCollect,
                string columnFamilyName = null,
                IEnumerable<string> additionalColumnFamilies = null,
                CancellationToken cancellationToken = default,
                byte[] startValue = null)
            {
                return GarbageCollectByKeyValue(i => canCollect(i.Key()), columnFamilyName, additionalColumnFamilies, cancellationToken, startValue);
            }
            
            /// <inheritdoc />
            public GarbageCollectResult GarbageCollectByKeyValue(
                Func<Iterator, bool> canCollect,
                string columnFamilyName = null,
                IEnumerable<string> additionalColumnFamilies = null,
                CancellationToken cancellationToken = default,
                byte[] startValue = null)
            {
                var gcStats = new GarbageCollectResult
                {
                    BatchSize = GarbageCollectionBatchSize
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
                var primaryColumn = new string[] { columnFamilyName };
                var columnsToUse = additionalColumnFamilies == null ?  primaryColumn : additionalColumnFamilies.Concat(primaryColumn);
                using (Iterator iterator = m_store.NewIterator(columnFamilyHandleToUse, m_readOptions))
                {
                    if (startValue != null)
                    {
                        iterator.Seek(startValue);
                    }
                    else
                    {
                        iterator.SeekToFirst();
                    }

                    bool reachedEnd = !iterator.Valid();
                    while (!reachedEnd && !cancellationToken.IsCancellationRequested)
                    {
                        gcStats.TotalCount++;
                        bool canCollectResult = canCollect(iterator);

                        if (canCollectResult)
                        {
                            var bytesKey = iterator.Key();
                            keysToRemove.Add(bytesKey);
                        }

                        iterator.Next();
                        reachedEnd = !iterator.Valid();

                        if (keysToRemove.Count == GarbageCollectionBatchSize 
                            || (reachedEnd && keysToRemove.Count > 0))
                        {
                            var startTime = TimestampUtilities.Timestamp;
                            // Remove the key across all specified columns
                            RemoveBatch(keysToRemove, columnFamilyNames: columnsToUse);

                            var duration = TimestampUtilities.Timestamp - startTime;

                            if (duration > gcStats.MaxBatchEvictionTime)
                            {
                                gcStats.MaxBatchEvictionTime = duration;
                            }

                            gcStats.LastKey = keysToRemove.Last();
                            gcStats.RemovedCount += keysToRemove.Count;
                            keysToRemove.Clear();
                        }
                    }
                }

                gcStats.Canceled = cancellationToken.IsCancellationRequested;
                return gcStats;
            }

            /// <inheritdoc />
            public GarbageCollectResult GarbageCollect(Func<byte[], byte[], bool> canCollect, string columnFamilyName = null, CancellationToken cancellationToken = default, byte[] startValue = null)
            {
                var gcResult = new GarbageCollectResult();
                // The implementation below ignores batching and removes keys one by one
                gcResult.BatchSize = 1;

                var columnFamilyInfo = GetColumnFamilyInfo(columnFamilyName);

                var columnFamilyHandleToUse = columnFamilyInfo.Handle;

                using (var iterator = m_store.NewIterator(columnFamilyHandleToUse))
                {
                    if (startValue != null)
                    {
                        iterator.Seek(startValue);
                    }
                    else
                    {
                        iterator.SeekToFirst();
                    }

                    while (iterator.Valid() && !cancellationToken.IsCancellationRequested)
                    {
                        var startTime = TimestampUtilities.Timestamp;
                        gcResult.TotalCount++;
                        var bytesKey = iterator.Key();
                        var bytesValue = iterator.Value();

                        gcResult.LastKey = bytesKey;
                        if (canCollect(bytesKey, bytesValue))
                        {
                            Remove(bytesKey, columnFamilyName);
                            gcResult.RemovedCount++;
                        }

                        iterator.Next();

                        var duration = TimestampUtilities.Timestamp - startTime;
                        if (duration > gcResult.MaxBatchEvictionTime)
                        {
                            gcResult.MaxBatchEvictionTime = duration;
                        }
                    }
                }

                return gcResult;
            }

            private byte[] StringToBytes(string str)
            {
                if (str == null)
                {
                    return null;
                }

                return Encoding.UTF8.GetBytes(str);
            }

            private string BytesToString(byte[] bytes)
            {
                if (bytes == null)
                {
                    return null;
                }

                return Encoding.UTF8.GetString(bytes);
            }

            private ColumnFamilyInfo GetColumnFamilyInfo(string columnFamilyName)
            {
                if (columnFamilyName == null && m_defaultColumnFamilyInfo.Handle != null)
                {
                    return m_defaultColumnFamilyInfo;
                }

                return GetColumnFamilyInfoSlow(columnFamilyName);
            }

            private ColumnFamilyInfo GetColumnFamilyInfoSlow(string columnFamilyName)
            {
                columnFamilyName = columnFamilyName ?? ColumnFamilies.DefaultName;

                if (m_columns.TryGetValue(columnFamilyName, out var result))
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
            public IBuildXLKeyValueStore CreateSnapshot()
            {
                return new RocksDbStore(this);
            }
        } // RocksDbStore
    } // KeyValueStoreAccessor
}
