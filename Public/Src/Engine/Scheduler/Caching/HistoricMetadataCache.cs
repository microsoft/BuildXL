// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.Core.FormattableStringEx;
using OperationHints = BuildXL.Cache.ContentStore.Interfaces.Sessions.OperationHints;

namespace BuildXL.Scheduler.Cache
{
    /// <summary>
    /// This class extends the functionality of PipTwoPhaseCacheWithHashLookup class by incorporating the metadata querying functionality.
    /// This class provides methods for managing and querying historic metadata cache entries, including:
    ///  - Storing and retrieving metadata and path sets.
    ///  - Publishing and retrieving cache entries.
    ///  - Supports remote metadata and path set reporting.
    /// </summary>
    public class HistoricMetadataCache : PipTwoPhaseCacheWithHashLookup
    {
        /// <summary>
        /// Indicates if entries should be purged as soon as there TTL reaches zero versus reaching a limit in percentage expired.
        /// </summary>
        private static bool ProactivePurging => EngineEnvironmentSettings.ProactivePurgeHistoricMetadataEntries;

        /// <summary>
        /// Default time-to-live (TTL) for new entries
        /// The TTL of an entry is the number of save / load round-trips until eviction (assuming it is not accessed within that time).
        /// </summary>
        private static byte TimeToLive => (byte)(EngineEnvironmentSettings.HistoricMetadataCacheDefaultTimeToLive.Value ?? 5);

        private static readonly Task<Possible<Unit>> s_genericSuccessTask = Task.FromResult(new Possible<Unit>(Unit.Void));

        /// <summary>
        /// Originating cache string
        /// </summary>
        public const string OriginatingCacheId = "HistoricMetadataCache";

        /// <summary>
        /// PathSet or Metadata content hashes that are newly added in the current session.
        /// </summary>
        /// <remarks>
        /// BuildXL only sends the new hashes to orchestrator from worker
        /// </remarks>
        private readonly ConcurrentBigMap<ContentHash, bool> m_newContentEntries;

        /// <summary>
        /// Hash codes of content which should be retained in the database. This is content associated with entries
        /// in <see cref="m_weakFingerprintEntries"/> and <see cref="m_fullFingerprintEntries"/>
        /// </summary>
        private readonly ConcurrentBigSet<int> m_retainedContentHashCodes = new ConcurrentBigSet<int>();

        /// <summary>
        /// Hash codes of content in the database.
        /// </summary>
        private readonly ConcurrentBigSet<int> m_existingContentEntries = new ConcurrentBigSet<int>();

        /// <summary>
        /// WeakFingerprint -> stack of (StrongFingeprint, PathSetHash).
        /// </summary>
        /// <remarks>
        ///  We want to iterate the lastly added cache entries earlier to increase the chance for strong fingerprint hits.
        ///  </remarks>
        private readonly ConcurrentBigMap<WeakContentFingerprint, Stack<Expirable<PublishedEntry>>> m_weakFingerprintEntries;

        /// <summary>
        /// Full Fingerprint (WeakFingerprint ^ StrongFingerprint) -> MetadataHash
        /// </summary>
        private readonly ConcurrentBigMap<ContentFingerprint, ContentHash> m_fullFingerprintEntries;

        /// <summary>
        /// Tracking mapping from weak fingerprint to semistable hash. Serialization is based on weak fingerprints. So in order to serialize
        /// <see cref="m_pipSemistableHashToWeakFingerprintMap"/> we need to lookup the corresponding semistable hash for a given weak
        /// fingerprint.
        /// </summary>
        private readonly ConcurrentBigMap<WeakContentFingerprint, long> m_weakFingerprintsToPipSemistableHashMap = new ConcurrentBigMap<WeakContentFingerprint, long>();

        private readonly ConcurrentBigMap<long, WeakContentFingerprint> m_pipSemistableHashToWeakFingerprintMap = new ConcurrentBigMap<long, WeakContentFingerprint>();

        /// <summary>
        /// Newly added full fingerprints (WeakFingerprint ^ StrongFingerprint) 
        /// </summary>
        private readonly ConcurrentBigSet<ContentFingerprint> m_newFullFingerprints;

        private Task m_garbageCollectTask = Task.FromResult(0);

        private readonly CancellationTokenSource m_garbageCollectCancellation = new CancellationTokenSource();

        /// <summary>
        /// When true, on closing the cache, we wait for the completion of the garbage collection task without sending a cancellation signal to that task beforehand.
        /// This can surely slow down the closing of the cache.
        /// </summary>
        private readonly bool m_waitForGCOnClose;

