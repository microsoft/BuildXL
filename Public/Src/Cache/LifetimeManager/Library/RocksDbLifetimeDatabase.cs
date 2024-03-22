// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore.Sketching;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;
using RocksDbSharp;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    /// <summary>
    /// RocksDb-backed database which keeps track of the content each content hash list references, as well as keeping track
    /// of the reference coutn for each individual piece of content. One of the important functionalities of the database is to also
    /// provide a way of enumerating fingerprints on a LRU ordering, to enable the scenario of garbage collection.
    /// </summary>
    public class RocksDbLifetimeDatabase : IDisposable
    {
        private const string CreationTimeKey = "__creation_time__";
        private static readonly Tracer Tracer = new(nameof(RocksDbLifetimeDatabase));

        public class Configuration
        {
            public required string DatabasePath { get; init; }

            /// <summary>
            /// LRU enumeration works by first doing a first pass of the entire database to
            /// estimate the percentiles for content hash lists' last access time. After that first pass,
            /// we will yield values that are under some percentile. If this isn't enough, more passes will
            /// be made, yielding values that fall under the previous percentile and the current one.
            ///
            /// For example, a value of 0.10 will first enumerate values that fall under the 0-10th percentile,
            /// then 10-20th percentile, and so on, until enumeration is broken.
            ///
            /// A larger value will perform less iterations of the database, but be less accurate in terms of actual
            /// LRU ordering.
            /// </summary>
            public double LruEnumerationPercentileStep { get; set; } = 0.05;

            /// <summary>
            /// It is not advisable to keep an interator open for long periods of time. Because of this, we are getting the list
            /// in batches, so that operations against the L3 can happen in between usages of the database.
            /// </summary>
            public int LruEnumerationBatchSize { get; set; } = 1000;

            public required IReadOnlyList<BlobNamespaceId> BlobNamespaceIds { get; set; }
        }

        /// <summary>
        /// There's multiple column families in this usage of RocksDB. Their usages are documented below.
        /// </summary>
        private enum ColumnFamily
        {
            /// <summary>
            /// Stores a mapping from <see cref="ContentHash"/> to <see cref="ContentEntry"/>. This will allow us to keep track
            /// of which content is ready to be removed from the L3, as well as how large it is.
            /// </summary>
            Content,

            /// Stores a mapping from <see cref="StrongFingerprint"/> to <see cref="MetadataEntry"/>.
            /// This allows us to keep track of which strong fingerprints have been accessed least recently, as well as which
            /// content hashes are contained within its content hash list.
            Fingerprints,

            /// <summary>
            /// Stores a mapping from storage account to a cursor to the latest change feed event processed.
            /// </summary>
            Cursors,

            /// <summary>
            /// Stores the last access time for untracked namespaces, such that we can clean up old unused containers.
            /// </summary>
            NamespaceLastAccessTime,
        }

        internal record ContentEntry(long BlobSize, int ReferenceCount)
        {
            public void Serialize(ref SpanWriter writer)
            {
                writer.Write(BlobSize);
                writer.Write(ReferenceCount);
            }

            public static ContentEntry Deserialize(ref SpanReader reader)
            {
                var blobSize = reader.ReadInt64();
                var referenceCount = reader.ReadInt32();

                return new ContentEntry(blobSize, referenceCount);
            }
        }

        internal record MetadataEntry(long BlobSize, DateTime LastAccessTime, ContentHash[] Hashes)
        {
            public void Serialize(ref SpanWriter writer)
            {
                writer.Write(BlobSize);
                writer.Write(LastAccessTime);
                writer.Write(Hashes, (ref SpanWriter w, ContentHash hash) => HashSerializationExtensions.Write(ref w, hash));
            }

            public static MetadataEntry Deserialize(ref SpanReader reader)
            {
                var blobSize = reader.ReadInt64();
                var lastAccessTime = reader.ReadDateTime();
                var hashes = reader.ReadArray(static (ref SpanReader source) => ContentHash.FromSpan(source.ReadSpan(ContentHash.SerializedLength)));

                return new MetadataEntry(blobSize, lastAccessTime, hashes);
            }
        }

        private readonly RocksDb _db;
        private readonly SerializationPool _serializationPool = new();
        private readonly Configuration _configuration;
        private readonly IClock _clock;

        private readonly ColumnFamilyHandle _cursorsCf;
        private readonly ColumnFamilyHandle _namespaceLastAccessTimeCf;

        private readonly ReadOptions _readOptions = new ReadOptions()
            .SetVerifyChecksums(true)
            .SetTotalOrderSeek(true);

        private readonly WriteOptions _writeOptions = new WriteOptions()
            .SetSync(false)
            .DisableWal(disable: 1);

        protected RocksDbLifetimeDatabase(
            Configuration configuration,
            IClock clock,
            RocksDb db)
        {
            _configuration = configuration;
            _clock = clock;
            _db = db;

            _cursorsCf = _db.GetColumnFamily(nameof(ColumnFamily.Cursors));
            _namespaceLastAccessTimeCf = _db.GetColumnFamily(nameof(ColumnFamily.NamespaceLastAccessTime));
        }

        public static RocksDbLifetimeDatabase Create(
            Configuration configuration,
            IClock clock)
        {
            RocksDb db = CreateDb(configuration);
            return new RocksDbLifetimeDatabase(configuration, clock, db);
        }

        protected static RocksDb CreateDb(Configuration configuration)
        {
            var options = new DbOptions();
            options.EnableStatistics();
            options.SetAdviseRandomOnOpen(true);
            options.SetCreateIfMissing(true);
            options.SetCreateMissingColumnFamilies(true);
            options.SetParanoidChecks(true);
            options.SetMaxOpenFiles(int.MaxValue);
            options.SetKeepLogFileNum(5);
            options.SetMaxLogFileSize(100_000);
            options.SetAllowConcurrentMemtableWrite(true);

            // The default column family is unused on purpose.
            var defaultOptions = new ColumnFamilyOptions() { };

            var columnFamilies = new ColumnFamilies(defaultOptions);

            var cursorOptions = new ColumnFamilyOptions()
                .SetCompression(Compression.Zstd);
            columnFamilies.Add(new ColumnFamilies.Descriptor(nameof(ColumnFamily.Cursors), cursorOptions));

            var namespaceLastAccessTimeOptions = new ColumnFamilyOptions()
                .SetCompression(Compression.Zstd)
                .SetMergeOperator(MergeOperators.CreateAssociative(
                    "NamespaceLastAccessTimeMergeOperator",
                    (key, value1, value2, result) =>
                        MergeNamespaceLastAccessTime(value1, value2, result)));
            columnFamilies.Add(new ColumnFamilies.Descriptor(nameof(ColumnFamily.NamespaceLastAccessTime), namespaceLastAccessTimeOptions));

            foreach (var namespaceId in configuration.BlobNamespaceIds)
            {
                var contentOptions = new ColumnFamilyOptions()
                    .SetCompression(Compression.Zstd)
                    .SetMergeOperator(MergeOperators.CreateAssociative(
                        $"ContentRefCountMergeOperator_{namespaceId.Universe}_{namespaceId.Namespace}",
                        (key, value1, value2, result) =>
                            MergeContentRefCount(value1, value2, result)));

                var fingerprintsOptions = new ColumnFamilyOptions()
                    .SetCompression(Compression.Zstd);

                columnFamilies.Add(new ColumnFamilies.Descriptor(GetColumnFamilyName(ColumnFamily.Content, namespaceId), contentOptions));
                columnFamilies.Add(new ColumnFamilies.Descriptor(GetColumnFamilyName(ColumnFamily.Fingerprints, namespaceId), fingerprintsOptions));
            }

            Directory.CreateDirectory(configuration.DatabasePath);
            var db = RocksDb.Open(options, path: configuration.DatabasePath, columnFamilies);
            return db;
        }

        public BoolResult Compact(OperationContext context)
        {
            Dictionary<string, ColumnFamilyStats>? statsBeforeCompaction = null;
            Dictionary<string, ColumnFamilyStats>? statsAfterCompaction = null;
            return context.PerformOperation(
                Tracer,
                () =>
                {
                    statsBeforeCompaction = _db.GetStatisticsByColumnFamily(context);

                    // By specifying no start and no limit, we compact the entire database.
                    _db.CompactRange(start: (byte[]?)null, limit: null);

                    statsAfterCompaction = _db.GetStatisticsByColumnFamily(context);

                    return BoolResult.Success;
                },
                messageFactory: _ => $"Before compaction: {string.Join(", ", statsBeforeCompaction?.Select(kvp => $"{kvp.Key}=[{kvp.Value}]") ?? [])}. " +
                    $"After compaction: {string.Join(", ", statsAfterCompaction?.Select(kvp => $"{kvp.Key}=[{kvp.Value}]") ?? [])}");
        }

        public IAccessor GetAccessor(BlobNamespaceId namespaceId)
        {
            return new Accessor(this, namespaceId);
        }

        private static bool MergeContentRefCount(
            ReadOnlySpan<byte> value1,
            ReadOnlySpan<byte> value2,
            MergeResult result)
        {
            var reader1 = value1.AsReader();
            var reader2 = value2.AsReader();
            var deserialized1 = ContentEntry.Deserialize(ref reader1);
            var deserialized2 = ContentEntry.Deserialize(ref reader2);

            var resultEntry = new ContentEntry(
                // When decrementing the ref count of a content hash, we don't get the size, so
                // we insert a 0 in the blob size. Make sure we're using the non-0 value.
                Math.Max(deserialized1.BlobSize, deserialized2.BlobSize),
                deserialized1.ReferenceCount + deserialized2.ReferenceCount);

            result.ValueBuffer.Resize(value1.Length);
            var writer = result.ValueBuffer.Value.AsWriter();
            resultEntry.Serialize(ref writer);

            return true;
        }

        private static bool MergeNamespaceLastAccessTime(
            ReadOnlySpan<byte> value1,
            ReadOnlySpan<byte> value2,
            MergeResult result)
        {
            var reader1 = value1.AsReader();
            var reader2 = value2.AsReader();

            var date1 = reader1.ReadDateTime();
            var date2 = reader2.ReadDateTime();

            var max = date1 > date2 ? date1 : date2;

            result.ValueBuffer.Resize(value1.Length);
            var writer = result.ValueBuffer.Value.AsWriter();
            writer.Write(max);

            return true;
        }

        public void SetCreationTime(DateTime creationTimeUtc)
        {
            using var key = _serializationPool.SerializePooled(CreationTimeKey, static (string s, ref SpanWriter writer) => writer.Write(s));

            var existing = Get<DateTime?>(key.WrittenSpan, cfHandle: null, (ref SpanReader reader) => reader.ReadDateTime());
            Contract.Assert(existing is null, "DB creation time can only be set once.");

            using var value = _serializationPool.SerializePooled(creationTimeUtc, static (DateTime instance, ref SpanWriter writer) => writer.Write(instance));

            _db.Put(key.WrittenSpan, value.WrittenSpan, cf: null);
        }

        public DateTime? GetCreationTime()
        {
            using var key = _serializationPool.SerializePooled(CreationTimeKey, static (string s, ref SpanWriter writer) => writer.Write(s));
            return Get<DateTime?>(key.WrittenSpan, cfHandle: null, (ref SpanReader reader) => reader.ReadDateTime());
        }

        public void SetNamespaceLastAccessTime(BlobNamespaceId namespaceId, string matrix, DateTime? lastAccessTimeUtc)
        {
            var keyString = GetNamespaceLastAccessTimeKey(namespaceId, matrix);
            using var key = _serializationPool.SerializePooled(keyString, static (string s, ref SpanWriter writer) => writer.Write(s));

            if (lastAccessTimeUtc is null)
            {
                _db.Delete(key.WrittenSpan, cf: _namespaceLastAccessTimeCf);
            }
            else
            {
                using var value = _serializationPool.SerializePooled(
                    lastAccessTimeUtc.Value, static (DateTime instance, ref SpanWriter writer) => writer.Write(instance));
                _db.Merge(key.WrittenSpan, value.WrittenSpan, cf: _namespaceLastAccessTimeCf);
            }
        }

        public DateTime? GetNamespaceLastAccessTime(BlobNamespaceId namespaceId, string matrix)
        {
            var keyString = GetNamespaceLastAccessTimeKey(namespaceId, matrix);
            using var key = _serializationPool.SerializePooled(keyString, static (string s, ref SpanWriter writer) => writer.Write(s));
            return Get<DateTime?>(key.WrittenSpan, cfHandle: _namespaceLastAccessTimeCf, (ref SpanReader reader) => reader.ReadDateTime());
        }

        private static string GetNamespaceLastAccessTimeKey(BlobNamespaceId namespaceId, string matrix) => $"{matrix}-{namespaceId}";

        private void AddContentHashList(
            ContentHashList contentHashList,
            IReadOnlyCollection<(ContentHash hash, long size)> hashes,
            ColumnFamilyHandle contentCf,
            ColumnFamilyHandle fingerprintCf)
        {
            var batch = new WriteBatch();

            // Make sure we don't dispose of any of the pooled spans until the batch is complete.
            var disposables = new List<IDisposable> { batch };

            try
            {
                var entry = new MetadataEntry(contentHashList.BlobSize, contentHashList.LastAccessTime, contentHashList.Hashes);

                var key = _serializationPool.SerializePooled(contentHashList.BlobName, static (string s, ref SpanWriter writer) => writer.Write(s));
                disposables.Add(key);

                var value = _serializationPool.SerializePooled(entry, static (MetadataEntry instance, ref SpanWriter writer) => instance.Serialize(ref writer));
                disposables.Add(value);

                batch.Put(key.WrittenSpan, value.WrittenSpan, fingerprintCf);

                foreach (var (hash, size) in hashes)
                {
                    var hashKey = _serializationPool.SerializePooled(
                        hash,
                        static (ContentHash instance, ref SpanWriter writer) => HashSerializationExtensions.Write(ref writer, instance));
                    disposables.Add(hashKey);

                    var contentEntry = new ContentEntry(BlobSize: size, ReferenceCount: 1);

                    var valueSpan = _serializationPool.SerializePooled(
                        contentEntry,
                        static (ContentEntry instance, ref SpanWriter writer) => instance.Serialize(ref writer));
                    disposables.Add(valueSpan);

                    batch.Merge(hashKey.WrittenSpan, valueSpan.WrittenSpan, contentCf);
                }

                _db.Write(batch, _writeOptions);
            }
            finally
            {
                foreach (var disposable in disposables)
                {
                    disposable.Dispose();
                }
            }
        }

        private void DeleteContentHashList(
            string contentHashListName,
            IReadOnlyCollection<ContentHash> hashes,
            ColumnFamilyHandle contentCf,
            ColumnFamilyHandle fingerprintCf)
        {
            var batch = new WriteBatch();

            // Make sure we don't dispose of any of the pooled spans until the batch is complete.
            var disposables = new List<IDisposable> { batch };

            try
            {
                var key = _serializationPool.SerializePooled(contentHashListName, static (string s, ref SpanWriter writer) => writer.Write(s));
                disposables.Add(key);

                batch.Delete(key.WrittenSpan, fingerprintCf);

                // Decrement reference count for all the contents.
                foreach (var hash in hashes)
                {
                    var hashKey = _serializationPool.SerializePooled(
                        hash,
                        static (ContentHash instance, ref SpanWriter writer) => HashSerializationExtensions.Write(ref writer, instance));
                    disposables.Add(hashKey);

                    var value = new ContentEntry(BlobSize: 0, ReferenceCount: -1);

                    var valueSpan = _serializationPool.SerializePooled(
                        value,
                        static (ContentEntry instance, ref SpanWriter writer) => instance.Serialize(ref writer));
                    disposables.Add(valueSpan);

                    batch.Merge(hashKey.WrittenSpan, valueSpan.WrittenSpan, contentCf);
                }

                _db.Write(batch, _writeOptions);
            }
            finally
            {
                foreach (var disposable in disposables)
                {
                    disposable.Dispose();
                }
            }
        }

        public string? GetCursor(string accountName)
        {
            using var key = _serializationPool.SerializePooled(accountName, static (string instance, ref SpanWriter writer) => writer.Write(instance));
            return Get(key.WrittenSpan, _cursorsCf, static (ref SpanReader reader) => reader.ReadString());
        }

        public void SetCursor(string accountName, string cursor)
        {
            using var key = _serializationPool.SerializePooled(accountName, static (string instance, ref SpanWriter writer) => writer.Write(instance));
            using var value = _serializationPool.SerializePooled(cursor, static (string instance, ref SpanWriter writer) => writer.Write(instance));

            _db.Put(key.WrittenSpan, value.WrittenSpan, _cursorsCf);
        }

        private void AddContent(ContentHash hash, long blobSize, ColumnFamilyHandle contentCf)
        {
            using var key = _serializationPool.SerializePooled(hash, static (ContentHash instance, ref SpanWriter writer) => HashSerializationExtensions.Write(ref writer, instance));

            var value = new ContentEntry(BlobSize: blobSize, ReferenceCount: 0);
            using var valueSpan = _serializationPool.SerializePooled(
                value,
                static (ContentEntry instance, ref SpanWriter writer) => instance.Serialize(ref writer));

            _db.Merge(key.WrittenSpan, valueSpan.WrittenSpan, contentCf);
        }

        private EnumerationResult GetLruOrderedContentHashLists(OperationContext context, ColumnFamilyHandle fingerprintsCf, ColumnFamilyHandle contentCf)
        {
            // LRU enumeration works by first doing a first pass of the entire database to
            // estimate the percentiles for content hash lists' last access time. After that first pass,
            // we will yield values that are under some percentile. If this isn't enough, more passes will
            // be made, yielding values that fall under the previous percentile and the current one.

            // The database snapshot that we traverse will be set at the moment we call PrefixSearch. Any changes to
            // the database after that won't be seen by this method. Hence, last access times are computed w.r.t. now.
            var now = _clock.UtcNow;

            Tracer.Info(context, $"Starting first pass. Now=[{now}]");

            long totalSize = 0;
            long firstPassScannedEntries = 0;
            var lastAccessDeltaSketch = new DDSketch();

            IterateDbContent(
                iterator =>
                {
                    MetadataEntry metadataEntry;
                    try
                    {
                        var reader = iterator.Value().AsReader();
                        metadataEntry = MetadataEntry.Deserialize(ref reader);
                    }
                    catch
                    {
                        Tracer.Error(context, $"Failure to deserialize span: {Convert.ToHexString(iterator.Value())}");
                        throw;
                    }

                    var lastAccessDelta = now - metadataEntry.LastAccessTime;
                    lastAccessDeltaSketch.Insert(lastAccessDelta.TotalMilliseconds);

                    totalSize += metadataEntry.BlobSize;
                    firstPassScannedEntries++;
                },
                fingerprintsCf,
                startKey: null,
                context.Token);

            IEnumerable<ContentHashList> chlEnumerable = Array.Empty<ContentHashList>();

            if (firstPassScannedEntries == 0)
            {
                Tracer.Info(context, $"No fingerprints in the database. Statistics are not available.");
            }
            else
            {
                Tracer.Info(context, $"First pass complete. CountEntries=[{firstPassScannedEntries}]. " +
                    $"Last Access Time statistics: Max=[{lastAccessDeltaSketch.Max}] Min=[{lastAccessDeltaSketch.Min}] Avg=[{lastAccessDeltaSketch.Average}] " +
                    $"P50=[{lastAccessDeltaSketch.Quantile(0.5)}] P75=[{lastAccessDeltaSketch.Quantile(0.75)}] P90=[{lastAccessDeltaSketch.Quantile(0.90)}] " +
                    $"P95=[{lastAccessDeltaSketch.Quantile(0.95)}]");

                var previousQuantile = 1.0;

                // Since lower limit is non-inclusive, make sure initial range includes the max delta.
                var previousLimit = now - TimeSpan.FromMilliseconds(lastAccessDeltaSketch.Max + 1);

                while (previousQuantile > 0)
                {
                    var newQuantile = Math.Max(0, previousQuantile - _configuration.LruEnumerationPercentileStep);
                    var upperLimit = now - TimeSpan.FromMilliseconds(lastAccessDeltaSketch.Quantile(newQuantile));

                    chlEnumerable = chlEnumerable.Concat(EnumerateContentHashLists(context, previousLimit, upperLimit, fingerprintsCf));

                    previousLimit = upperLimit;
                    previousQuantile = newQuantile;
                }
            }

            Tracer.Info(context, "Starting content enumeration for calculating total size");

            long zeroRefContent = 0;
            long zeroRefContentSize = 0;
            long contentBlobCount = 0;

            IterateDbContent(
                iterator =>
                {
                    var reader = iterator.Value().AsReader();
                    var contentEntry = ContentEntry.Deserialize(ref reader);
                    totalSize += contentEntry.BlobSize;
                    firstPassScannedEntries++;
                    contentBlobCount++;

                    if (contentEntry.ReferenceCount == 0)
                    {
                        zeroRefContent++;
                        zeroRefContentSize += contentEntry.BlobSize;
                    }
                },
                contentCf,
                startKey: null,
                context.Token);

            if (firstPassScannedEntries == 0)
            {
                Tracer.Info(context, $"No entries in database. Early stopping.");
                return new EnumerationResult(Array.Empty<ContentHashList>(), Array.Empty<(ContentHash, long)>(), totalSize: 0);
            }

            Tracer.Info(context, $"Content enumeration complete. TotalContentBlobs=[{contentBlobCount}] ZeroReferenceBlobCount=[{zeroRefContent}], ZeroReferenceBlobSize=[{zeroRefContentSize}]");


            Tracer.Info(context, $"Initial enumeration complete. TotalSize=[{totalSize}]");

            context.Token.ThrowIfCancellationRequested();

            var zeroReferenceEnumerable = EnumerateZeroRefContent(context, contentCf);

            return new EnumerationResult(chlEnumerable, zeroReferenceEnumerable, totalSize);
        }

        private IEnumerable<ContentHashList> EnumerateContentHashLists(
            OperationContext context,
            DateTime lowerLimitNonInclusive,
            DateTime upperLimitInclusive,
            ColumnFamilyHandle fingerprintsCf)
        {
            Tracer.Debug(context, $"Starting enumeration of DB for values in range ({lowerLimitNonInclusive}, {upperLimitInclusive}]");

            byte[]? nextKey = default;
            while (true)
            {
                var batchResult = context.PerformOperation(
                    Tracer,
                    () => new Result<(IReadOnlyList<ContentHashList> batch, bool reachedEnd, byte[]? nextKey)>(GetEnumerateContentHashListsBatch(
                        context,
                        lowerLimitNonInclusive,
                        upperLimitInclusive,
                        _configuration.LruEnumerationBatchSize,
                        nextKey,
                        fingerprintsCf)),
                    messageFactory: r => r.Succeeded
                        ? $"ReachedEnd={r.Value.reachedEnd}, NextKey=[{(r.Value.nextKey is null ? "null" : Convert.ToHexString(r.Value.nextKey))}]"
                        : string.Empty
                    );

                if (!batchResult.Succeeded)
                {
                    // Operation failure has already been logged by PerformOperation. This should be incredibly rare. Skip this batch and continue with next
                    // quantile band.

                    yield break;
                }

                (var batch, var reachedEnd, nextKey) = batchResult.Value;

                foreach (var item in batch)
                {
                    yield return item;
                }

                if (reachedEnd)
                {
                    break;
                }
            }

            yield break;
        }

        private IEnumerable<(ContentHash content, long length)> EnumerateZeroRefContent(OperationContext context, ColumnFamilyHandle contentCf)
        {
            Tracer.Info(context, "Starting enumeration of zero-reference content");

            byte[]? nextKey = default;
            while (true)
            {
                var batchResult = context.PerformOperation(
                    Tracer,
                    () => new Result<(IReadOnlyList<(ContentHash, long)>, bool reachedEnd, byte[]? nextKey)>(GetZeroRefContentHashBatch(
                        context,
                        _configuration.LruEnumerationBatchSize,
                        nextKey,
                        contentCf)),
                    messageFactory: r => r.Succeeded
                        ? $"ReachedEnd={r.Value.reachedEnd}, NextKey=[{(r.Value.nextKey is null ? "null" : Convert.ToHexString(r.Value.nextKey))}]"
                        : string.Empty
                    );

                if (!batchResult.Succeeded)
                {
                    // Operation failure has already been logged by PerformOperation. This should be incredibly rare. This will stop zero-reference content enumeration.

                    yield break;
                }

                (var batch, var reachedEnd, nextKey) = batchResult.Value;

                foreach (var item in batch)
                {
                    yield return item;
                }

                if (reachedEnd)
                {
                    break;
                }
            }

            yield break;
        }

        /// <summary>
        /// Virtual for testing.
        /// </summary>
        protected virtual (IReadOnlyList<ContentHashList> batch, bool reachedEnd, byte[]? next) GetEnumerateContentHashListsBatch(
            OperationContext context,
            DateTime lowerLimitNonInclusive,
            DateTime upperLimitInclusive,
            int limit,
            byte[]? startKey,
            ColumnFamilyHandle fingerprintsCf)
        {
            return GetEnumerationBatch(
                context,
                limit,
                startKey,
                (ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, IList<ContentHashList> result) =>
                {
                    var reader = key.AsReader();
                    var blobName = reader.ReadString();

                    var valueReader = value.AsReader();
                    var metadataEntry = MetadataEntry.Deserialize(ref valueReader);

                    if (metadataEntry.LastAccessTime > lowerLimitNonInclusive &&
                        metadataEntry.LastAccessTime <= upperLimitInclusive)
                    {
                        result.Add(new ContentHashList(blobName, metadataEntry.LastAccessTime, metadataEntry.Hashes, metadataEntry.BlobSize));
                    }
                },
                fingerprintsCf);
        }

        private (IReadOnlyList<(ContentHash, long)> batch, bool reachedEnd, byte[]? next) GetZeroRefContentHashBatch(
            OperationContext context,
            int limit,
            byte[]? startKey,
            ColumnFamilyHandle contentCf)
        {
            return GetEnumerationBatch(
                context,
                limit,
                startKey,
                (ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, IList<(ContentHash, long)> result) =>
                {
                    var reader = value.AsReader();
                    var contentEntry = ContentEntry.Deserialize(ref reader);

                    if (contentEntry.ReferenceCount == 0)
                    {
                        var hash = ContentHash.FromSpan(key);
                        result.Add((hash, contentEntry.BlobSize));
                    }
                },
                contentCf);
        }

        private delegate void EnumerationBatchHandler<T>(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, IList<T> result);
        private delegate T SpanReaderDeserializer<T>(ref SpanReader reader);

        private (IReadOnlyList<T> batch, bool reachedEnd, byte[]? next) GetEnumerationBatch<T>(
            OperationContext context,
            int limit,
            byte[]? startKey,
            EnumerationBatchHandler<T> handle,
            ColumnFamilyHandle cfHandle)
        {
            Contract.Requires(limit > 0);

            var result = new List<T>();
            byte[]? nextInitialKey = null;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(context.Token);

            var iterationResult = IterateDbContent(
                iterator =>
                {
                    var key = iterator.Key();

                    if (result.Count == limit)
                    {
                        nextInitialKey = key.ToArray();
                        cts.Cancel();
                        return;
                    }

                    var value = iterator.Value();

                    handle(key, value, result);
                },
                cfHandle,
                startKey,
                cts.Token);

            return (result, iterationResult.ReachedEnd, nextInitialKey);
        }

        private IterateDbContentResult IterateDbContent(
            Action<Iterator> onNextItem,
            ColumnFamilyHandle cfHandle,
            byte[]? startKey,
            CancellationToken token)
        {
            using (var iterator = _db.NewIterator(cfHandle, _readOptions))
            {
                if (startKey is null)
                {
                    iterator.SeekToFirst();
                }
                else
                {
                    iterator.Seek(startKey);
                }


                while (iterator.Valid() && !token.IsCancellationRequested)
                {
                    onNextItem(iterator);
                    iterator.Next();
                }
            }

            return new IterateDbContentResult() { ReachedEnd = !token.IsCancellationRequested, Canceled = token.IsCancellationRequested, };
        }

        private void UpdateContentHashListLastAccessTime(ContentHashList contentHashList, ColumnFamilyHandle fingerprintsCf)
        {
            // We can simply override the existing value.
            var entry = new MetadataEntry(contentHashList.BlobSize, contentHashList.LastAccessTime, contentHashList.Hashes);
            using var key = _serializationPool.SerializePooled(contentHashList.BlobName, static (string s, ref SpanWriter writer) => writer.Write(s));
            using var value = _serializationPool.SerializePooled(entry, static (MetadataEntry instance, ref SpanWriter writer) => instance.Serialize(ref writer));

            _db.Put(key.WrittenSpan, value.WrittenSpan, fingerprintsCf, _writeOptions);
        }

        private void DeleteContent(ContentHash hash, ColumnFamilyHandle contentCf)
        {
            using var key = _serializationPool.SerializePooled(
                    hash,
                    static (ContentHash instance, ref SpanWriter writer) => HashSerializationExtensions.Write(ref writer, instance));

            _db.Remove(key.WrittenSpan, contentCf, _writeOptions);
        }

        private MetadataEntry? GetContentHashList(StrongFingerprint strongFingerprint, ColumnFamilyHandle fingerprintsCf, out string? blobPath)
        {
            blobPath = AzureBlobStorageMetadataStore.GetBlobPath(strongFingerprint);
            using var key = _serializationPool.SerializePooled(blobPath, static (string s, ref SpanWriter writer) => writer.Write(s));

            return Get(key.WrittenSpan, fingerprintsCf, MetadataEntry.Deserialize);
        }

        private ContentEntry? GetContentEntry(ContentHash hash, ColumnFamilyHandle contentCf)
        {
            using var key = _serializationPool.SerializePooled(
                    hash,
                    static (ContentHash instance, ref SpanWriter writer) => HashSerializationExtensions.Write(ref writer, instance));

            return Get(key.WrittenSpan, contentCf, ContentEntry.Deserialize);
        }

        private T? Get<T>(ReadOnlySpan<byte> key, ColumnFamilyHandle? cfHandle, SpanReaderDeserializer<T> deserializer)
        {
            var pinnable = _db.UnsafeGetPinnable(key, cfHandle, _readOptions);
            if (pinnable is null)
            {
                return default;
            }

            using (pinnable)
            {
                var spanReader = pinnable.Value.UnsafePin().AsReader();
                return deserializer(ref spanReader)!;
            }
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        private static string GetColumnFamilyName(ColumnFamily column, BlobNamespaceId namespaceId) => $"{column}-{namespaceId}";

        /// <summary>
        /// Provides a view into the database where all the data corresponds to a given cache namespace.
        /// </summary>
        public interface IAccessor
        {
            void AddContent(ContentHash hash, long blobSize);
            internal ContentEntry? GetContentEntry(ContentHash hash);
            void DeleteContent(ContentHash hash);

            void AddContentHashList(ContentHashList contentHashList, IReadOnlyCollection<(ContentHash hash, long size)> hashes);
            internal MetadataEntry? GetContentHashList(StrongFingerprint strongFingerprint, out string? blobPath);
            void DeleteContentHashList(string contentHashListName, IReadOnlyCollection<ContentHash> hashes);

            EnumerationResult GetLruOrderedContentHashLists(OperationContext context);
            void UpdateContentHashListLastAccessTime(ContentHashList contentHashList);
        }

        private class Accessor : IAccessor
        {
            private readonly RocksDbLifetimeDatabase _database;
            private readonly BlobNamespaceId _namespaceId;

            private readonly ColumnFamilyHandle _contentCf;
            private readonly ColumnFamilyHandle _fingerprintCf;

            public Accessor(RocksDbLifetimeDatabase database, BlobNamespaceId namespaceId)
            {
                _database = database;
                _namespaceId = namespaceId;

                _contentCf = database._db.GetColumnFamily(GetColumnFamilyName(ColumnFamily.Content));
                _fingerprintCf = database._db.GetColumnFamily(GetColumnFamilyName(ColumnFamily.Fingerprints));
            }

            public void AddContent(ContentHash hash, long blobSize) => _database.AddContent(hash, blobSize, _contentCf);

            public void AddContentHashList(ContentHashList contentHashList, IReadOnlyCollection<(ContentHash hash, long size)> hashes)
                => _database.AddContentHashList(contentHashList, hashes, _contentCf, _fingerprintCf);

            public void DeleteContent(ContentHash hash) => _database.DeleteContent(hash, _contentCf);

            public void DeleteContentHashList(string contentHashListName, IReadOnlyCollection<ContentHash> hashes)
                => _database.DeleteContentHashList(contentHashListName, hashes, _contentCf, _fingerprintCf);

            public EnumerationResult GetLruOrderedContentHashLists(OperationContext context)
                => _database.GetLruOrderedContentHashLists(context, _fingerprintCf, _contentCf);

            public void UpdateContentHashListLastAccessTime(ContentHashList contentHashList)
                => _database.UpdateContentHashListLastAccessTime(contentHashList, _fingerprintCf);

            private string GetColumnFamilyName(ColumnFamily column) => RocksDbLifetimeDatabase.GetColumnFamilyName(column, _namespaceId);

            public ContentEntry? GetContentEntry(ContentHash hash) => _database.GetContentEntry(hash, _contentCf);

            MetadataEntry? IAccessor.GetContentHashList(StrongFingerprint strongFingerprint, out string? blobPath)
                => _database.GetContentHashList(strongFingerprint, _fingerprintCf, out blobPath);
        }


        /// <summary>
        /// <see cref="CheckpointManager"/> is designed around having a database that will be modified on the fly.
        /// <see cref="RocksDbLifetimeDatabase"/> does not need this use case, since it is only restored during start of execution, and saved during
        /// the end of execution of GC. Thus, it's possible for the DB to have a simpler design/state if we add this wrapper.
        /// </summary>
        public class CheckpointableLifetimeDatabase : StartupShutdownBase, ICheckpointable
        {
            protected override Tracer Tracer { get; } = new Tracer(nameof(CheckpointableLifetimeDatabase));

            public RocksDbLifetimeDatabase? Database { get; set; }

            private readonly Configuration _config;
            private readonly IClock _clock;

            public CheckpointableLifetimeDatabase(Configuration config, IClock clock)
            {
                _config = config;
                _clock = clock;
            }

            public RocksDbLifetimeDatabase GetDatabase()
            {
                if (Database is null)
                {
                    throw new InvalidOperationException("A checkpoint must be restored before attempting to use the database.");
                }

                return Database;
            }

            public bool IsImmutable(AbsolutePath filePath)
            {
                return filePath.Path.EndsWith(".sst", StringComparison.OrdinalIgnoreCase);
            }

            public BoolResult RestoreCheckpoint(OperationContext context, AbsolutePath checkpointDirectory)
            {
                Contract.Assert(Database is null);

                return context.PerformOperation(
                    Tracer,
                    () =>
                    {
                        // Make sure the parent directory exists, but the destination directory doesn't.
                        Directory.CreateDirectory(_config.DatabasePath);
                        FileUtilities.DeleteDirectoryContents(_config.DatabasePath, deleteRootDirectory: true);

                        Directory.Move(checkpointDirectory.ToString(), _config.DatabasePath);

                        var db = CreateDb(_config);
                        Database = new RocksDbLifetimeDatabase(_config, _clock, db);
                        return BoolResult.Success;
                    });
            }

            public BoolResult SaveCheckpoint(OperationContext context, AbsolutePath checkpointDirectory)
            {
                Contract.Assert(Database is not null);
                return context.PerformOperation(
                    Tracer,
                    () =>
                    {
                        // Make sure the parent directory exists, but the destination directory doesn't.
                        Directory.CreateDirectory(checkpointDirectory.Path);
                        FileUtilities.DeleteDirectoryContents(checkpointDirectory.Path, deleteRootDirectory: true);

                        var checkpoint = Database._db.Checkpoint();
                        checkpoint.Save(checkpointDirectory.Path);
                        return BoolResult.Success;
                    });
            }

            public void SetGlobalEntry(string key, string? value)
            {
                Contract.Assert(Database is not null);

                using var keySpan = Database._serializationPool.SerializePooled(key, static (string instance, ref SpanWriter writer) => writer.Write(instance));
                if (value is null)
                {
                    Database._db.Remove(keySpan.WrittenSpan);
                }
                else
                {
                    using var valueSpan = Database._serializationPool.SerializePooled(value, static (string instance, ref SpanWriter writer) => writer.Write(instance));
                    Database._db.Put(keySpan.WrittenSpan, valueSpan.WrittenSpan);
                }
            }

            public bool TryGetGlobalEntry(string key, [NotNullWhen(true)] out string? value)
            {
                Contract.Assert(Database is not null);

                using var keySpan = Database._serializationPool.SerializePooled(key, static (string instance, ref SpanWriter writer) => writer.Write(instance));
                value = Database.Get(keySpan.WrittenSpan, cfHandle: null, (ref SpanReader r) => r.ReadString());
                return value is not null;
            }
        }
    }

    public record ContentHashList(string BlobName, DateTime LastAccessTime, ContentHash[] Hashes, long BlobSize);

    public class EnumerationResult
    {
        /// <summary>
        /// It is important to ensure that this enumerable is never materialized, as it would trigger multiple reads of the
        /// entire database. The intended use is to break enumeration once the L3 is under quota.
        /// </summary>
        public IEnumerable<ContentHashList> LruOrderedContentHashLists { get; init; }

        public IEnumerable<(ContentHash, long)> ZeroReferenceBlobs { get; init; }

        public long TotalSize { get; init; }

        public EnumerationResult(IEnumerable<ContentHashList> enumerable, IEnumerable<(ContentHash hash, long length)> zeroRefContent, long totalSize)
        {
            LruOrderedContentHashLists = enumerable;
            ZeroReferenceBlobs = zeroRefContent;
            TotalSize = totalSize;
        }
    }
}
