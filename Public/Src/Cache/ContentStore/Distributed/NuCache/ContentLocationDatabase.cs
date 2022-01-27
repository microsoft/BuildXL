// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.InMemory;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Cache.ContentStore.Distributed.Tracing.TracingStructuredExtensions;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using TaskExtensions = BuildXL.Cache.ContentStore.Interfaces.Extensions.TaskExtensions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Interface for <see cref="ContentLocationDatabase"/>.
    /// </summary>
    public interface IContentLocationDatabase
    {
        /// <summary>
        /// Tries to locate an entry for a given hash.
        /// </summary>
        bool TryGetEntry(OperationContext context, ShortHash hash, [NotNullWhen(true)] out ContentLocationEntry? entry);
    }

    /// <summary>
    /// Base class that implements the core logic of <see cref="ContentLocationDatabase"/> interface.
    /// </summary>
    public abstract class ContentLocationDatabase : StartupShutdownSlimBase, IContentLocationDatabase
    {
        public override bool AllowMultipleStartupAndShutdowns => true;

        /// <nodoc />
        protected readonly SerializationPool SerializationPool = new SerializationPool();

        /// <nodoc />
        public readonly IClock Clock;

        /// <nodoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(ContentLocationDatabase)) { LogOperationStarted = false };

        /// <nodoc />
        public CounterCollection<ContentLocationDatabaseCounters> Counters { get; } = new CounterCollection<ContentLocationDatabaseCounters>();

        private readonly Func<IReadOnlyList<MachineId>> _getInactiveMachines;

        protected NagleQueue<(Context, IToStringConvertible entry, EntryOperation op, OperationReason reason)>? NagleOperationTracer;
        private readonly ContentLocationDatabaseConfiguration _configuration;

        protected bool IsDatabaseWriteable;

        private Timer? _gcTimer;
        private readonly object _garbageCollectionLock = new object();
        private bool _isContentGarbageCollectionEnabled;
        private bool _isMetadataGarbageCollectionEnabled;
        private Task _garbageCollectionTask = Task.CompletedTask;
        private CancellationTokenSource _garbageCollectionCts = new CancellationTokenSource();

        /// <summary>
        /// Fine-grained locks that is used for all operations that mutate records.
        /// </summary>
        private readonly object[] _locks = Enumerable.Range(0, ushort.MaxValue + 1).Select(s => new object()).ToArray();

        /// <summary>
        /// Event callback that's triggered when the database is permanently invalidated. 
        /// </summary>
        public Action<OperationContext, Failure<Exception>>? DatabaseInvalidated;

        /// <nodoc />
        protected void OnDatabaseInvalidated(OperationContext context, Failure<Exception> failure)
        {
            Contract.Requires(failure != null);
            // Notice that no update to the internal state is required when invalidation happens. By definition,
            // nothing can be done to this instance after invalidation: all incoming and ongoing operations should fail
            // (because it is triggered by RocksDb). The only way to resume operation is to reload from a checkpoint,
            // which resets the internal state correctly.
            DatabaseInvalidated?.Invoke(context, failure);
        }

        /// <nodoc />
        protected ContentLocationDatabase(IClock clock, ContentLocationDatabaseConfiguration configuration, Func<IReadOnlyList<MachineId>> getInactiveMachines)
        {
            Clock = clock;
            _configuration = configuration;
            _getInactiveMachines = getInactiveMachines;
        }

        /// <summary>
        /// Sets a key to a given value in the global info map
        /// </summary>
        public abstract void SetGlobalEntry(string key, string? value);

        /// <summary>
        /// Attempts to get a value from the global info map
        /// </summary>
        public abstract bool TryGetGlobalEntry(string key, [NotNullWhen(true)] out string? value);

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
        /// Prepares the database for read only or read/write mode. This operation assumes no operations are underway
        /// while running. It is the responsibility of the caller to ensure that is so.
        /// </summary>
        public async Task SetDatabaseModeAsync(bool isDatabaseWriteable)
        {
            _garbageCollectionCts.Cancel();
            await _garbageCollectionTask;

            lock (_garbageCollectionLock)
            {
                _isContentGarbageCollectionEnabled = isDatabaseWriteable;
                _isMetadataGarbageCollectionEnabled = isDatabaseWriteable && _configuration.MetadataGarbageCollectionEnabled;
                IsDatabaseWriteable = isDatabaseWriteable;
            }

            Interlocked.Exchange(ref _garbageCollectionCts, new CancellationTokenSource());
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            NagleOperationTracer = !_configuration.TraceOperations ? null : NagleQueue<(Context context, IToStringConvertible entry, EntryOperation op, OperationReason reason)>.Create(
                ops =>
                {
                    if (_configuration.UseContextualEntryOperationLogging)
                    {
                        foreach (var group in ops.GroupBy(static t => t.context, static t => (t.entry, t.op, t.reason)))
                        {
                            LogContentLocationOperations(
                                group.Key,
                                Tracer.Name,
                                group);
                        }
                    }
                    else
                    {
                        LogContentLocationOperations(
                            context.CreateNested(componentName: nameof(ContentLocationDatabase), caller: "LogContentLocationOperations"),
                            Tracer.Name,
                            ops.Select(static t => (t.entry, t.op, t.reason)));
                    }

                    return Unit.VoidTask;
                },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMinutes(1),
                batchSize: 10000);

            // GC will be triggered on this timer for all machines, irrespective of their role. However, work is only
            // performed when it needs to be (i.e. when the machine is the master). This is because the previous
            // approaches proved to be brittle and prone to programming mistakes.
            _gcTimer = new Timer(
                _ =>
                    PerformGarbageCollectionAsync(context)
                        .FireAndForgetErrorsAsync(context, operation: nameof(PerformGarbageCollectionAsync))
                        .GetAwaiter()
                        .GetResult(),
                null,
                _configuration.GarbageCollectionInterval,
                Timeout.InfiniteTimeSpan);

            return Task.FromResult(InitializeCore(context));
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            NagleOperationTracer?.Dispose();

            _garbageCollectionCts.Cancel();
            await _garbageCollectionTask;
            lock (_garbageCollectionLock)
            {
                _gcTimer?.Dispose();
            }

            return await base.ShutdownCoreAsync(context);
        }

        /// <nodoc />
        protected abstract BoolResult InitializeCore(OperationContext context);

        /// <summary>
        /// Tries to locate an entry for a given hash.
        /// </summary>
        public bool TryGetEntry(OperationContext context, ShortHash hash, [NotNullWhen(true)] out ContentLocationEntry? entry)
        {
            if (TryGetEntryCore(context, hash, out entry))
            {
                // Filtering the inactive machines if configured. Otherwise the filtering is happening in another layer.
                if (_configuration.FilterInactiveMachines)
                {
                    entry = FilterInactiveMachines(entry);
                }

                return true;
            }

            return false;
        }

        /// <nodoc />
        protected abstract IEnumerable<ShortHash> EnumerateSortedKeysFromStorage(OperationContext context);

        /// <summary>
        /// Enumeration filter used by <see cref="EnumerateEntriesWithSortedKeys"/> to filter out entries by raw value from a database.
        /// </summary>
        public class EnumerationFilter
        {
            /// <nodoc />
            public Func<ReadOnlyMemory<byte>, bool> ShouldEnumerate { get; }

            /// <nodoc />
            public ShortHash? StartingPoint { get; }

            /// <nodoc />
            public EnumerationFilter(Func<ReadOnlyMemory<byte>, bool> shouldEnumerate, ShortHash? startingPoint) =>
                (ShouldEnumerate, StartingPoint) = (shouldEnumerate, startingPoint);
        }

        /// <nodoc />
        protected abstract IEnumerable<(ShortHash key, ContentLocationEntry? entry)> EnumerateEntriesWithSortedKeysFromStorage(
            OperationContext context,
            EnumerationFilter? filter = null,
            bool returnKeysOnly = false);

        /// <summary>
        /// Gets a sequence of keys and values sorted by keys.
        /// </summary>
        public IEnumerable<(ShortHash key, ContentLocationEntry? entry)> EnumerateEntriesWithSortedKeys(
            OperationContext context,
            EnumerationFilter? filter = null)
        {
            return EnumerateEntriesWithSortedKeysFromStorage(context, filter);
        }

        private async Task PerformGarbageCollectionAsync(OperationContext context)
        {
            var nestedContext = context.CreateNested(componentName: nameof(ContentLocationDatabase), caller: nameof(GarbageCollectAsync));

            try
            {
                using (var nestedContextWithCancellation = nestedContext.WithCancellationToken(_garbageCollectionCts.Token))
                using (var operationContext = TrackShutdown(nestedContextWithCancellation))
                {
                    await _garbageCollectionTask;
#pragma warning disable AsyncFixer04 // Fire-and-forget async call inside a using block
                    var newGarbageCollectionTask = GarbageCollectAsync(operationContext);
#pragma warning restore AsyncFixer04 // Fire-and-forget async call inside a using block
                    _ = Interlocked.Exchange(ref _garbageCollectionTask, newGarbageCollectionTask);
                    (await newGarbageCollectionTask).IgnoreFailure();
                }
            }
            catch (Exception exception)
            {
                // Purposefully ignoring any exceptions here, since they'll just go into the Timer
                Tracer.Error(nestedContext, exception: exception, message: "Failed to run garbage collection", operation: nameof(GarbageCollectAsync));
            }
            finally
            {
                lock (_garbageCollectionLock)
                {
                    if (!ShutdownStarted)
                    {
                        _gcTimer?.Change(_configuration.GarbageCollectionInterval, Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }

        /// <summary>
        /// Collects entries with last access time longer then time to live.
        /// </summary>
        public virtual Task<BoolResult> GarbageCollectAsync(OperationContext context)
        {
            bool gcMetadata = false;
            bool gcContent = false;

            return context.PerformOperationAsync(Tracer,
                async () =>
                {
                    lock (_garbageCollectionLock)
                    {
                        gcMetadata = _isMetadataGarbageCollectionEnabled;
                        gcContent = _isContentGarbageCollectionEnabled;
                    }

                    if (!gcMetadata && !gcContent)
                    {
                        Tracer.Debug(context, "No garbage collection round run");
                        return BoolResult.Success;
                    }

                    var metadataGcResult = BoolResult.SuccessTask;
                    if (gcMetadata)
                    {
                        metadataGcResult = TaskExtensions.Run(
                            () => GarbageCollectMetadata(context),
                            inline: !_configuration.GarbageCollectionConcurrent);
                    }

                    // Metadata GC may take a while. During that time, we may have switched roles. Hence, we need to
                    // recheck if we have to run content GC.
                    var contentGcResult = BoolResult.SuccessTask;
                    lock (_garbageCollectionLock)
                    {
                        gcContent = _isContentGarbageCollectionEnabled;
                    }

                    if (gcContent)
                    {
                        contentGcResult = TaskExtensions.Run(
                            () => GarbageCollectContent(context),
                            inline: !_configuration.GarbageCollectionConcurrent);
                    }

                    await Task.WhenAll(metadataGcResult, contentGcResult);

                    return await metadataGcResult & await contentGcResult;
                },
                counter: Counters[ContentLocationDatabaseCounters.GarbageCollect],
                extraEndMessage: r => $"GarbageCollectMetadata=[{gcMetadata}] GarbageCollectContent=[{gcContent}]",
                isCritical: true);
        }

        /// <summary>
        /// Collect unreachable entries from the local database.
        /// </summary>
        private BoolResult GarbageCollectContent(OperationContext context)
        {
            return context.PerformOperation(Tracer,
                () => GarbageCollectContentCore(context),
                counter: Counters[ContentLocationDatabaseCounters.GarbageCollectContent],
                isCritical: true);
        }

        // Iterate over all content in DB, for each hash removing locations known to
        // be inactive, and removing hashes with no locations.
        private BoolResult GarbageCollectContentCore(OperationContext context)
        {
            // Counters for work done.
            long removedEntries = 0;
            long totalEntries = 0;
            long uniqueContentSize = 0;
            long totalContentCount = 0;
            long totalContentSize = 0;
            long uniqueContentCount = 0;

            // Tracking the difference between sequence of hashes for diagnostic purposes. We need to know how good short hashes are and how close are we to collisions. 
            ShortHash? lastHash = null;
            int maxHashFirstByteDifference = 0;

            long[] totalSizeByLogSize = new long[64];
            long[] uniqueSizeByLogSize = new long[totalSizeByLogSize.Length];
            int[] countsByLogSize = new int[totalSizeByLogSize.Length];

            // Enumerate over all hashes...
            // NOTE: GC will query for the value itself and thereby get the value from the in memory cache if present.
            // It will NOT necessarily enumerate all keys in the in memory cache since they may be new keys but GC is
            // fine to just handle those on the next GC iteration.
            foreach (var hash in EnumerateSortedKeysFromStorage(context))
            {
                if (context.Token.IsCancellationRequested)
                {
                    break;
                }

                if (!TryGetEntryCore(context, hash, out var entry))
                {
                    continue;
                }

                // Update counters.
                int replicaCount = entry.Locations.Count;
                uniqueContentCount++;
                uniqueContentSize += entry.ContentSize;
                totalContentSize += entry.ContentSize * replicaCount;
                totalContentCount += replicaCount;

                int logSize = (int)Math.Log(Math.Max(1, entry.ContentSize), 2);
                countsByLogSize[logSize]++;
                totalSizeByLogSize[logSize] += entry.ContentSize * replicaCount;
                uniqueSizeByLogSize[logSize] += entry.ContentSize;

                // Filter out inactive machines.
                var filteredEntry = FilterInactiveMachines(entry);

                // Decide if we ought to modify the entry.
                if (filteredEntry.Locations.Count == 0 || filteredEntry.Locations.Count != entry.Locations.Count)
                {
                    // Use double-checked locking to usually avoid locking, but still
                    // be safe in case we are in a race to update content location data.
                    lock (GetLock(hash))
                    {
                        if (!TryGetEntryCore(context, hash, out entry))
                        {
                            continue;
                        }
                        filteredEntry = FilterInactiveMachines(entry);

                        if (filteredEntry.Locations.Count == 0)
                        {
                            // If there are no good locations, remove the entry.
                            removedEntries++;
                            Counters[ContentLocationDatabaseCounters.TotalNumberOfCollectedEntries].Increment();
                            Delete(context, hash);
                            LogEntryDeletion(context, hash, OperationReason.GarbageCollect, entry.ContentSize);
                        }
                        else if (filteredEntry.Locations.Count != entry.Locations.Count)
                        {
                            // If there are some bad locations, remove them.
                            Counters[ContentLocationDatabaseCounters.TotalNumberOfCleanedEntries].Increment();
                            Store(context, hash, filteredEntry);
                            NagleOperationTracer?.Enqueue((context, hash, EntryOperation.RemoveMachine, OperationReason.GarbageCollect));
                        }
                    }
                }

                totalEntries++;

                // Some logic to try to measure how "close" short hashes get.
                // dawright: I don't think this works, because hashes could be very close (e.g. all same in low-order bits)
                // and yet still be very far away when ordered (e.g. high-order bits differ), and we only compare
                // neighbors in ordered list. But I'm leaving it for now because it's orthogonal to my current change.
                if (lastHash != null && lastHash != hash)
                {
                    maxHashFirstByteDifference = Math.Max(maxHashFirstByteDifference, GetFirstByteDifference(lastHash.Value, hash));
                }
                lastHash = hash;
            }

            Counters[ContentLocationDatabaseCounters.TotalNumberOfScannedEntries].Add(uniqueContentCount);

            Tracer.Debug(context, $"Overall DB Stats: UniqueContentCount={uniqueContentCount}, UniqueContentSize={uniqueContentSize}, "
                + $"TotalContentCount={totalContentCount}, TotalContentSize={totalContentSize}, MaxHashFirstByteDifference={maxHashFirstByteDifference}"
                + $", UniqueContentAddedSize={Counters[ContentLocationDatabaseCounters.UniqueContentAddedSize].Value}"
                + $", TotalNumberOfCreatedEntries={Counters[ContentLocationDatabaseCounters.TotalNumberOfCreatedEntries].Value}"
                + $", TotalContentAddedSize={Counters[ContentLocationDatabaseCounters.TotalContentAddedSize].Value}"
                + $", TotalContentAddedCount={Counters[ContentLocationDatabaseCounters.TotalContentAddedCount].Value}"
                + $", UniqueContentRemovedSize={Counters[ContentLocationDatabaseCounters.UniqueContentRemovedSize].Value}"
                + $", TotalNumberOfDeletedEntries={Counters[ContentLocationDatabaseCounters.TotalNumberOfDeletedEntries].Value}"
                + $", TotalContentRemovedSize={Counters[ContentLocationDatabaseCounters.TotalContentRemovedSize].Value}"
                + $", TotalContentRemovedCount={Counters[ContentLocationDatabaseCounters.TotalContentRemovedCount].Value}"
                );

            for (int logSize = 0; logSize < countsByLogSize.Length; logSize++)
            {
                if (countsByLogSize[logSize] != 0)
                {
                    Tracer.Debug(context, $"DB Content Stat: Log2_Size={logSize}, Count={countsByLogSize[logSize]}, UniqueSize={uniqueSizeByLogSize[logSize]}, TotalSize={totalSizeByLogSize[logSize]}, IsComplete={!context.Token.IsCancellationRequested}");
                }
            }

            Tracer.GarbageCollectionFinished(
                context,
                Counters[ContentLocationDatabaseCounters.GarbageCollectContent].Duration,
                totalEntries,
                removedEntries,
                Counters[ContentLocationDatabaseCounters.TotalNumberOfCollectedEntries].Value,
                uniqueContentCount,
                uniqueContentSize,
                totalContentCount,
                totalContentSize);

            return BoolResult.Success;
        }

        /// <nodoc />
        protected struct MetadataGarbageCollectionOutput
        {
            /// <nodoc />
            public long Scanned;

            /// <nodoc />
            public long Removed;

            /// <summary>
            /// NOTE: This estimate, and the one below, are just estimates. They can be (and often are) arbitrarily
            /// wrong. They are here just for tracing and as one more data point when things go wrong in production.
            /// </summary>
            public long MetadataCFSizeBeforeGcBytes;

            /// <summary>
            /// NOTE: Metadata CF size may potentially increase after GC. This is because GC adds tombstones, in
            /// amounts that may be significant.
            /// </summary>
            public long MetadataCFSizeAfterGcBytes;

            /// <nodoc />
            public bool KillSwitch;

            /// <nodoc />
            public override string ToString()
            {
                return $"Scanned=[{Scanned}] Removed=[{Removed}] MetadataCFSizeBeforeGcBytes=[{MetadataCFSizeBeforeGcBytes}] MetadataCFSizeAfterGcBytes=[{MetadataCFSizeAfterGcBytes}] KillSwitch=[{KillSwitch}]";
            }
        }

        /// <summary>
        /// Perform garbage collection of metadata entries.
        /// </summary>
        private BoolResult GarbageCollectMetadata(OperationContext context)
        {
            return context.PerformOperation(Tracer,
                () => GarbageCollectMetadataCore(context),
                counter: Counters[ContentLocationDatabaseCounters.GarbageCollectMetadata],
                messageFactory: result => result.Select(output => output.ToString()).GetValueOrDefault(string.Empty)!,
                isCritical: true);
        }

        /// <nodoc />
        protected virtual Result<MetadataGarbageCollectionOutput> GarbageCollectMetadataCore(OperationContext context)
        {
            return Result.FromErrorMessage<MetadataGarbageCollectionOutput>(message: "Metadata GC is not implemented");
        }

        private int GetFirstByteDifference(in ShortHash hash1, in ShortHash hash2)
        {
            for (int i = 0; i < ShortHash.HashLength; i++)
            {
                if (hash1[i] != hash2[i])
                {
                    return i;
                }
            }

            return ShortHash.HashLength;
        }

        private ContentLocationEntry FilterInactiveMachines(ContentLocationEntry entry)
        {
            var inactiveMachines = _getInactiveMachines();
            return entry.SetMachineExistence(MachineIdCollection.Create(inactiveMachines), exists: false);
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
                return context.PerformOperation(Tracer,
                    () => SaveCheckpointCore(context, checkpointDirectory),
                    extraStartMessage: $"CheckpointDirectory=[{checkpointDirectory}]",
                    messageFactory: _ => $"CheckpointDirectory=[{checkpointDirectory}]");
            }
        }

        /// <nodoc />
        protected abstract BoolResult SaveCheckpointCore(OperationContext context, AbsolutePath checkpointDirectory);

        /// <nodoc/>
        public BoolResult RestoreCheckpoint(OperationContext context, AbsolutePath checkpointDirectory)
        {
            using (Counters[ContentLocationDatabaseCounters.RestoreCheckpoint].Start())
            {
                return context.PerformOperation(Tracer,
                    () => RestoreCheckpointCore(context, checkpointDirectory),
                    extraStartMessage: $"CheckpointDirectory=[{checkpointDirectory}]",
                    messageFactory: _ => $"CheckpointDirectory=[{checkpointDirectory}]");
            }
        }

        /// <nodoc />
        protected abstract BoolResult RestoreCheckpointCore(OperationContext context, AbsolutePath checkpointDirectory);

        /// <nodoc />
        protected abstract bool TryGetEntryCoreFromStorage(OperationContext context, ShortHash hash, [NotNullWhen(true)] out ContentLocationEntry? entry);

        /// <nodoc />
        protected bool TryGetEntryCore(OperationContext context, ShortHash hash, [NotNullWhen(true)] out ContentLocationEntry? entry)
        {
            Counters[ContentLocationDatabaseCounters.NumberOfGetOperations].Increment();

            return TryGetEntryCoreFromStorage(context, hash, out entry);
        }

        /// <nodoc />
        internal abstract void Persist(OperationContext context, ShortHash hash, ContentLocationEntry? entry);

        /// <nodoc />
        public void Store(OperationContext context, ShortHash hash, ContentLocationEntry? entry)
        {
            Counters[ContentLocationDatabaseCounters.NumberOfStoreOperations].Increment();
            CacheActivityTracker.AddValue(CaSaaSActivityTrackingCounters.ProcessedHashes, value: 1);
            Persist(context, hash, entry);
        }

        /// <nodoc />
        protected void Delete(OperationContext context, ShortHash hash)
        {
            Store(context, hash, entry: null);
        }

        private (ContentLocationEntry entry, bool entryHasChanged) SetMachineExistenceAndUpdateDatabase(OperationContext context, ShortHash hash, MachineId? machine, bool existsOnMachine, long size, UnixTime? lastAccessTime, bool reconciling)
        {
            var created = false;
            var reason = reconciling ? OperationReason.Reconcile : OperationReason.Unknown;
            lock (GetLock(hash))
            {
                if (TryGetEntryCore(context, hash, out var entry))
                {
                    var initialEntry = entry;

                    // Don't update machines if entry already contains the machine
                    MachineIdCollection machines = machine != null && (entry.Locations[machine.Value] != existsOnMachine)
                        ? MachineIdCollection.Create(machine.Value)
                        : MachineIdCollection.Empty;

                    // Don't update last access time if the touch frequency interval has not elapsed since last access
                    if (lastAccessTime != null && initialEntry.LastAccessTimeUtc.ToDateTime().IsRecent(lastAccessTime.Value.ToDateTime(), _configuration.TouchFrequency))
                    {
                        lastAccessTime = null;
                    }

                    entry = entry.SetMachineExistence(machines, existsOnMachine, lastAccessTime, size: size >= 0 ? (long?)size : null);

                    if (entry == initialEntry)
                    {
                        if (_configuration.TraceNoStateChangeOperations)
                        {
                            NagleOperationTracer?.Enqueue((context, hash, existsOnMachine ? EntryOperation.AddMachineNoStateChange : EntryOperation.RemoveMachineNoStateChange, reason));
                        }

                        // The entry is unchanged.
                        return (initialEntry, entryHasChanged: false);
                    }

                    EntryOperation entryOperation;
                    if (existsOnMachine)
                    {
                        entryOperation = initialEntry.Locations.Count == entry.Locations.Count ? EntryOperation.Touch : EntryOperation.AddMachine;
                    }
                    else
                    {
                        entryOperation = machine == null ? EntryOperation.Touch : EntryOperation.RemoveMachine;
                    }

                    // Not tracing touches if configured.
                    if (_configuration.TraceTouches || entryOperation != EntryOperation.Touch)
                    {
                        NagleOperationTracer?.Enqueue((context, hash, entryOperation, reason));
                    }
                }
                else
                {
                    if (!existsOnMachine || machine == null)
                    {
                        if (_configuration.TraceNoStateChangeOperations)
                        {
                            NagleOperationTracer?.Enqueue((context, hash, EntryOperation.RemoveOnUnknownMachine, reason));
                        }

                        // Attempting to remove a machine from or touch a missing entry should result in no changes
                        return (ContentLocationEntry.Missing, entryHasChanged: false);
                    }

                    lastAccessTime ??= Clock.UtcNow;
                    var creationTime = UnixTime.Min(lastAccessTime.Value, Clock.UtcNow.ToUnixTime());

                    entry = ContentLocationEntry.Create(MachineIdSet.Empty.SetExistence(MachineIdCollection.Create(machine.Value), existsOnMachine), size, lastAccessTime.Value, creationTime);
                    created = true;
                }

                if (machine != null)
                {
                    if (existsOnMachine)
                    {
                        Counters[ContentLocationDatabaseCounters.TotalContentAddedCount].Increment();
                        Counters[ContentLocationDatabaseCounters.TotalContentAddedSize].Add(entry.ContentSize);
                    }
                    else
                    {
                        Counters[ContentLocationDatabaseCounters.TotalContentRemovedCount].Increment();
                        Counters[ContentLocationDatabaseCounters.TotalContentRemovedSize].Add(entry.ContentSize);
                    }
                }

                if (entry.Locations.Count == 0)
                {
                    // Remove the hash when no more locations are registered
                    Delete(context, hash);
                    LogEntryDeletion(context, hash, reason, entry.ContentSize);
                }
                else
                {
                    Store(context, hash, entry);

                    if (created)
                    {
                        Counters[ContentLocationDatabaseCounters.TotalNumberOfCreatedEntries].Increment();
                        Counters[ContentLocationDatabaseCounters.UniqueContentAddedSize].Add(entry.ContentSize);
                        NagleOperationTracer?.Enqueue((context, hash, EntryOperation.Create, reason));
                    }
                }

                return (entry, entryHasChanged: true);
            }
        }

        private void LogEntryDeletion(Context context, ShortHash hash, OperationReason reason, long size)
        {
            Counters[ContentLocationDatabaseCounters.TotalNumberOfDeletedEntries].Increment();
            Counters[ContentLocationDatabaseCounters.UniqueContentRemovedSize].Add(size);
            NagleOperationTracer?.Enqueue((context, hash, EntryOperation.Delete, reason));
        }

        /// <summary>
        /// Performs an upsert operation on metadata, while ensuring all invariants are kept. If the
        /// fingerprint is not present, then it is inserted. If fingerprint is present, predicate is used to
        /// specify whether to perform the update.
        /// </summary>
        /// <returns>
        /// Result providing the call's completion status. True if the replacement was completed successfully,
        /// false otherwise.
        /// </returns>
        public abstract Possible<bool> TryUpsert(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism replacement,
            Func<MetadataEntry, bool> shouldReplace,
            DateTime? lastAccessTimeUtc);

        /// <summary>
        /// Load a ContentHashList.
        /// </summary>
        public abstract Result<MetadataEntry?> GetMetadataEntry(OperationContext context, StrongFingerprint strongFingerprint, bool touch);

        /// <summary>
        /// Load a ContentHashList.
        /// </summary>
        public GetContentHashListResult GetContentHashList(OperationContext context, StrongFingerprint strongFingerprint)
        {
            var result = GetMetadataEntry(context, strongFingerprint, touch: true);
            return result.Succeeded
                ? new GetContentHashListResult(result.Value?.ContentHashListWithDeterminism ?? new ContentHashListWithDeterminism(null, CacheDeterminism.None))
                : new GetContentHashListResult(result);
        }

        /// <summary>
        /// Gets known selectors for a given weak fingerprint.
        /// </summary>
        public abstract Result<IReadOnlyList<Selector>> GetSelectors(OperationContext context, Fingerprint weakFingerprint);

        /// <summary>
        /// Enumerates all strong fingerprints currently stored in the cache.
        /// </summary>
        /// <remarks>
        ///     Warning: this function should only ever be used on tests.
        /// </remarks>
        public abstract IEnumerable<Result<StrongFingerprint>> EnumerateStrongFingerprints(OperationContext context);

        private object GetLock(ShortHash hash)
        {
            // NOTE: We choose not to use "random" two bytes of the hash because
            // otherwise GC which uses an ordered set of hashes would acquire the same
            // lock over and over again potentially freezing out writers
            return _locks[hash[6] << 8 | hash[3]];
        }

        /// <nodoc />
        public bool LocationAdded(OperationContext context, ShortHash hash, MachineId machine, long size, bool reconciling = false, bool updateLastAccessTime = true)
        {
            using (Counters[ContentLocationDatabaseCounters.LocationAdded].Start())
            {
                return SetMachineExistenceAndUpdateDatabase(context, hash, machine, existsOnMachine: true, size: size, lastAccessTime: updateLastAccessTime ? Clock.UtcNow : (DateTime?)null, reconciling: reconciling).entryHasChanged;
            }
        }

        /// <nodoc />
        public bool LocationRemoved(OperationContext context, ShortHash hash, MachineId machine, bool reconciling = false)
        {
            using (Counters[ContentLocationDatabaseCounters.LocationRemoved].Start())
            {
                return SetMachineExistenceAndUpdateDatabase(context, hash, machine, existsOnMachine: false, size: -1, lastAccessTime: null, reconciling: reconciling).entryHasChanged;
            }
        }

        /// <nodoc />
        public bool ContentTouched(OperationContext context, ShortHash hash, UnixTime accessTime)
        {
            using (Counters[ContentLocationDatabaseCounters.ContentTouched].Start())
            {
                return SetMachineExistenceAndUpdateDatabase(context, hash, machine: null, existsOnMachine: false, -1, lastAccessTime: accessTime, reconciling: false).entryHasChanged;
            }
        }

        /// <summary>
        /// Serialize a given <paramref name="entry"/> into a byte stream.
        /// </summary>
        protected PooledBuffer SerializeContentLocationEntry(ContentLocationEntry entry)
        {
            return SerializationPool.SerializePooled(entry, static (instance, writer) => instance.Serialize(writer));
        }

        /// <summary>
        /// Deserialize <see cref="ContentLocationEntry"/> from an array of bytes.
        /// </summary>
        protected ContentLocationEntry DeserializeContentLocationEntry(ReadOnlyMemory<byte> bytes)
        {
            // Please do not convert the delegate to a method group, because this code is called many times
            // and method group allocates a delegate on each conversion to a delegate.
            return SerializationPool.Deserialize(bytes, static reader => ContentLocationEntry.Deserialize(reader));
        }

        /// <summary>
        /// Returns true a byte array deserialized into <see cref="ContentLocationEntry"/> would have <paramref name="machineId"/> index set.
        /// </summary>
        /// <remarks>
        /// This is an optimization that allows the clients to "poke" inside the value stored in the database without full deserialization.
        /// The approach is very useful in reconciliation scenarios, when the client wants to obtain content location entries for the current machine only.
        /// </remarks>
        public bool HasMachineId(ReadOnlyMemory<byte> bytes, int machineId)
        {
            return SerializationPool.Deserialize(
                bytes,
                machineId,
                static (localIndex, reader) =>
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