        /// <nodoc/>
        public HistoricMetadataCache(
            LoggingContext loggingContext,
            EngineCache cache,
            PipExecutionContext context,
            PathExpander pathExpander,
            AbsolutePath storeLocation,
            Func<PipTwoPhaseCacheWithHashLookup, Task> prepareAsync = null,
            AbsolutePath? logDirectoryLocation = null,
            bool waitForGCOnClose = false)
            : base(
                loggingContext,
                cache,
                context,
                pathExpander,
                storeLocation,
                prepareAsync,
                logDirectoryLocation)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(cache != null);
            Contract.Requires(context != null);
            Contract.Requires(storeLocation.IsValid);

            m_weakFingerprintEntries = new ConcurrentBigMap<WeakContentFingerprint, Stack<Expirable<PublishedEntry>>>();
            m_fullFingerprintEntries = new ConcurrentBigMap<ContentFingerprint, ContentHash>();
            m_newFullFingerprints = new ConcurrentBigSet<ContentFingerprint>();
            m_retainedContentHashCodes = new ConcurrentBigSet<int>();
            m_newContentEntries = new ConcurrentBigMap<ContentHash, bool>();
            m_waitForGCOnClose = waitForGCOnClose;
        }

        /// <summary>
        /// Executes the initialization tasks for loading the database. This method orchestrates the loading of cache entries,
        /// starts the garbage collection process asynchronously.
        /// </summary>
        protected override async Task<bool> ExecuteLoadTask(Func<PipTwoPhaseCacheWithHashLookup, Task> prepareAsync)
        {
            // Note the base method yields immediately, so we unblock the caller
            await base.ExecuteLoadTask(prepareAsync);

            if (!Valid)
            {
                return true;
            }

            try
            {
                StoreAccessor?.Use(database =>
                {
                    using (Counters.StartStopwatch(PipCachingCounter.HistoricDeserializationDuration))
                    {
                        LoadCacheEntries(database);
                    }
                });

                m_garbageCollectTask = Task.Run(() => GarbageCollect());
            }
            catch (Exception ex)
            {
                Logger.Log.HistoricMetadataCacheLoadFailed(LoggingContext, ex.ToString());
                Valid = false;
            }

            return true;
        }

        /// <inheritdoc/>
        public override IEnumerable<Task<Possible<PublishedEntryRef, Failure>>> ListPublishedEntriesByWeakFingerprint(
            OperationContext operationContext,
            WeakContentFingerprint weak,
            OperationHints hints = default)
        {
            if (!LoadTask.GetAwaiter().GetResult())
            {
                yield return Task.FromResult(
                    new Possible<PublishedEntryRef, Failure>(
                        new CancellationFailure()));
            }

            Counters.IncrementCounter(PipCachingCounter.HistoricWeakFingerprintHits);

            Stack<Expirable<PublishedEntry>> stack;
            if (m_weakFingerprintEntries.TryGetValue(weak, out stack))
            {
                foreach (var entry in stack)
                {
                    // Exclude entries with no pointer to the full fingerprint. This state happens if GC has collected the full
                    // fingerprint due to missing content.
                    if (m_fullFingerprintEntries.ContainsKey(GetFullFingerprint(weak, entry.Value.StrongFingerprint)))
                    {
                        yield return Task.FromResult(
                            new Possible<PublishedEntryRef, Failure>(
                                new PublishedEntryRef(
                                    entry.Value.PathSetHash,
                                    entry.Value.StrongFingerprint,
                                    OriginatingCacheId,
                                    PublishedEntryRefLocality.Local)));
                    }
                }
            }

            Counters.DecrementCounter(PipCachingCounter.HistoricWeakFingerprintHits);
            Counters.IncrementCounter(PipCachingCounter.HistoricWeakFingerprintMisses);

            foreach (var entry in base.ListPublishedEntriesByWeakFingerprint(operationContext, weak, hints))
            {
                yield return entry;
            }

            Counters.IncrementCounter(PipCachingCounter.WeakFingerprintMisses);
        }

        /// <inheritdoc/>
        public override async Task<Possible<CacheEntry?, Failure>> TryGetCacheEntryAsync(
            Pip pip,
            WeakContentFingerprint weakFingerprint,
            ContentHash pathSetHash,
            StrongContentFingerprint strongFingerprint,
            OperationHints hints)
        {
            if (!await LoadTask)
            {
                return new Possible<CacheEntry?, Failure>(new CancellationFailure());
            }

            var fullFingerprint = GetFullFingerprint(weakFingerprint, strongFingerprint);
            ContentHash metadataHash;
            if (m_fullFingerprintEntries.TryGetValue(fullFingerprint, out metadataHash))
            {
                Counters.IncrementCounter(PipCachingCounter.HistoricCacheEntryHits);

                // Update the TTL in the weakFingerprintEntries
                Stack<Expirable<PublishedEntry>> stack;
                if (m_weakFingerprintEntries.TryGetValue(weakFingerprint, out stack))
                {
                    var newEntry = NewExpirable(new PublishedEntry(strongFingerprint, pathSetHash), TimeToLive);
                    stack.Push(newEntry);
                }

                return new CacheEntry(metadataHash, OriginatingCacheId, new ContentHash[] { metadataHash });
            }

            var result = await base.TryGetCacheEntryAsync(pip, weakFingerprint, pathSetHash, strongFingerprint, hints);
            if (result.Succeeded && result.Result.HasValue)
            {
                Counters.IncrementCounter(PipCachingCounter.HistoricCacheEntryMisses);
                SetMetadataEntry(weakFingerprint, strongFingerprint, pathSetHash, result.Result.Value.MetadataHash, semistableHash: pip.SemiStableHash);
            }

            return result;
        }

