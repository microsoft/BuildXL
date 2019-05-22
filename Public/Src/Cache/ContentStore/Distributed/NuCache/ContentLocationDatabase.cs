// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.InMemory;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Cache.ContentStore.Distributed.Tracing.TracingStructuredExtensions;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Base class that implements the core logic of <see cref="ContentLocationDatabase"/> interface.
    /// </summary>
    public abstract class ContentLocationDatabase : StartupShutdownSlimBase
    {
        private readonly ObjectPool<StreamBinaryWriter> _writerPool = new ObjectPool<StreamBinaryWriter>(() => new StreamBinaryWriter(), w => w.ResetPosition());
        private readonly ObjectPool<StreamBinaryReader> _readerPool = new ObjectPool<StreamBinaryReader>(() => new StreamBinaryReader(), r => { });

        /// <nodoc />
        protected readonly IClock Clock;

        /// <nodoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(ContentLocationDatabase)) { LogOperationStarted = false };

        /// <nodoc />
        public CounterCollection<ContentLocationDatabaseCounters> Counters { get; } = new CounterCollection<ContentLocationDatabaseCounters>();

        private readonly Func<IReadOnlyList<MachineId>> _getInactiveMachines;

        private Timer _gcTimer;
        private NagleQueue<(ShortHash hash, EntryOperation op, OperationReason reason, int modificationCount)> _nagleOperationTracer;
        private readonly ContentLocationDatabaseConfiguration _configuration;
        private bool _isGarbageCollectionEnabled;

        /// <summary>
        /// Fine-grained locks that is used for all operations that mutate records.
        /// </summary>
        private readonly object[] _locks = Enumerable.Range(0, ushort.MaxValue + 1).Select(s => new object()).ToArray();

        private ConcurrentBigMap<ShortHash, ContentLocationEntry> _inMemoryWriteCache = new ConcurrentBigMap<ShortHash, ContentLocationEntry>();

        private ConcurrentBigMap<ShortHash, ContentLocationEntry> _flushingInMemoryWriteCache = new ConcurrentBigMap<ShortHash, ContentLocationEntry>();
        private readonly object _cacheFlushLock = new object();

        /// <summary>
        /// Whether the cache is currently being used. Can only possibly be true in master. Only meant for testing
        /// purposes.
        /// </summary>
        internal bool IsInMemoryCacheEnabled { get; private set; } = false;

        /// <summary>
        /// This counter is not exact, but provides an approximate count. It may be thwarted by flushes and cache
        /// activate/deactivate events. Its only purpose is to roughly help ensure flushes are more frequent as
        /// more operations are performed.
        /// </summary>
        private int _cacheUpdatesSinceLastFlush = 0;

        /// <summary>
        /// Controls cache flushing due to timeout.
        /// </summary>
        private Timer _inMemoryCacheFlushTimer;

        private readonly object _cacheFlushTimerLock = new object();

        /// <nodoc />
        protected ContentLocationDatabase(IClock clock, ContentLocationDatabaseConfiguration configuration, Func<IReadOnlyList<MachineId>> getInactiveMachines)
        {
            Contract.Requires(clock != null);
            Contract.Requires(configuration != null);
            Contract.Requires(getInactiveMachines != null);

            Clock = clock;
            _configuration = configuration;
            _getInactiveMachines = getInactiveMachines;
        }

        /// <summary>
        /// Factory method that creates an instance of a <see cref="ContentLocationDatabase"/> based on an optional <paramref name="configuration"/> instance.
        /// </summary>
        public static ContentLocationDatabase Create(IClock clock, ContentLocationDatabaseConfiguration configuration, Func<IReadOnlyList<MachineId>> getInactiveMachines)
        {
            Contract.Requires(clock != null);
            Contract.Requires(configuration != null);

            switch (configuration)
            {
                case MemoryContentLocationDatabaseConfiguration memoryConfiguration:
                    return new MemoryContentLocationDatabase(clock, memoryConfiguration, getInactiveMachines);
                case RocksDbContentLocationDatabaseConfiguration rocksDbConfiguration:
                    return new RocksDbContentLocationDatabase(clock, rocksDbConfiguration, getInactiveMachines);
                default:
                    throw new InvalidOperationException($"Unknown configuration instance of type '{configuration.GetType()}'");
            }
        }

        /// <todoc />
        public void SetDatabaseMode(bool isDatabaseWritable)
        {
            ConfigureGarbageCollection(isDatabaseWritable);
            ConfigureInMemoryDatabaseCache(isDatabaseWritable);
        }

        /// <summary>
        /// Configures the behavior of the database's garbage collection
        /// </summary>
        private void ConfigureGarbageCollection(bool shouldDoGc)
        {
            if (_isGarbageCollectionEnabled != shouldDoGc)
            {
                _isGarbageCollectionEnabled = shouldDoGc;
                var nextGcTimeSpan = _isGarbageCollectionEnabled ? _configuration.LocalDatabaseGarbageCollectionInterval : Timeout.InfiniteTimeSpan;
                _gcTimer?.Change(nextGcTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        private void ConfigureInMemoryDatabaseCache(bool isDatabaseWritable)
        {
            if (_configuration.CacheEnabled)
            {
                // Ensure the in-memory cache is empty
                if (_inMemoryWriteCache.Count != 0)
                {
                    _inMemoryWriteCache = new ConcurrentBigMap<ShortHash, ContentLocationEntry>();
                }

                lock (_cacheFlushTimerLock)
                {
                    IsInMemoryCacheEnabled = isDatabaseWritable;
                }

                ResetFlushTimer();
            }
        }

        private void ResetFlushTimer()
        {
            lock (_cacheFlushTimerLock)
            {
                var cacheFlushTimeSpan = IsInMemoryCacheEnabled
                    ? _configuration.CacheFlushingMaximumInterval
                    : Timeout.InfiniteTimeSpan;

                _inMemoryCacheFlushTimer?.Change(cacheFlushTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (_configuration.LocalDatabaseGarbageCollectionInterval != Timeout.InfiniteTimeSpan)
            {
                _gcTimer = new Timer(
                    _ => GarbageCollect(context),
                    null,
                    _isGarbageCollectionEnabled ? _configuration.LocalDatabaseGarbageCollectionInterval : Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);
            }

            if (_configuration.CacheEnabled && _configuration.CacheFlushingMaximumInterval != Timeout.InfiniteTimeSpan)
            {
                _inMemoryCacheFlushTimer = new Timer(
                    _ => {
                        Counters[ContentLocationDatabaseCounters.NumberOfCacheFlushesTriggeredByTimer].Increment();
                        FlushIfEnabled(context);
                    },
                    null,
                    Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);
            }

            _nagleOperationTracer = NagleQueue<(ShortHash, EntryOperation, OperationReason, int)>.Create(
                ops =>
                {
                    LogContentLocationOperations(context, Tracer.Name, ops);
                    return Unit.VoidTask;
                },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMinutes(1),
                batchSize: 100);

            return Task.FromResult(InitializeCore(context));
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _nagleOperationTracer?.Dispose();

            lock (this)
            {
                _gcTimer?.Dispose();
                _inMemoryCacheFlushTimer?.Dispose();
            }

            return base.ShutdownCoreAsync(context);
        }

        /// <nodoc />
        protected abstract BoolResult InitializeCore(OperationContext context);

        /// <summary>
        /// Tries to locate an entry for a given hash.
        /// </summary>
        public bool TryGetEntry(OperationContext context, ShortHash hash, out ContentLocationEntry entry)
        {
            if (TryGetEntryCore(context, hash, out entry))
            {
                entry = FilterInactiveMachines(entry);
                return true;
            }

            return false;
        }

        /// <nodoc />
        protected abstract IEnumerable<ShortHash> EnumerateSortedKeysFromStorage(CancellationToken token);

        /// <summary>
        /// Gets a sequence of keys.
        /// </summary>
        protected IEnumerable<ShortHash> EnumerateSortedKeys(OperationContext context)
        {
            // NOTE: This is used by GC which will query for the value itself and thereby
            // get the value from the in memory cache if present. It will NOT necessarily
            // enumerate all keys in the in memory cache since they may be new keys but GC
            // is fine to just handle those on the next GC iteration
            return EnumerateSortedKeysFromStorage(context.Token);
        }

        /// <summary>
        /// Enumeration filter used by <see cref="ContentLocationDatabase.EnumerateEntriesWithSortedKeys"/> to filter out entries by raw value from a database.
        /// </summary>
        public delegate bool EnumerationFilter(byte[] value);

        /// <nodoc />
        protected abstract IEnumerable<(ShortHash key, ContentLocationEntry entry)> EnumerateEntriesWithSortedKeysFromStorage(
            CancellationToken token,
            EnumerationFilter filter = null);

        /// <summary>
        /// Gets a sequence of keys and values sorted by keys.
        /// </summary>
        public IEnumerable<(ShortHash key, ContentLocationEntry entry)> EnumerateEntriesWithSortedKeys(
            OperationContext context,
            EnumerationFilter filter = null)
        {
            Counters[ContentLocationDatabaseCounters.NumberOfCacheFlushesTriggeredByGarbageCollection].Increment();
            FlushIfEnabled(context);
            return EnumerateEntriesWithSortedKeysFromStorage(context.Token, filter);
        }

        /// <summary>
        /// Collects entries with last access time longer then time to live.
        /// </summary>
        public void GarbageCollect(OperationContext context)
        {
            lock (this)
            {
                if (ShutdownStarted)
                {
                    return;
                }
            }

            using (var cancellableContext = TrackShutdown(context))
            {
                DoGarbageCollect(cancellableContext);
            }

            lock (this)
            {
                if (!ShutdownStarted)
                {
                    if (_isGarbageCollectionEnabled)
                    {
                        _gcTimer?.Change(_configuration.LocalDatabaseGarbageCollectionInterval, Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }

        /// <summary>
        /// Collect unreachable entries from the local database.
        /// </summary>
        private void DoGarbageCollect(OperationContext context)
        {
            Tracer.Debug(context, "Start garbage collection of a local database.");

            using (var stopwatch = Counters[ContentLocationDatabaseCounters.GarbageCollect].Start())
            {
                int removedEntries = 0;
                int totalEntries = 0;

                long uniqueContentSize = 0;
                long totalContentCount = 0;
                long totalContentSize = 0;
                int uniqueContentCount = 0;

                // Tracking the difference between sequence of hashes for diagnostic purposes. We need to know how good short hashes are and how close are we to collisions.
                int maxHashFirstByteDifference = 0;

                ShortHash? lastHash = null;

                foreach (var hash in EnumerateSortedKeys(context))
                {
                    totalEntries++;

                    if (lastHash != null && lastHash != hash)
                    {
                        maxHashFirstByteDifference = Math.Max(maxHashFirstByteDifference, GetFirstByteDifference(lastHash.Value, hash));
                    }

                    lastHash = hash;

                    lock (GetLock(hash))
                    {
                        if (context.Token.IsCancellationRequested)
                        {
                            break;
                        }

                        if (!TryGetEntryCore(context, hash, out var entry))
                        {
                            continue;
                        }

                        var replicaCount = entry.Locations.Count;

                        uniqueContentCount++;
                        uniqueContentSize += entry.ContentSize;
                        totalContentSize += entry.ContentSize * replicaCount;
                        totalContentCount += replicaCount;

                        var filteredEntry = FilterInactiveMachines(entry);
                        if (filteredEntry.Locations.Count == 0)
                        {
                            removedEntries++;
                            Counters[ContentLocationDatabaseCounters.TotalNumberOfCollectedEntries].Increment();
                            Delete(context, hash);
                            LogEntryDeletion(context, hash, entry, OperationReason.GarbageCollect, replicaCount);
                        }
                        else if (filteredEntry.Locations.Count != entry.Locations.Count)
                        {
                            Counters[ContentLocationDatabaseCounters.TotalNumberOfCleanedEntries].Increment();
                            Store(context, hash, entry);

                            _nagleOperationTracer.Enqueue((hash, EntryOperation.RemoveMachine, OperationReason.GarbageCollect, entry.Locations.Count - filteredEntry.Locations.Count));
                        }
                    }
                }

                Counters[ContentLocationDatabaseCounters.TotalNumberOfScannedEntries].Add(uniqueContentCount);

                Tracer.Debug(context, $"Overall DB Stats: UniqueContentCount={uniqueContentCount}, UniqueContentSize={uniqueContentSize}, "
                    + $"TotalContentCount={totalContentCount}, TotalContentSize={totalContentSize}, MaxHashFirstByteDifference={maxHashFirstByteDifference}");

                Tracer.GarbageCollectionFinished(
                    context,
                    stopwatch.Elapsed,
                    totalEntries,
                    removedEntries,
                    Counters[ContentLocationDatabaseCounters.TotalNumberOfCollectedEntries].Value,
                    uniqueContentCount,
                    uniqueContentSize,
                    totalContentCount,
                    totalContentSize);
            }
        }

        private int GetFirstByteDifference(in ShortHash hash1, in ShortHash hash2)
        {
            for (int i = 0; i < ShortHash.SerializedLength; i++)
            {
                if (hash1[i] != hash2[i])
                {
                    return i;
                }
            }

            return ShortHash.SerializedLength;
        }

        private ContentLocationEntry FilterInactiveMachines(ContentLocationEntry entry)
        {
            var inactiveMachines = _getInactiveMachines();
            return entry.SetMachineExistence(inactiveMachines, exists: false);
        }

        /// <summary>
        /// Synchronizes machine location data between the database and the given cluster state instance
        /// </summary>
        public void UpdateClusterState(OperationContext context, ClusterState clusterState, bool write)
        {
            if (!_configuration.StoreClusterState)
            {
                return;
            }

            context.PerformOperation(
                Tracer,
                () =>
                {
                    // TODO: Handle setting inactive machines here
                    UpdateClusterStateCore(context, clusterState, write);

                    return BoolResult.Success;
                }).IgnoreFailure();
        }

        /// <nodoc />
        protected abstract void UpdateClusterStateCore(OperationContext context, ClusterState clusterState, bool write);

        /// <summary>
        /// Gets whether the file in the database's checkpoint directory is immutable between checkpoints (i.e. files with the same name will have the same content)
        /// </summary>
        public abstract bool IsImmutable(AbsolutePath dbFile);

        /// <nodoc/>
        public BoolResult SaveCheckpoint(OperationContext context, AbsolutePath checkpointDirectory)
        {
            using (Counters[ContentLocationDatabaseCounters.SaveCheckpoint].Start())
            {
                Counters[ContentLocationDatabaseCounters.NumberOfCacheFlushesTriggeredByCheckpoint].Increment();
                FlushIfEnabled(context);
                return context.PerformOperation(Tracer, () => SaveCheckpointCore(context, checkpointDirectory));
            }
        }

        /// <nodoc />
        protected abstract BoolResult SaveCheckpointCore(OperationContext context, AbsolutePath checkpointDirectory);

        /// <nodoc/>
        public BoolResult RestoreCheckpoint(OperationContext context, AbsolutePath checkpointDirectory)
        {
            using (Counters[ContentLocationDatabaseCounters.RestoreCheckpoint].Start())
            {
                return context.PerformOperation(Tracer, () => RestoreCheckpointCore(context, checkpointDirectory));
            }
        }

        /// <nodoc />
        protected abstract BoolResult RestoreCheckpointCore(OperationContext context, AbsolutePath checkpointDirectory);

        /// <nodoc />
        protected abstract bool TryGetEntryCoreFromStorage(OperationContext context, ShortHash hash, out ContentLocationEntry entry);

        /// <nodoc />
        protected bool TryGetEntryCore(OperationContext context, ShortHash hash, out ContentLocationEntry entry)
        {
            if (IsInMemoryCacheEnabled)
            {
                // The entry could be a tombstone, so we need to make sure the user knows content has actually been
                // deleted, which is why we check for null.
                if (_inMemoryWriteCache.TryGetValue(hash, out entry))
                {
                    Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Increment();
                    return entry != null;
                } else if (_flushingInMemoryWriteCache.TryGetValue(hash, out entry))
                {
                    Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Increment();
                    return entry != null;
                }

                Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Increment();
            }

            return TryGetEntryCoreFromStorage(context, hash, out entry);
        }

        /// <nodoc />
        protected abstract void Persist(OperationContext context, ShortHash hash, ContentLocationEntry entry);

        /// <nodoc />
        protected virtual void PersistBatch(OperationContext context, IEnumerable<KeyValuePair<ShortHash, ContentLocationEntry>> pairs)
        {
            foreach (var pair in pairs)
            {
                Persist(context, pair.Key, pair.Value);
            }
        }

        /// <nodoc />
        protected void Store(OperationContext context, ShortHash hash, ContentLocationEntry entry)
        {
            if (IsInMemoryCacheEnabled)
            {
                _inMemoryWriteCache[hash] = entry;

                // The fact that this is == is important to ensure it can only be triggered once by this condition
                if (Interlocked.Increment(ref _cacheUpdatesSinceLastFlush) == _configuration.CacheMaximumUpdatesPerFlush)
                {
                    Counters[ContentLocationDatabaseCounters.NumberOfCacheFlushesTriggeredByUpdates].Increment();
                    Task.Run(() => FlushIfEnabled(context)).Forget();
                }
            }
            else
            {
                Persist(context, hash, entry);
            }
        }

        /// <nodoc />
        protected void Delete(OperationContext context, ShortHash hash)
        {
            Store(context, hash, entry: null);
        }

        internal void FlushIfEnabled(OperationContext context)
        {
            if (!IsInMemoryCacheEnabled)
            {
                return;
            }

            Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheFlushes].Increment();

            context.PerformOperation(
                Tracer,
                () =>
                {
                    try
                    {
                        // This lock is required to ensure no flushes happen concurrently. We may loose updates if
                        // that happens.
                        lock (_cacheFlushLock)
                        {
                            using (Counters[ContentLocationDatabaseCounters.CacheFlush].Start())
                            {
                                Contract.Assert(_flushingInMemoryWriteCache.Count == 0);

                                // Make the flushing cache equivalent to the working cache. Since they are the same
                                // objects, changes are concurrent to both (i.e. no updates are lost between this line and
                                // the next).
                                Interlocked.Exchange(ref _flushingInMemoryWriteCache, _inMemoryWriteCache);

                                // Make the working cache be a new object. This way, all new operations from this point in
                                // time will be filled from this empty cache, backed with the flushing cache, and in the
                                // worst case hitting the store. Point being, the flushing cache becomes read-only.
                                Interlocked.Exchange(ref _inMemoryWriteCache, new ConcurrentBigMap<ShortHash, ContentLocationEntry>());

                                if (_configuration.CacheFlushSingleTransaction)
                                {
                                    PersistBatch(context, _flushingInMemoryWriteCache);
                                } else
                                {
                                    var actionBlock = new ActionBlockSlim<KeyValuePair<ShortHash, ContentLocationEntry>>(_configuration.CacheFlushDegreeOfParallelism, kv => {
                                        // Do not lock on GetLock here, as it will cause a deadlock with
                                        // SetMachineExistenceAndUpdateDatabase. It is correct not do take any locks as well,
                                        // because there no Store can happen while flush is running.
                                        Persist(context, kv.Key, kv.Value);
                                    });

                                    foreach (var kv in _flushingInMemoryWriteCache)
                                    {
                                        actionBlock.Post(kv);
                                    }

                                    actionBlock.Complete();
                                    actionBlock.CompletionAsync().Wait();
                                }

                                Interlocked.Exchange(ref _flushingInMemoryWriteCache, new ConcurrentBigMap<ShortHash, ContentLocationEntry>());
                            }
                        }

                        return BoolResult.Success;
                    }
                    finally
                    {
                        // Do not use Volatile.Write here; the memory being used is not volatile.
                        Interlocked.Exchange(ref _cacheUpdatesSinceLastFlush, 0);
                        ResetFlushTimer();
                    }
                }).ThrowIfFailure();
        }

        private ContentLocationEntry SetMachineExistenceAndUpdateDatabase(OperationContext context, ShortHash hash, MachineId? machine, bool existsOnMachine, long size, UnixTime? lastAccessTime, bool reconciling)
        {
            var created = false;
            var reason = reconciling ? OperationReason.Reconcile : OperationReason.Unknown;
            var priorLocationCount = 0;
            lock (GetLock(hash))
            {
                if (TryGetEntryCore(context, hash, out var entry))
                {
                    var initialEntry = entry;
                    priorLocationCount = entry.Locations.Count;

                    // Don't update machines if entry already contains the machine
                    var machines = machine != null && (entry.Locations[machine.Value] != existsOnMachine)
                        ? new[] { machine.Value }
                        : CollectionUtilities.EmptyArray<MachineId>();

                    // Don't update last access time if the touch frequency interval has not elapsed since last access
                    if (lastAccessTime != null && initialEntry.LastAccessTimeUtc.ToDateTime().IsRecent(lastAccessTime.Value.ToDateTime(), _configuration.TouchFrequency))
                    {
                        lastAccessTime = null;
                    }

                    entry = entry.SetMachineExistence(machines, existsOnMachine, lastAccessTime, size: size >= 0 ? (long?)size : null);

                    if (entry == initialEntry)
                    {
                        // The entry is unchanged.
                        return initialEntry;
                    }

                    if (existsOnMachine)
                    {
                        _nagleOperationTracer.Enqueue((hash, initialEntry.Locations.Count == entry.Locations.Count ? EntryOperation.Touch : EntryOperation.AddMachine, reason, 1));
                    }
                    else
                    {
                        _nagleOperationTracer.Enqueue((hash, EntryOperation.RemoveMachine, reason, 1));
                    }
                }
                else
                {
                    if (!existsOnMachine || machine == null)
                    {
                        // Attempting to remove a machine from or touch a missing entry should result in no changes
                        return ContentLocationEntry.Missing;
                    }

                    lastAccessTime = lastAccessTime ?? Clock.UtcNow;
                    var creationTime = UnixTime.Min(lastAccessTime.Value, Clock.UtcNow.ToUnixTime());

                    entry = ContentLocationEntry.Create(MachineIdSet.Empty.SetExistence(new[] { machine.Value }, existsOnMachine), size, lastAccessTime.Value, creationTime);
                    created = true;
                }

                if (entry.Locations.Count == 0)
                {
                    // Remove the hash when no more locations are registered
                    Delete(context, hash);
                    Counters[ContentLocationDatabaseCounters.TotalNumberOfDeletedEntries].Increment();
                    LogEntryDeletion(context, hash, entry, reason, priorLocationCount);
                }
                else
                {
                    Store(context, hash, entry);

                    if (created)
                    {
                        Counters[ContentLocationDatabaseCounters.TotalNumberOfCreatedEntries].Increment();
                        _nagleOperationTracer.Enqueue((hash, EntryOperation.Create, reason, 1));
                    }
                }

                return entry;
            }
        }

        private void LogEntryDeletion(OperationContext context, ShortHash hash, ContentLocationEntry entry, OperationReason reason, int priorLocationCount)
        {
            _nagleOperationTracer.Enqueue((hash, EntryOperation.Delete, reason, priorLocationCount));
            context.TraceDebug($"Deleted entry for hash {hash}. Creation Time: '{entry.CreationTimeUtc}', Last Access Time: '{entry.LastAccessTimeUtc}'");
        }

        private object GetLock(ShortHash hash)
        {
            // NOTE: We choose not to use "random" two bytes of the hash because
            // otherwise GC which uses an ordered set of hashes would acquire the same
            // lock over and over again potentially freezing out writers
            return _locks[hash[6] << 8 | hash[3]];
        }

        /// <nodoc />
        public void LocationAdded(OperationContext context, ShortHash hash, MachineId machine, long size, bool reconciling = false)
        {
            using (Counters[ContentLocationDatabaseCounters.LocationAdded].Start())
            {
                SetMachineExistenceAndUpdateDatabase(context, hash, machine, existsOnMachine: true, size: size, lastAccessTime: Clock.UtcNow, reconciling: reconciling);
            }
        }

        /// <nodoc />
        public void LocationRemoved(OperationContext context, ShortHash hash, MachineId machine, bool reconciling = false)
        {
            using (Counters[ContentLocationDatabaseCounters.LocationRemoved].Start())
            {
                SetMachineExistenceAndUpdateDatabase(context, hash, machine, existsOnMachine: false, size: -1, lastAccessTime: null, reconciling: reconciling);
            }
        }

        /// <nodoc />
        public void ContentTouched(OperationContext context, ShortHash hash, UnixTime accessTime)
        {
            using (Counters[ContentLocationDatabaseCounters.ContentTouched].Start())
            {
                SetMachineExistenceAndUpdateDatabase(context, hash, machine: null, existsOnMachine: false, -1, lastAccessTime: accessTime, reconciling: false);
            }
        }

        /// <summary>
        /// Serialize a given <paramref name="entry"/> into a byte stream.
        /// </summary>
        protected byte[] Serialize(ContentLocationEntry entry)
        {
            using (var pooledWriter = _writerPool.GetInstance())
            {
                var writer = pooledWriter.Instance.Writer;
                writer.WriteCompact(entry.ContentSize);
                entry.Locations.Serialize(writer);
                writer.Write(entry.CreationTimeUtc);
                long lastAccessTimeOffset = entry.LastAccessTimeUtc.Value - entry.CreationTimeUtc.Value;
                writer.WriteCompact(lastAccessTimeOffset);
                return pooledWriter.Instance.Buffer.ToArray();
            }
        }

        /// <summary>
        /// Deserialize <see cref="ContentLocationEntry"/> from an array of bytes.
        /// </summary>
        protected ContentLocationEntry Deserialize(byte[] bytes)
        {
            using (PooledObjectWrapper<StreamBinaryReader> pooledReader = _readerPool.GetInstance())
            {
                var reader = pooledReader.Instance;
                return reader.Deserialize(new ArraySegment<byte>(bytes), r =>
                                                                         {
                                                                             var size = r.ReadInt64Compact();
                                                                             var locations = MachineIdSet.Deserialize(r);
                                                                             var creationTimeUtc = r.ReadUnixTime();
                                                                             var lastAccessTimeOffset = r.ReadInt64Compact();
                                                                             var lastAccessTime = new UnixTime(creationTimeUtc.Value + lastAccessTimeOffset);
                                                                             return ContentLocationEntry.Create(locations, size, lastAccessTime, creationTimeUtc);
                                                                         });
            }
        }

        /// <summary>
        /// Returns true a byte array deserialized into <see cref="ContentLocationEntry"/> would have <paramref name="machineId"/> index set.
        /// </summary>
        /// <remarks>
        /// This is an optimization that allows the clients to "poke" inside the value stored in the database without full deserialization.
        /// The approach is very useful in reconciliation scenarios, when the client wants to obtain content location entries for the current machine only.
        /// </remarks>
        public bool HasMachineId(byte[] bytes, int machineId)
        {
            using (var pooledObjectWrapper = _readerPool.GetInstance())
            {
                var pooledReader = pooledObjectWrapper.Instance;
                return pooledReader.Deserialize(
                    new ArraySegment<byte>(bytes),
                    machineId,
                    (localIndex, reader) =>
                    {
                        // It is very important for this lambda to be non-capturing, because it will be called
                        // many times.
                        // Avoiding allocations here severely affect performance during reconciliation.
                        _ = reader.ReadInt64Compact();
                        return MachineIdSet.HasMachineId(reader, localIndex);
                    });
            }
        }
    }
}
