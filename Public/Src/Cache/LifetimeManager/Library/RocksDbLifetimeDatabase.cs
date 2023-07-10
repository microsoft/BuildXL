// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore.Sketching;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
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
        }

        internal record ContentEntry(long BlobSize, int ReferenceCount)
        {
            public void Serialize(SpanWriter writer)
            {
                writer.Write(BlobSize);
                writer.Write(ReferenceCount);
            }

            public static ContentEntry Deserialize(SpanReader reader)
            {
                var blobSize = reader.ReadInt64();
                var referenceCount = reader.ReadInt32();

                return new ContentEntry(blobSize, referenceCount);
            }
        }

        internal record MetadataEntry(long BlobSize, DateTime LastAccessTime, ContentHash[] Hashes)
        {
            public void Serialize(SpanWriter writer)
            {
                writer.Write(BlobSize);
                writer.Write(LastAccessTime);
                writer.Write(Hashes, (ref SpanWriter w, ContentHash hash) => w.Write(hash));
            }

            public static MetadataEntry Deserialize(SpanReader reader)
            {
                var blobSize = reader.ReadInt64();
                var lastAccessTime = reader.ReadDateTime();
                var hashes = reader.ReadArray(static (ref SpanReader source) => ContentHash.FromSpan(source.ReadSpan(ContentHash.SerializedLength)));

                return new MetadataEntry(blobSize, lastAccessTime, hashes);
            }
        }

        /// <summary>
        /// There's multiple column families in this usage of RocksDB. Their usages are documented below.
        /// </summary>
        private enum Columns
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
        }

        private readonly KeyValueStoreAccessor _keyValueStore;
        private readonly SerializationPool _serializationPool = new();
        private readonly Configuration _configuration;
        private readonly IClock _clock;

        private readonly ColumnFamilyHandle _contentCf;
        private readonly ColumnFamilyHandle _fingerprintCf;

        protected RocksDbLifetimeDatabase(
            Configuration configuration,
            IClock clock,
            KeyValueStoreAccessor keyValueStore,
            ColumnFamilyHandle contentCf,
            ColumnFamilyHandle fingerprintCf)
        {
            _configuration = configuration;
            _clock = clock;
            _keyValueStore = keyValueStore;
            _contentCf = contentCf;
            _fingerprintCf = fingerprintCf;
        }

        public static Result<RocksDbLifetimeDatabase> Create(
            Configuration configuration,
            IClock clock)
        {
            var possibleStore = CreateAccessor(configuration);

            KeyValueStoreAccessor keyValueStore;
            if (possibleStore.Succeeded)
            {
                keyValueStore = possibleStore.Result;

                var (contentCf, fingerprintCf) = GetColumnFamilies(keyValueStore);

                return new RocksDbLifetimeDatabase(configuration, clock, keyValueStore, contentCf, fingerprintCf);
            }

            return new Result<RocksDbLifetimeDatabase>($"Failed to initialize a RocksDb store at {configuration.DatabasePath}:", possibleStore.Failure.DescribeIncludingInnerFailures());
        }

        internal static (ColumnFamilyHandle contentCf, ColumnFamilyHandle fingerprintCf) GetColumnFamilies(KeyValueStoreAccessor keyValueStore)
        {
            ColumnFamilyHandle? contentCf = null, fingerprintCf = null;
            var result = keyValueStore.Use(store =>
            {
                contentCf = store.GetColumn(nameof(Columns.Content));
                fingerprintCf = store.GetColumn(nameof(Columns.Fingerprints));
            }).ThrowIfFailure();

            Contract.Assert(contentCf != null);
            Contract.Assert(fingerprintCf != null);
            return (contentCf, fingerprintCf);
        }

        protected static Possible<KeyValueStoreAccessor> CreateAccessor(Configuration configuration)
        {
            // WIP: review these settings. Mostly copy/pasted from RocksDbContentLocationDatabase
            var settings = new RocksDbStoreConfiguration(configuration.DatabasePath)
            {
                AdditionalColumns = new[] { nameof(Columns.Content), nameof(Columns.Fingerprints) },
                RotateLogsMaxFileSizeBytes = (ulong)"1MB".ToSize(),
                RotateLogsNumFiles = 10,
                RotateLogsMaxAge = TimeSpan.FromHours(3),
                FastOpen = true,
                ReadOnly = false,
                DisableAutomaticCompactions = false,
                LeveledCompactionDynamicLevelTargetSizes = true,
                Compression = Compression.Zstd,
                UseReadOptionsWithSetTotalOrderSeekInDbEnumeration = true,
                UseReadOptionsWithSetTotalOrderSeekInGarbageCollection = true,
            };

            settings.MergeOperators.Add(
                    nameof(Columns.Content),
                    MergeOperators.CreateAssociative(
                        "ContentRefCountMergeOperator",
                        (key, value1, value2, result) =>
                            MergeContentRefCount(value1, value2, result)));

            return KeyValueStoreAccessor.Open(settings);
        }

        private static bool MergeContentRefCount(
            ReadOnlySpan<byte> value1,
            ReadOnlySpan<byte> value2,
            MergeResult result)
        {
            var deserialized1 = ContentEntry.Deserialize(value1.AsReader());
            var deserialized2 = ContentEntry.Deserialize(value2.AsReader());

            var resultEntry = new ContentEntry(
                // When decrementing the ref count of a content hash, we don't get the size, so
                // we insert a 0 in the blob size. Make sure we're using the non-0 value.
                Math.Max(deserialized1.BlobSize, deserialized2.BlobSize),
                deserialized1.ReferenceCount + deserialized2.ReferenceCount);

            result.ValueBuffer.Resize(value1.Length);
            var writer = result.ValueBuffer.Value.AsWriter();
            resultEntry.Serialize(writer);

            return true;
        }

        public BoolResult AddContentHashList(ContentHashList contentHashList, IReadOnlyCollection<(ContentHash hash, long size)> hashes)
        {
            var batch = new WriteBatch();

            // Make sure we don't dispose of any of the pooled spans until the batch is complete.
            var disposables = new List<IDisposable>();

            try
            {
                var entry = new MetadataEntry(contentHashList.BlobSize, contentHashList.LastAccessTime, contentHashList.Hashes);

                var key = _serializationPool.SerializePooled(contentHashList.BlobName, static (string s, ref SpanWriter writer) => writer.Write(s));
                disposables.Add(key);

                var value = _serializationPool.SerializePooled(entry, static (MetadataEntry instance, ref SpanWriter writer) => instance.Serialize(writer));
                disposables.Add(value);

                batch.Put(key.WrittenSpan, value.WrittenSpan, _fingerprintCf);

                foreach (var (hash, size) in hashes)
                {
                    var hashKey = _serializationPool.SerializePooled(
                        hash,
                        static (ContentHash instance, ref SpanWriter writer) => writer.Write(instance));
                    disposables.Add(hashKey);

                    var contentEntry = new ContentEntry(BlobSize: size, ReferenceCount: 1);

                    var valueSpan = _serializationPool.SerializePooled(
                        contentEntry,
                        static (ContentEntry instance, ref SpanWriter writer) => instance.Serialize(writer));
                    disposables.Add(valueSpan);

                    batch.Merge(hashKey.WrittenSpan, valueSpan.WrittenSpan, _contentCf);
                }

                var status = _keyValueStore.Use(store =>
                {
                    store.ApplyBatch(batch);
                });

                return status.Succeeded
                    ? BoolResult.Success
                    : new BoolResult(status.Failure.DescribeIncludingInnerFailures());
            }
            finally
            {
                foreach (var disposable in disposables)
                {
                    disposable.Dispose();
                }
            }
        }

        public BoolResult DeleteContentHashList(
            string contentHashListName,
            IReadOnlyCollection<ContentHash> hashes)
        {
            var batch = new WriteBatch();

            // Make sure we don't dispose of any of the pooled spans until the batch is complete.
            var disposables = new List<IDisposable>();

            try
            {
                var key = _serializationPool.SerializePooled(contentHashListName, static (string s, ref SpanWriter writer) => writer.Write(s));
                disposables.Add(key);

                batch.Delete(key.WrittenSpan, _fingerprintCf);

                // Decrement reference count for all the contents.
                foreach (var hash in hashes)
                {
                    var hashKey = _serializationPool.SerializePooled(
                        hash,
                        static (ContentHash instance, ref SpanWriter writer) => writer.Write(instance));
                    disposables.Add(hashKey);

                    var value = new ContentEntry(BlobSize: 0, ReferenceCount: -1);

                    var valueSpan = _serializationPool.SerializePooled(
                        value,
                        static (ContentEntry instance, ref SpanWriter writer) => instance.Serialize(writer));
                    disposables.Add(valueSpan);

                    batch.Merge(hashKey.WrittenSpan, valueSpan.WrittenSpan, _contentCf);
                }

                var status = _keyValueStore.Use(store =>
                {
                    store.ApplyBatch(batch);
                });

                return status.Succeeded
                    ? BoolResult.Success
                    : new BoolResult(status.Failure.DescribeIncludingInnerFailures());
            }
            finally
            {
                foreach (var disposable in disposables)
                {
                    disposable.Dispose();
                }
            }
        }

        public BoolResult AddContent(ContentHash hash, long blobSize)
        {
            var status = _keyValueStore.Use(store =>
            {
                using var key = _serializationPool.SerializePooled(
                    hash,
                    static (ContentHash instance, ref SpanWriter writer) => writer.Write(instance));

                var value = new ContentEntry(BlobSize: blobSize, ReferenceCount: 0);

                using var valueSpan = _serializationPool.SerializePooled(
                    value,
                    static (ContentEntry instance, ref SpanWriter writer) => instance.Serialize(writer));

                store.Merge(key.WrittenSpan, valueSpan.WrittenSpan, columnFamilyName: nameof(Columns.Content));
            });

            return status.Succeeded
                ? BoolResult.Success
                : new BoolResult(status.Failure.DescribeIncludingInnerFailures());
        }

        public Result<EnumerationResult> GetLruOrderedContentHashLists(OperationContext context)
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

            var status = _keyValueStore.Use(store =>
            {
                store.PrefixLookup(
                    state: string.Empty,
                    prefix: ReadOnlySpan<byte>.Empty,
                    columnFamilyName: nameof(Columns.Fingerprints),
                    (state, key, value) =>
                    {
                        if (context.Token.IsCancellationRequested)
                        {
                            return false;
                        }

                        MetadataEntry metadataEntry;
                        try
                        {
                            metadataEntry = MetadataEntry.Deserialize(value.AsReader());
                        }
                        catch
                        {
                            Tracer.Error(context, $"Failure to deserialize span: {Convert.ToHexString(value)}");
                            throw;
                        }

                        var lastAccessDelta = now - metadataEntry.LastAccessTime;
                        lastAccessDeltaSketch.Insert(lastAccessDelta.TotalMilliseconds);

                        totalSize += metadataEntry.BlobSize;
                        firstPassScannedEntries++;

                        return true;
                    });
            });

            if (!status.Succeeded)
            {
                return new Result<EnumerationResult>(status.Failure.DescribeIncludingInnerFailures());
            }

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

                    chlEnumerable = chlEnumerable.Concat(EnumerateContentHashLists(context, previousLimit, upperLimit));

                    previousLimit = upperLimit;
                    previousQuantile = newQuantile;
                }
            }

            Tracer.Info(context, "Starting content enumeration for calculating total size");

            long zeroRefContent = 0;
            long zeroRefContentSize = 0;
            long contentBlobCount = 0;
            status = _keyValueStore.Use(store =>
            {
                store.IterateDbContent(
                    iterator =>
                    {
                        var contentEntry = ContentEntry.Deserialize(iterator.Value().AsReader());
                        totalSize += contentEntry.BlobSize;
                        firstPassScannedEntries++;
                        contentBlobCount++;

                        if (contentEntry.ReferenceCount == 0)
                        {
                            zeroRefContent++;
                            zeroRefContentSize += contentEntry.BlobSize;
                        }
                    },
                    columnFamilyName: nameof(Columns.Content),
                    startValue: (byte[]?)null,
                    context.Token);
            });

            if (!status.Succeeded)
            {
                return new Result<EnumerationResult>(status.Failure.DescribeIncludingInnerFailures());
            }

            if (firstPassScannedEntries == 0)
            {
                Tracer.Info(context, $"No entries in database. Early stopping.");
                return new Result<EnumerationResult>(new EnumerationResult(Array.Empty<ContentHashList>(), Array.Empty<(ContentHash, long)>(), totalSize: 0));
            }

            Tracer.Info(context, $"Content enumeration complete. TotalContentBlobs=[{contentBlobCount}] ZeroReferenceBlobCount=[{zeroRefContent}], ZeroReferenceBlobSize=[{zeroRefContentSize}]");


            Tracer.Info(context, $"Initial enumeration complete. TotalSize=[{totalSize}]");

            context.Token.ThrowIfCancellationRequested();

            var zeroReferenceEnumerable = EnumerateZeroRefContent(context);

            return new Result<EnumerationResult>(new EnumerationResult(chlEnumerable, zeroReferenceEnumerable, totalSize));
        }

        private IEnumerable<ContentHashList> EnumerateContentHashLists(
            OperationContext context,
            DateTime lowerLimitNonInclusive,
            DateTime upperLimitInclusive)
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
                        nextKey)),
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

        private IEnumerable<(ContentHash content, long length)> EnumerateZeroRefContent(OperationContext context)
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
                        nextKey)),
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
            byte[]? startKey)
        {
            return GetEnumerationBatch(
                context,
                limit,
                startKey,
                (ref ReadOnlySpan<byte> key, ref ReadOnlySpan<byte> value, IList<ContentHashList> result) =>
                {
                    var reader = key.AsReader();
                    var blobName = reader.ReadString();

                    var metadataEntry = MetadataEntry.Deserialize(value.AsReader());

                    if (metadataEntry.LastAccessTime > lowerLimitNonInclusive &&
                        metadataEntry.LastAccessTime <= upperLimitInclusive)
                    {
                        result.Add(new ContentHashList(blobName, metadataEntry.LastAccessTime, metadataEntry.Hashes, metadataEntry.BlobSize));
                    }
                },
                nameof(Columns.Fingerprints));
        }

        private (IReadOnlyList<(ContentHash, long)> batch, bool reachedEnd, byte[]? next) GetZeroRefContentHashBatch(
            OperationContext context,
            int limit,
            byte[]? startKey)
        {
            return GetEnumerationBatch(
                context,
                limit,
                startKey,
                (ref ReadOnlySpan<byte> key, ref ReadOnlySpan<byte> value, IList<(ContentHash, long)> result) =>
                {
                    var contentEntry = ContentEntry.Deserialize(value.AsReader());

                    if (contentEntry.ReferenceCount == 0)
                    {
                        var hash = ContentHash.FromSpan(key);
                        result.Add((hash, contentEntry.BlobSize));
                    }
                },
                nameof(Columns.Content));
        }

        private delegate void EnumerationBatchHandler<T>(ref ReadOnlySpan<byte> key, ref ReadOnlySpan<byte> value, IList<T> result);

        private (IReadOnlyList<T> batch, bool reachedEnd, byte[]? next) GetEnumerationBatch<T>(
            OperationContext context,
            int limit,
            byte[]? startKey,
            EnumerationBatchHandler<T> handle,
            string columnFamily)
        {
            Contract.Requires(limit > 0);

            var result = new List<T>();
            byte[]? nextInitialKey = null;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
            var status = _keyValueStore!.Use(store =>
            {
                return store.IterateDbContent(
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

                        handle(ref key, ref value, result);
                    },
                    columnFamilyName: columnFamily,
                    startValue: startKey,
                    cts.Token);
            }).ThrowIfFailure();

            return (result, status.Result.ReachedEnd, nextInitialKey);
        }

        public BoolResult UpdateContentHashListLastAccessTime(ContentHashList contentHashList)
        {
            // We can simply override the existing value.
            var entry = new MetadataEntry(contentHashList.BlobSize, contentHashList.LastAccessTime, contentHashList.Hashes);
            using var key = _serializationPool.SerializePooled(contentHashList.BlobName, static (string s, ref SpanWriter writer) => writer.Write(s));
            using var value = _serializationPool.SerializePooled(entry, static (MetadataEntry instance, ref SpanWriter writer) => instance.Serialize(writer));

            var status = _keyValueStore.Use(store =>
            {
                store.Put(key.WrittenSpan, value.WrittenSpan, nameof(Columns.Fingerprints));
            });

            return status.Succeeded
                ? BoolResult.Success
                : new BoolResult(status.Failure.DescribeIncludingInnerFailures());
        }

        public BoolResult DeleteContent(ContentHash hash)
        {
            var status = _keyValueStore.Use(store =>
            {
                using var key = _serializationPool.SerializePooled(
                    hash,
                    static (ContentHash instance, ref SpanWriter writer) => writer.Write(instance));

                store.Remove(key.WrittenSpan, columnFamilyName: nameof(Columns.Content));
            });

            if (!status.Succeeded)
            {
                return new BoolResult(status.Failure.DescribeIncludingInnerFailures());
            }

            return BoolResult.Success;
        }

        internal MetadataEntry? TryGetContentHashList(StrongFingerprint strongFingerprint, out string? blobPath)
        {
            string? path = null;
            var result = _keyValueStore.Use(store =>
            {
                path = AzureBlobStorageMetadataStore.GetBlobPath(strongFingerprint);
                using var key = _serializationPool.SerializePooled(path, static (string s, ref SpanWriter writer) => writer.Write(s));

                var found = store.TryDeserializeValue(
                    key.WrittenSpan,
                    columnFamilyName: nameof(Columns.Fingerprints),
                    MetadataEntry.Deserialize,
                    out var result);

                return result;
            });

            blobPath = path;
            return result.Result;
        }

        internal ContentEntry? GetContentEntry(ContentHash hash)
        {
            var result = _keyValueStore.Use(store =>
            {
                using var key = _serializationPool.SerializePooled(
                    hash,
                    static (ContentHash instance, ref SpanWriter writer) => writer.Write(instance));

                var found = store.TryDeserializeValue<ContentEntry>(
                    key.WrittenSpan,
                    columnFamilyName: nameof(Columns.Content),
                    ContentEntry.Deserialize,
                    out var result);

                return result;
            });

            return result.Result;
        }

        public void Dispose()
        {
            _keyValueStore.Dispose();
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