        /// <inheritdoc/>
        public override async Task<Possible<CacheEntryPublishResult, Failure>> TryPublishCacheEntryAsync(
            Pip pip,
            WeakContentFingerprint weakFingerprint,
            ContentHash pathSetHash,
            StrongContentFingerprint strongFingerprint,
            CacheEntry entry,
            CacheEntryPublishMode mode = CacheEntryPublishMode.CreateNew)
        {
            var result = await base.TryPublishCacheEntryAsync(pip, weakFingerprint, pathSetHash, strongFingerprint, entry, mode);

            if (result.Succeeded)
            {
                SetMetadataEntry(
                    weakFingerprint,
                    strongFingerprint,
                    pathSetHash,
                    result.Result.Status == CacheEntryPublishStatus.Published ? entry.MetadataHash : result.Result.ConflictingEntry.MetadataHash,
                    pip.SemiStableHash);
            }

            return result;
        }

        /// <inheritdoc/>
        public override async Task<Possible<PipCacheDescriptorV2Metadata>> TryRetrieveMetadataAsync(
            Pip pip,
            WeakContentFingerprint weakFingerprint,
            StrongContentFingerprint strongFingerprint,
            ContentHash metadataHash,
            ContentHash pathSetHash)
        {
            if(!await LoadTask)
            {
                new Possible<PipCacheDescriptorV2Metadata>(new CancellationFailure());
            }

            if (TryGetContent(metadataHash, out var content))
            {
                Counters.IncrementCounter(PipCachingCounter.HistoricMetadataHits);
                return CacheGrpcExtensions.Deserialize<PipCacheDescriptorV2Metadata>(content);
            }

            var possiblyRetrieved = await base.TryRetrieveMetadataAsync(pip, weakFingerprint, strongFingerprint, metadataHash, pathSetHash);
            if (possiblyRetrieved.Succeeded)
            {
                Counters.IncrementCounter(PipCachingCounter.HistoricMetadataMisses);
                if (possiblyRetrieved.Result != null)
                {
                    TryAdd(metadataHash, possiblyRetrieved.Result);
                }
            }

            return possiblyRetrieved;
        }

        /// <inheritdoc/>
        public override async Task<Possible<ContentHash>> TryStoreMetadataAsync(PipCacheDescriptorV2Metadata metadata)
        {
            var possiblyStored = await base.TryStoreMetadataAsync(metadata);
            if (possiblyStored.Succeeded)
            {
                if (!await LoadTask)
                {
                    return new Possible<ContentHash>(new CancellationFailure());
                }

                var metadataHash = possiblyStored.Result;
                TryAdd(metadataHash, metadata);
            }

            return possiblyStored;
        }

        /// <inheritdoc/>
        protected override async Task<Possible<StreamWithLength>> TryLoadAndOpenPathSetStreamAsync(ContentHash pathSetHash, bool avoidRemoteLookups = false)
        {
            if (!await LoadTask)
            {
                return new Possible<StreamWithLength>(new CancellationFailure());
            }

            if (TryGetContent(pathSetHash, out var content))
            {
                Counters.IncrementCounter(PipCachingCounter.HistoricPathSetHits);
                return (new MemoryStream(content.Array, content.Offset, content.Count, writable: false)).WithLength();
            }

            var possiblyOpened = await base.TryLoadAndOpenPathSetStreamAsync(pathSetHash, avoidRemoteLookups);
            if (possiblyOpened.Succeeded)
            {
                Counters.IncrementCounter(PipCachingCounter.HistoricPathSetMisses);
                using (Stream stream = possiblyOpened.Result)
                {
                    content = new ArraySegment<byte>(new byte[(int)stream.Length]);
                    var readCount = await stream.ReadAsync(content.Array, 0, content.Count);
                    Contract.Assert(readCount == content.Count);
                    TryAddContent(pathSetHash, content);
                    return (new MemoryStream(content.Array, writable: false)).WithLength();
                }
            }

            return possiblyOpened;
        }

