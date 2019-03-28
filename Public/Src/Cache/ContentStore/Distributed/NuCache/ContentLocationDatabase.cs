// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.InMemory;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Cache.ContentStore.Distributed.Tracing.TracingStructuredExtensions;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Base class that implements the core logic of <see cref="ContentLocationDatabase"/> interface.
    /// </summary>
    public abstract class ContentLocationDatabase : StartupShutdownSlimBase
    {
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

        /// <summary>
        /// Configures the behavior of the database's garbage collection
        /// </summary>
        public void ConfigureGarbageCollection(bool shouldDoGc)
        {
            if (_isGarbageCollectionEnabled != shouldDoGc)
            {
                _isGarbageCollectionEnabled = shouldDoGc;
                var nextGcTimeSpan = _isGarbageCollectionEnabled ? _configuration.LocalDatabaseGarbageCollectionInterval : Timeout.InfiniteTimeSpan;
                _gcTimer?.Change(nextGcTimeSpan, Timeout.InfiniteTimeSpan);
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

        /// <summary>
        /// Gets a sequence of keys.
        /// </summary>
        public abstract IEnumerable<ShortHash> EnumerateSortedKeys(CancellationToken token);

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
                if (!ShutdownStarted && _isGarbageCollectionEnabled)
                {
                    _gcTimer?.Change(_configuration.LocalDatabaseGarbageCollectionInterval, Timeout.InfiniteTimeSpan);
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

                foreach (var hash in EnumerateSortedKeys(context.Token))
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
        /// Gets whether the file in the database's checkpoint directory is immutable between checkpoints (i.e. files with the same name will have the same content)
        /// </summary>
        public abstract bool IsImmutable(AbsolutePath dbFile);

        /// <nodoc/>
        public BoolResult SaveCheckpoint(OperationContext context, AbsolutePath checkpointDirectory)
        {
            using (Counters[ContentLocationDatabaseCounters.SaveCheckpoint].Start())
            {
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
        protected abstract bool TryGetEntryCore(OperationContext context, ShortHash hash, out ContentLocationEntry entry);

        /// <nodoc />
        protected abstract void Store(OperationContext context, ShortHash hash, ContentLocationEntry entry);

        /// <nodoc />
        protected abstract void Delete(OperationContext context, ShortHash hash);

        private ContentLocationEntry SetMachineExistenceAndUpdateDatabase(OperationContext context, ShortHash hash, MachineId? machine, bool existsOnMachine, long size, UnixTime? lastAccessTime, bool reconciling)
        {
            bool created = false;
            OperationReason reason = reconciling ? OperationReason.Reconcile : OperationReason.Unknown;
            int priorLocationCount = 0;
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
    }
}