        /// <inheritdoc/>
        public override IEnumerable<Task<Possible<ObservedPathSet>>> TryGetAssociatedPathSetsAsync(OperationContext context, Pip pip)
        {
            if (m_pipSemistableHashToWeakFingerprintMap.TryGetValue(pip.SemiStableHash, out var weakFingerprint)
                && m_weakFingerprintEntries.TryGetValue(weakFingerprint, out var entries))
            {
                foreach (var entry in entries)
                {
                    yield return TryRetrievePathSetAsync(context, weakFingerprint, entry.Value.PathSetHash);
                }
            }
        }

        /// <inheritdoc/>
        protected override async Task<Possible<Unit>> TryStorePathSetContentAsync(ContentHash pathSetHash, MemoryStream pathSetBuffer)
        {
            if (!await LoadTask)
            {
                new Possible<Unit>(new CancellationFailure());
            }

            this.TryAddContent(pathSetHash, ToStorableContent(pathSetBuffer));
            return await base.TryStorePathSetContentAsync(pathSetHash, pathSetBuffer);
        }

        /// <summary>
        /// Converts the given MemoryStream to an ArraySegment of bytes for storage.
        /// </summary>
        protected static ArraySegment<byte> ToStorableContent(MemoryStream pathSetBuffer)
        {
            return new ArraySegment<byte>(pathSetBuffer.GetBuffer(), 0, (int)pathSetBuffer.Length);
        }

        /// <summary>
        /// Adding cache entry
        /// </summary>
        protected bool SetMetadataEntry(
            WeakContentFingerprint weakFingerprint,
            StrongContentFingerprint strongFingerprint,
            ContentHash pathSetHash,
            ContentHash metadataHash,
            long semistableHash,
            byte? ttl = null)
        {
            m_retainedContentHashCodes.Add(pathSetHash.GetHashCode());
            m_retainedContentHashCodes.Add(metadataHash.GetHashCode());

            if (semistableHash != 0)
            {
                m_pipSemistableHashToWeakFingerprintMap[semistableHash] = weakFingerprint;
                m_weakFingerprintsToPipSemistableHashMap[weakFingerprint] = semistableHash;
            }

            var fullFingerprint = GetFullFingerprint(weakFingerprint, strongFingerprint);
            var addOrUpdateResult = m_fullFingerprintEntries.AddOrUpdate(
                fullFingerprint,
                metadataHash,
                (key, hash) => hash,
                (key, hash, existingEntry) => hash);

            if (!addOrUpdateResult.IsFound)
            {
                m_newFullFingerprints.Add(fullFingerprint);
            }

            var getOrAddResult = m_weakFingerprintEntries.GetOrAdd(
                weakFingerprint,
                new Stack<Expirable<PublishedEntry>>(),
                (key, result) => result);

            var stack = getOrAddResult.Item.Value;
            lock (stack)
            {
                stack.Push(NewExpirable(new PublishedEntry(strongFingerprint, pathSetHash), ttl ?? TimeToLive));
            }

            return !addOrUpdateResult.IsFound;
        }

        /// <inheritdoc/>
        protected static ContentFingerprint GetFullFingerprint(WeakContentFingerprint weakFingerprint, StrongContentFingerprint strongFingerprint)
        {
            Span<byte> fullFingerprintBytes = stackalloc byte[FingerprintUtilities.FingerprintLength];
            for (int i = 0; i < FingerprintUtilities.FingerprintLength; i++)
            {
                fullFingerprintBytes[i] = (byte)(weakFingerprint.Hash[i] ^ strongFingerprint.Hash[i]);
            }

            return new ContentFingerprint(new Fingerprint(new ReadOnlyFixedBytes(fullFingerprintBytes), FingerprintUtilities.FingerprintLength));
        }

        /// <summary>
        /// Closes the cache, ensuring all load tasks are complete and data is saved properly, and manages garbage collection shutdown.
        /// </summary>
        protected override async Task DoCloseAsync()
        {
            // Only save info if historic metadata cache was accessed
            if (Volatile.Read(ref LoadStarted))
            {
                // If a load was started, wait for full completion of the load
                // Otherwise, close and the load initialization can run concurrently and cause race conditions
                await LoadTask;

                Logger.Log.HistoricMetadataCacheTrace(LoggingContext, I($"Saving historic metadata cache Start"));

                if (StoreAccessor != null)
                {
                    // This flag ensures that garbage collection fully completes before proceeding,
                    // preventing issues caused by incomplete entry populations during cache operations.
                    if (!m_waitForGCOnClose)
                    {
                        // Stop garbage collection
                        await m_garbageCollectCancellation.CancelTokenAsyncIfSupported();
                    }

                    // Wait for garbage collection to complete
                    await m_garbageCollectTask;

                    await StoreCacheEntriesAsync();
                }

                Logger.Log.HistoricMetadataCacheTrace(LoggingContext, I($"Saving historic metadata cache Done"));
            }

            await base.DoCloseAsync();
        }

        private void GarbageCollect()
        {
            if (StoreAccessor != null)
            {
                Analysis.IgnoreResult(
                    StoreAccessor.Use(store =>
                    {
                        using (Counters.StartStopwatch(PipCachingCounter.HistoricCollectDuration))
                        {
                            byte[] startKey = null;
                            store.TryGetValue(StoreKeyNames.ContentGarbageCollectCursor, out startKey);

                            var gcStats = store.GarbageCollect(
                                canCollect: (byte[] key) =>
                                {
                                    if (ContentHashingUtilities.HashInfo.ByteLength == key.Length)
                                    {
                                        var hashCode = ContentHashingUtilities.CreateFrom(key).GetHashCode();
                                        if (m_retainedContentHashCodes.Contains(hashCode))
                                        {
                                            m_existingContentEntries.Add(hashCode);
                                            return false;
                                        }

                                        return true;
                                    }

                                    // Unexpected key in the content segment of the store. Just remove it.
                                    return true;
                                },
                                columnFamilyName: StoreColumnNames.Content,
                                cancellationToken: m_garbageCollectCancellation.Token,
                                startValue: startKey);

                            if (m_garbageCollectCancellation.IsCancellationRequested)
                            {
                                Counters.AddToCounter(PipCachingCounter.HistoricCollectCancelled, 1);
                            }

                            Counters.AddToCounter(PipCachingCounter.HistoricCollectMaxBatchEvictionTime, gcStats.MaxBatchEvictionTime);

                            if (m_garbageCollectCancellation.IsCancellationRequested && gcStats.LastKey != null)
                            {
                                // Garbage collection was terminated, so store the last key to enable resuming collection on the next build
                                store.Put(StoreKeyNames.ContentGarbageCollectCursor, gcStats.LastKey);
                            }
                            else
                            {
                                // Garbage collection finished, so start at the beginning for the next garbage collection
                                store.Remove(StoreKeyNames.ContentGarbageCollectCursor);
                            }

                            Counters.AddToCounter(PipCachingCounter.HistoricCollectRemovedBlobCount, gcStats.RemovedCount);
                            Counters.AddToCounter(PipCachingCounter.HistoricCollectTotalBlobCount, gcStats.TotalCount);
                        }
                    })
                );
            }
        }

        /// <inheritdoc />
        public override bool IsNewlyAdded(ContentHash hash)
        {
            return m_newContentEntries.TryGetValue(hash, out var newlyAdded) && newlyAdded;
        }

        /// <inheritdoc/>
        public override void ReportRemoteMetadata(
            PipCacheDescriptorV2Metadata metadata,
            ContentHash? metadataHash,
            ContentHash? pathSetHash,
            WeakContentFingerprint? weakFingerprint,
            StrongContentFingerprint? strongFingerprint,
            bool isExecution,
            bool preservePathCasing)
        {

            if (!LoadTask.GetAwaiter().GetResult())
            {
                return;
            }

            Counters.IncrementCounter(PipCachingCounter.ReportRemoteMetadataCalls);

            if (metadata != null && metadataHash.HasValue)
            {
                if (TryAdd(metadataHash.Value, metadata))
                {
                    Counters.IncrementCounter(
                        isExecution
                            ? PipCachingCounter.HistoricMetadataCountFromRemoteExecution
                            : PipCachingCounter.HistoricMetadataCountFromRemoteLookup);
                }
                else
                {
                    Counters.IncrementCounter(
                        isExecution
                            ? PipCachingCounter.HistoricMetadataExistCountFromRemoteExecution
                            : PipCachingCounter.HistoricMetadataExistCountFromRemoteLookup);
                }
            }

            if (weakFingerprint.HasValue && strongFingerprint.HasValue && pathSetHash.HasValue && metadataHash.HasValue)
            {
                if (SetMetadataEntry(weakFingerprint.Value, strongFingerprint.Value, pathSetHash.Value, metadataHash.Value, metadata?.SemiStableHash ?? 0))
                {
                    Counters.IncrementCounter(
                        isExecution
                            ? PipCachingCounter.HistoricCacheEntryCountFromRemoteExecution
                            : PipCachingCounter.HistoricCacheEntryCountFromRemoteLookup);
                }
                else
                {
                    Counters.IncrementCounter(
                        isExecution
                            ? PipCachingCounter.HistoricCacheEntryExistCountFromRemoteExecution
                            : PipCachingCounter.HistoricCacheEntryExistCountFromRemoteLookup);
                }
            }
        }

        /// <inheritdoc/>
        public override void ReportRemotePathSet(
            ObservedPathSet? pathSet,
            ContentHash? pathSetHash,
            bool isExecution,
            bool preservePathCasing)
        {
            Counters.IncrementCounter(PipCachingCounter.ReportRemotePathSetCalls);

            if (pathSet.HasValue && pathSetHash.HasValue)
            {
                if (TryAdd(pathSetHash.Value, pathSet.Value, preservePathCasing))
                {
                    Counters.IncrementCounter(
                        isExecution
                            ? PipCachingCounter.HistoricPathSetCountFromRemoteExecution
                            : PipCachingCounter.HistoricPathSetCountFromRemoteLookup);
                }
                else
                {
                    Counters.IncrementCounter(
                        isExecution
                            ? PipCachingCounter.HistoricPathSetExistCountFromRemoteExecution
                            : PipCachingCounter.HistoricPathSetExistCountFromRemoteLookup);
                }
            }
        }

        /// <summary>
        /// Adding pathset
        /// </summary>
        private bool TryAdd(ContentHash hash, in ObservedPathSet pathSet, bool preservePathCasing)
        {
            if (!LoadTask.GetAwaiter().GetResult())
            {
                return false;
            }

            using (Counters.StartStopwatch(PipCachingCounter.HistoricTryAddPathSetDuration))
            {
                bool added = false;
                Analysis.IgnoreResult(
                    TrySerializedAndStorePathSetAsync(
                        pathSet,
                        (pathSetHash, pathSetBuffer) =>
                        {
                            added = TryAddContent(pathSetHash, ToStorableContent(pathSetBuffer));
                            return s_genericSuccessTask;
                        },
                        preservePathCasing,
                        pathSetHash: hash
                    ).GetAwaiter().GetResult()
                );
                return added;
            }
        }

        /// <summary>
        /// Adding metadata
        /// </summary>
        private bool TryAdd(ContentHash metadataHash, PipCacheDescriptorV2Metadata metadata)
        {
            using (Counters.StartStopwatch(PipCachingCounter.HistoricTryAddMetadataDuration))
            {
                return TryAddContent(metadataHash, CacheGrpcExtensions.Serialize(metadata));
            }
        }

        private bool TryGetContent(ContentHash hash, out ArraySegment<byte> content)
        {
            using (Counters.StartStopwatch(PipCachingCounter.HistoricTryGetContentDuration))
            {
                content = default(ArraySegment<byte>);

                ArraySegment<byte>? result = null;
                StoreAccessor?.Use(database =>
                {
                    var hashKey = hash.ToHashByteArray();

                    if (database.TryGetValue(hashKey, out var value, StoreColumnNames.Content))
                    {
                        if (m_newContentEntries.TryAdd(hash, false))
                        {
                            m_retainedContentHashCodes.Add(hash.GetHashCode());
                        }
                        result = new ArraySegment<byte>(value);
                    }
                    else
                    {
                        result = null;
                    }
                });

                if (result == null)
                {
                    return false;
                }

                content = result.Value;
                return true;
            }
        }

        private bool TryAddContent(ContentHash hash, ArraySegment<byte> content)
        {
            using (Counters.StartStopwatch(PipCachingCounter.HistoricTryAddContentDuration))
            {
                if (m_newContentEntries.TryGetValue(hash, out var newlyAdded))
                {
                    // Already encountered content. No need to update backing store.
                    return false;
                }

                var success = false;
                StoreAccessor?.Use(database =>
                {
                    var hashKey = hash.ToHashByteArray();

                    if (!database.TryGetValue(hashKey, out var result, StoreColumnNames.Content))
                    {
                        // No hits. Content is new.
                        if (m_newContentEntries.TryAdd(hash, true))
                        {
                            m_retainedContentHashCodes.Add(hash.GetHashCode());
                            database.Put(hashKey, content.ToArray(), StoreColumnNames.Content);
                        }

                        success = true;
                    }
                    else
                    {
                        // Hits. Content is already present.
                        if (m_newContentEntries.TryAdd(hash, false))
                        {
                            m_retainedContentHashCodes.Add(hash.GetHashCode());
                        }

                        success = false;
                    }
                });

                return success;
            }
        }

        #region serialization

        /// <summary>
        /// Loads the cache entries from the database
        /// </summary>
        private void LoadCacheEntries(RocksDbStore store)
        {
            if (store.TryGetValue(StoreKeyNames.HistoricMetadataCacheEntriesKey, out var serializedCacheEntries))
            {
                using (var stream = new MemoryStream(serializedCacheEntries))
                using (var reader = BuildXLReader.Create(stream, leaveOpen: true))
                {
                    DeserializeCacheEntries(reader);
                }
            }
        }

        /// <summary>
        /// Stores the cache entries into the database
        /// </summary>
        private async Task StoreCacheEntriesAsync()
        {
            Contract.Requires(LoadTask.IsCompleted);

            // Unblock the thread
            await Task.Yield();

            using (Counters.StartStopwatch(PipCachingCounter.HistoricSerializationDuration))
            using (var stream = new MemoryStream())
            using (var writer = BuildXLWriter.Create(stream, leaveOpen: true))
            {
                SerializeCacheEntries(writer);
                var serializedCacheEntries = ToStorableContent(stream);

                Analysis.IgnoreResult(StoreAccessor.Use(store =>
                {
                    store.Put(StoreKeyNames.HistoricMetadataCacheEntriesKey, serializedCacheEntries.ToArray());
                }));
            }
        }

        private static List<T> GetUnexpiredEntries<T>(IReadOnlyCollection<T> entries, Func<T, bool> isExpired, out bool isPurging)
        {
            if (!ProactivePurging)
            {
                // If not proactive purging, wait to reach over half entries expired
                var expiredCount = entries.Where(isExpired).Count();
                if (expiredCount < (0.5 * entries.Count))
                {
                    // If the amount of expired entries is less than half, we don't purge (i.e. retain all entries even if they are expired)
                    isPurging = false;
                    return entries.ToList();
                }
            }

            isPurging = true;
            return entries.Where(v => !isExpired(v)).ToList();
        }

        private static byte ReadByteAsTimeToLive(BuildXLReader reader)
        {
            byte timeToLive = reader.ReadByte();
            if (timeToLive == 0)
            {
                // Zero is minimum time to live. Serializing entry with zero time to live is
                // allowed if during serialization purging was disabled (namely due to not having
                // a 'significant' number of entries to purge). See GetUnexpiredEntries
                return 0;
            }

            // Tentatively decrement the TTL for the in-memory table; if the table is saved again without using this entry, 
            // the TTL will stay at this lower value.
            return (byte)(timeToLive - 1);
        }

        private void SerializeCacheEntries(BuildXLWriter writer)
        {
            using (Counters.StartStopwatch(PipCachingCounter.HistoricCacheEntrySerializationDuration))
            {
                foreach (var content in m_newContentEntries)
                {
                    if (content.Value)
                    {
                        m_existingContentEntries.Add(content.Key.GetHashCode());
                    }
                }

                var beginningLength = writer.BaseStream.Length;

                int totalStrongEntries = 0;
                uint totalStrongEntriesWritten = 0;
                int numDuplicates = 0;
                int numInvalid = 0;

                var stack = new Stack<Expirable<PublishedEntry>>();
                var set = new HashSet<Expirable<PublishedEntry>>(new PublishedEntryComparer());

                bool isPurging;
                var unexpiredWeakFingerprints = GetUnexpiredEntries(m_weakFingerprintEntries, isExpired: a => a.Value.All(b => b.TimeToLive == 0), isPurging: out isPurging);
                uint unexpiredCount = (uint)unexpiredWeakFingerprints.Count;

                writer.Write(unexpiredCount);

                foreach (var weakFingerprintEntry in unexpiredWeakFingerprints)
                {
                    WeakContentFingerprint weakFingerprint = weakFingerprintEntry.Key;
                    totalStrongEntries += weakFingerprintEntry.Value.Count;

                    stack.Clear();
                    set.Clear();
                    foreach (var entry in weakFingerprintEntry.Value)
                    {
                        if (stack.Count == TimeToLive)
                        {
                            // Only retain at most DefaultTimeToLive entries for a given weak fingerprint
                            break;
                        }

                        if (isPurging && entry.TimeToLive == 0)
                        {
                            // Skip expired entries when purging
                            continue;
                        }

                        // To remove the duplicates of (strongfingerprint, pathsethash)
                        if (!set.Add(entry))
                        {
                            numDuplicates++;
                        }
                        else if (m_fullFingerprintEntries.TryGetValue(GetFullFingerprint(weakFingerprint, entry.Value.StrongFingerprint), out var metadataHash)
                            && m_existingContentEntries.Contains(entry.Value.PathSetHash.GetHashCode())
                            && m_existingContentEntries.Contains(metadataHash.GetHashCode()))
                        {
                            // Only include entries which were not evicted due to (i) missing metadata hash, (ii) missing pathset or metadata blob in store
                            stack.Push(entry);
                        }
                        else
                        {
                            numInvalid++;
                        }
                    }

                    uint length = (uint)stack.Count;

                    // For some weakfingerprints, there might be no usable strongfingerprints; so the length might equal to zero. We do still serialize
                    // those weakfingerprints; but during deserialization, none record will be created for those. 
                    m_weakFingerprintsToPipSemistableHashMap.TryGetValue(weakFingerprint, out var semistableHash);

                    writer.Write(weakFingerprint);
                    writer.Write(semistableHash);
                    writer.Write(length);
                    totalStrongEntriesWritten += length;

                    // Write the strong fingerprints in the increasing order of TTL because we use stack during deserialization.
                    foreach (var entry in stack)
                    {
                        writer.Write(entry.Value.StrongFingerprint);
                        writer.Write(entry.Value.PathSetHash);

                        var result = m_fullFingerprintEntries.TryGet(GetFullFingerprint(weakFingerprint, entry.Value.StrongFingerprint));
                        Contract.Assert(result.IsFound);
                        ContentHash metadataHash = result.Item.Value;
                        writer.Write(metadataHash);
                        writer.Write(entry.TimeToLive);
                    }
                }

                Counters.AddToCounter(PipCachingCounter.HistoricWeakFingerprintSavedCount, unexpiredCount);
                Counters.AddToCounter(PipCachingCounter.HistoricWeakFingerprintExpiredCount, m_weakFingerprintEntries.Count - unexpiredCount);
                Counters.AddToCounter(PipCachingCounter.HistoricStrongFingerprintSavedCount, totalStrongEntriesWritten);
                Counters.AddToCounter(PipCachingCounter.HistoricStrongFingerprintPurgedCount, totalStrongEntries - totalStrongEntriesWritten);
                Counters.AddToCounter(PipCachingCounter.HistoricStrongFingerprintDuplicatesCount, numDuplicates);
                Counters.AddToCounter(PipCachingCounter.HistoricStrongFingerprintInvalidCount, numInvalid);

                Counters.AddToCounter(PipCachingCounter.HistoricSavedCacheEntriesSizeBytes, writer.BaseStream.Length - beginningLength);
            }
        }

        private void DeserializeCacheEntries(BuildXLReader reader)
        {
            using (Counters.StartStopwatch(PipCachingCounter.HistoricCacheEntryDeserializationDuration))
            {
                int weakFingerprintCount = reader.ReadInt32();
                for (int i = 0; i < weakFingerprintCount; ++i)
                {
                    var weakFingerprint = reader.ReadWeakFingerprint();
                    var semistableHash = reader.ReadInt64();

                    var strongFingerprintCount = reader.ReadInt32();
                    for (int j = 0; j < strongFingerprintCount; ++j)
                    {
                        var strongFingerprint = reader.ReadStrongFingerprint();
                        var pathSetHash = reader.ReadContentHash();
                        var metadataHash = reader.ReadContentHash();
                        var ttl = ReadByteAsTimeToLive(reader);
                        SetMetadataEntry(weakFingerprint, strongFingerprint, pathSetHash, metadataHash, semistableHash, (byte)ttl);
                    }
                }

                Counters.AddToCounter(PipCachingCounter.HistoricWeakFingerprintLoadedCount, m_weakFingerprintEntries.Count);
                Counters.AddToCounter(PipCachingCounter.HistoricStrongFingerprintLoadedCount, m_fullFingerprintEntries.Count);
            }
        }

        #endregion

        /// <summary>
        /// Creates a new instance of the <see cref="Expirable{T}"/> struct with the specified value and time-to-live (TTL).
        /// </summary>
        private static Expirable<T> NewExpirable<T>(T value, byte timeToLive)
        {
            return new Expirable<T>(value, timeToLive);
        }

        /// <summary>
        /// Represents a value with an associated time-to-live (TTL) indicating its expiration status.
        /// </summary>
        private readonly record struct Expirable<T>
        {
            /// <summary>
            /// Gets the value stored in the expirable entry.
            /// </summary>
            public readonly T Value;

            /// <summary>
            /// Gets the time-to-live (TTL) for the entry, representing the number of save/load cycles until eviction if not accessed.
            /// </summary>
            public readonly byte TimeToLive;

            public Expirable(T value, byte timeToLive)
            {
                Value = value;
                TimeToLive = timeToLive;
            }
        }

        private class PublishedEntryComparer : IEqualityComparer<Expirable<PublishedEntry>>
        {
            public bool Equals(Expirable<PublishedEntry> x, Expirable<PublishedEntry> y)
            {
                // TTL is not a part of equality
                return x.Value.StrongFingerprint == y.Value.StrongFingerprint &&
                    x.Value.PathSetHash == y.Value.PathSetHash;
            }

            public int GetHashCode(Expirable<PublishedEntry> expirable)
            {
                unchecked
                {
                    return (expirable.Value.StrongFingerprint.GetHashCode() * 31) ^ expirable.Value.PathSetHash.GetHashCode();
                }
            }
        }

        /// <summary>
        /// Represents an entry in the historic metadata cache, consisting of a strong content fingerprint and a path set hash.
        /// </summary>
        private struct PublishedEntry
        {
            /// <summary>
            /// Strong content fingerprint associated with the entry.
            /// </summary>            
            public readonly StrongContentFingerprint StrongFingerprint;

            /// <summary>
            /// Gets the content hash of the path set associated with the entry.
            /// </summary>
            public readonly ContentHash PathSetHash;

            public PublishedEntry(StrongContentFingerprint strongFingerprint, ContentHash pathSetHash)
            {
                StrongFingerprint = strongFingerprint;
                PathSetHash = pathSetHash;
            }
        }
    }
}