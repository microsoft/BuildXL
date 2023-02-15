// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore.Sketching;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.ConfigurationHelpers;
using BuildXL.Utilities.Serialization;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using RocksDbSharp;
using static BuildXL.Cache.ContentStore.Distributed.Tracing.TracingStructuredExtensions;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// RocksDb-based version of <see cref="ContentLocationDatabase"/>.
    /// </summary>
    public sealed class RocksDbContentLocationDatabase : ContentLocationDatabase
    {
        private readonly RocksDbContentLocationDatabaseConfiguration _configuration;

        private KeyValueStoreGuard _keyValueStore;
        private const string ActiveStoreSlotFileName = "activeSlot.txt";
        private StoreSlot _activeSlot = StoreSlot.Slot1;
        private string? _storeLocation;
        private readonly string _activeSlotFilePath;

        private enum StoreSlot
        {
            Slot1,
            Slot2
        }

        /// <summary>
        /// There's multiple column families in this usage of RocksDB.
        ///
        /// The default column family is used to store a <see cref="ContentHash"/> to <see cref="ContentLocationEntry"/> mapping, which has been
        /// the usage since this started.
        ///
        /// All others are documented below.
        /// </summary>
        private enum Columns
        {
            ClusterState,
            /// <summary>
            /// Stores mapping from <see cref="StrongFingerprint"/> to a <see cref="ContentHashList"/>. This allows us
            /// to look up via a <see cref="Fingerprint"/>, or a <see cref="StrongFingerprint"/>. The only reason we
            /// can look up by <see cref="Fingerprint"/> is that it is stored as a prefix to the
            /// <see cref="StrongFingerprint"/>.
            ///
            /// What we effectively store is not a <see cref="ContentHashList"/>, but a <see cref="MetadataEntry"/>,
            /// which contains all information relevant to the database.
            ///
            /// This serves all of CaChaaS' needs for storage, modulo garbage collection.
            /// </summary>
            Metadata,
        }

        private enum ClusterStateKeys
        {
            StoredEpoch
        }

        /// <inheritdoc />
        public RocksDbContentLocationDatabase(IClock clock, RocksDbContentLocationDatabaseConfiguration configuration, Func<IReadOnlyList<MachineId>> getInactiveMachines)
            : base(clock, configuration, getInactiveMachines)
        {
            _configuration = configuration;
            _activeSlotFilePath = (_configuration.StoreLocation / ActiveStoreSlotFileName).ToString();

            // this is a hacky way to convince the compiler that the field is initialized.
            // Technically, the field is nullable, but keeping it as nullable causes more issues than giving us benefits.
            _keyValueStore = null!;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _keyValueStore?.Dispose();

            return base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        protected override BoolResult InitializeCore(OperationContext context)
        {
            var result = InitialLoad(context, GetActiveSlot(context.TracingContext));
            if (result)
            {
                if (_configuration.TestInitialCheckpointPath != null)
                {
                    return RestoreCheckpoint(context, _configuration.TestInitialCheckpointPath);
                }

                // We only create the timers within the Startup method. It is expected that users will call
                // SetDatabaseMode before proceeding to use the database, as that method will actually start the timers.
            }

            return result;
        }

        private BoolResult InitialLoad(OperationContext context, StoreSlot activeSlot)
        {
            var clean = _configuration.CleanOnInitialize;
            var result = Load(context, activeSlot, clean);

            bool reload = false;

            if (!clean)
            {
                if (result.Succeeded)
                {
                    if (IsStoredEpochInvalid(out var epoch))
                    {
                        Counters[ContentLocationDatabaseCounters.EpochMismatches].Increment();
                        Tracer.Debug(context, $"Stored epoch '{epoch}' does not match configured epoch '{_configuration.Epoch}'. Retrying with clean=true.");
                        reload = true;
                    }
                    else
                    {
                        Counters[ContentLocationDatabaseCounters.EpochMatches].Increment();
                    }
                }

                if (!result.Succeeded)
                {
                    Tracer.Warning(context, $"Failed to load database without cleaning. Retrying with clean=true. Failure: {result}");
                    reload = true;
                }
            }

            if (reload)
            {
                // If failed when cleaning is disabled, try again with forcing a clean
                return Load(context, GetNextSlot(activeSlot), clean: true);
            }

            return result;
        }

        private bool IsStoredEpochInvalid([NotNullWhen(true)] out string? epoch)
        {
            TryGetGlobalEntry(nameof(ClusterStateKeys.StoredEpoch), out epoch);
            return _configuration.Epoch != epoch;
        }

        private static RocksDbPerformanceConfiguration ToKeyValueStoreSettings(Host.Configuration.RocksDbPerformanceSettings configuration)
        {
            var result = new RocksDbPerformanceConfiguration();

            result = (configuration.CompactionDegreeOfParallelism?.ProcessorCountMultiplier).ApplyIfNotNull(result, static (result, value) => result with { BackgroundCompactionCoreMultiplier = value});
            result = (configuration.CompactionDegreeOfParallelism?.ThreadCount).ApplyIfNotNull(result, static (result, value) => result with { BackgroundCompactionThreadCount = value});
            result = configuration.MaxBackgroundCompactions.ApplyIfNotNull(result, static (result, value) => result with { MaxBackgroundCompactions = value});
            result = configuration.MaxBackgroundFlushes.ApplyIfNotNull(result, static (result, value) => result with { MaxBackgroundFlushes = value});
            result = configuration.MaxSubCompactions.ApplyIfNotNull(result, static (result, value) => result with { MaxSubCompactions = value});

            result = result with { ColumnFamilyPerformanceSettings = configuration.ColumnFamilySettings.ToDictionary(static kvp => kvp.Key, static kvp => fromSettings(kvp.Value)) };
            return result;

            static ColumnFamilyPerformanceConfiguration fromSettings(RocksDbColumnFamilyPerformanceSettings settings)
            {
                var r = new ColumnFamilyPerformanceConfiguration();
                r = settings.ColumnFamilyLevel0SlowdownWritesTrigger.ApplyIfNotNull(r, static (r, value) => r with { ColumnFamilyLevel0SlowdownWritesTrigger = value });
                r = settings.ColumnFamilyLevel0StopWritesTrigger.ApplyIfNotNull(r, static (r, value) => r with { ColumnFamilyLevel0StopWritesTrigger = value });
                r = settings.ColumnFamilyWriteBufferSize.ApplyIfNotNull(r, static (r, value) => r with { ColumnFamilyWriteBufferSize = value });
                r = settings.MaxBytesForLevelBase.ApplyIfNotNull(r, static (r, value) => r with { MaxBytesForLevelBase = value });
                return r;
            }
        }

        private BoolResult Load(OperationContext context, StoreSlot activeSlot, bool clean)
        {
            try
            {
                var storeLocation = GetStoreLocation(activeSlot);

                if (clean)
                {
                    Counters[ContentLocationDatabaseCounters.DatabaseCleans].Increment();

                    if (Directory.Exists(storeLocation))
                    {
                        FileUtilities.DeleteDirectoryContents(storeLocation, deleteRootDirectory: true);
                    }
                }

                bool dbAlreadyExists = Directory.Exists(storeLocation);
                Directory.CreateDirectory(storeLocation);

                Tracer.Info(context, $"Creating RocksDb store at '{storeLocation}'. Clean={clean}, UseMergeOperators={_configuration.UseMergeOperatorForContentLocations}, Configured Epoch='{_configuration.Epoch}', TracingLevel={_configuration.RocksDbTracingLevel}");

                var settings = new RocksDbStoreConfiguration(storeLocation)
                {
                    AdditionalColumns = new[] { nameof(Columns.ClusterState), nameof(Columns.Metadata) },
                    RotateLogsMaxFileSizeBytes = (ulong)"1MB".ToSize(),
                    RotateLogsNumFiles = 10,
                    RotateLogsMaxAge = TimeSpan.FromHours(3),
                    EnableStatistics = true,
                    FastOpen = true,
                    // We take the user's word here. This may be completely wrong, but we don't have enough
                    // information at this point to take a decision here. If a machine is master and demoted to
                    // worker, EventHub may continue to process events for a little while. If we set this to
                    // read-only during that checkpoint, those last few events will fail with RocksDbException.
                    // NOTE: we need to check that the database exists because RocksDb will refuse to open an empty
                    // read-only instance.
                    ReadOnly = _configuration.OpenReadOnly && dbAlreadyExists,
                    // The RocksDb database here is read-only from the perspective of the default column family,
                    // but read/write from the perspective of the ClusterState (which is rewritten on every
                    // heartbeat). This means that the database may perform background compactions on the column
                    // families, possibly triggering a RocksDb corruption "block checksum mismatch" error.
                    // Since the writes to ClusterState are relatively few, we can make-do with disabling
                    // compaction here and pretending like we are using a read-only database.
                    DisableAutomaticCompactions = !IsDatabaseWriteable,
                    LeveledCompactionDynamicLevelTargetSizes = true,
                    Compression = Compression.Zstd,
                    UseReadOptionsWithSetTotalOrderSeekInDbEnumeration = true,
                    UseReadOptionsWithSetTotalOrderSeekInGarbageCollection = true,
                };

                RocksDbUtilities.ConfigureRocksDbTracingIfNeeded(context, _configuration, settings, Tracer, componentName: nameof(RocksDbContentLocationDatabase));

                if (_configuration.UseMergeOperatorForContentLocations)
                {
                    var mergeContext = context.CreateNested(nameof(RocksDbContentLocationDatabase), caller: "MergeContentLocationEntries");
                    
                    settings.MergeOperators.Add(
                        ColumnFamilies.DefaultName,
                        MergeOperators.CreateAssociative(
                            "ContentLocationEntryMergeOperator",
                            (key, value1, value2, result) =>
                                MergeContentLocationEntries(mergeContext, value1, value2, result))
                        );
                }

                Tracer.Debug(context, $"RocksDb performance settings: {(_configuration.RocksDbPerformanceSettings?.ToString() ?? "null")}");
                if (_configuration.RocksDbPerformanceSettings != null)
                {
                    // Tracing the perf configuration separately to make sure the final settings are correct and there is no issue in the configuration translation layer.
                    settings.PerformanceConfiguration = ToKeyValueStoreSettings(_configuration.RocksDbPerformanceSettings);
                    Tracer.Debug(context, $"RocksDb-level performance settings: {settings.PerformanceConfiguration}");
                }

                var possibleStore = KeyValueStoreAccessor.Open(
                    settings,
                    // When an exception is caught from within methods using the database, this handler is called to
                    // decide whether the exception should be rethrown in user code, and the database invalidated. Our
                    // policy is to only invalidate if it is an exception coming from RocksDb, but not from our code.
                    failureHandler: failureEvent =>
                    {
                        // By default, rethrow is true iff it is a user error. We invalidate only if it isn't
                        failureEvent.Invalidate = !failureEvent.Rethrow;
                    },
                    // The database may be invalidated for a number of reasons, all related to latent bugs in our code.
                    // For example, exceptions thrown from methods that are operating on the DB. If that happens, we
                    // call a user-defined handler. This is because the instance is invalid after this happens.
                    invalidationHandler: failure => OnDatabaseInvalidated(context, failure),
                    // It is possible we may fail to open an already existing database. This can happen (most commonly)
                    // due to corruption, among others. If this happens, then we want to recreate it from empty. This
                    // only helps for the memoization store.
                    onFailureDeleteExistingStoreAndRetry: _configuration.OnFailureDeleteExistingStoreAndRetry,
                    // If the previous flag is true, and it does happen that we invalidate the database, we want to log
                    // it explicitly.
                    onStoreReset: failure =>
                    {
                        Tracer.Error(context, $"RocksDb critical error caused store to reset: {failure.DescribeIncludingInnerFailures()}");
                    });

                if (possibleStore.Succeeded)
                {
                    if (ShutdownStarted)
                    {
                        // If the shutdown already started (and this is possible due to something like background restore)
                        // we should immediately close the newly opened store to avoid resource leaks.

                        Tracer.Debug(context, "Closing a newly opened store because the shutdown was requested");
                        possibleStore.Result.Dispose();
                        return BoolResult.Success;
                    }

                    var oldKeyValueStore = _keyValueStore;
                    var store = possibleStore.Result;

                    if (oldKeyValueStore == null)
                    {
                        _keyValueStore = new KeyValueStoreGuard(store);
                    }
                    else
                    {
                        // Just replace the inner accessor
                        oldKeyValueStore.Replace(store);
                    }

                    _activeSlot = activeSlot;
                    _storeLocation = storeLocation;
                }

                return possibleStore.Succeeded ? BoolResult.Success : new BoolResult($"Failed to initialize a RocksDb store at {_storeLocation}:", possibleStore.Failure.DescribeIncludingInnerFailures());
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                return new BoolResult(ex);
            }
        }

        private bool MergeContentLocationEntries(
            OperationContext operationContext,
            ReadOnlySpan<byte> value1,
            ReadOnlySpan<byte> value2,
            MergeResult result)
        {
            // Using manual try/catch block instead of using 'PerformOperation' to avoid unnecessary overhead.
            // This method is called a lot, and it should be as efficient as possible.
            using var c = Counters[ContentLocationDatabaseCounters.MergeEntry].Start();

            try
            {
                // Trying a more optimized version first
                if (!TryMergeSortedContentLocations(operationContext, value1, value2, result))
                {
                    // And falling back to the deserialization-based one only when the optimized version is not supported or failed.
                    MergeContentLocations(operationContext, value1, value2, result);
                }
            }
            catch (Exception e)
            {
                var errorResult = new ErrorResult(e);
                errorResult.MakeCritical();
                Tracer.OperationFinished(operationContext, errorResult, c.Elapsed, message: "Failed merging values");
            }

            return true;
        }

        private void MergeContentLocations(
            OperationContext context,
            ReadOnlySpan<byte> value1,
            ReadOnlySpan<byte> value2,
            MergeResult result)
        {
            var leftEntry = ContentLocationEntry.Deserialize(value1);
            var rightEntry = ContentLocationEntry.Deserialize(value2);

            var mergedEntry = ContentLocationEntry.MergeEntries(context, leftEntry, rightEntry, sortLocations: _configuration.SortMergeableContentLocations);

            using var serializedEntry = SerializeContentLocationEntry(mergedEntry);
            result.ValueBuffer.Set(serializedEntry.WrittenSpan);
        }

        private bool TryMergeSortedContentLocations(
            OperationContext operationContext,
            ReadOnlySpan<byte> value1,
            ReadOnlySpan<byte> value2,
            MergeResult result)
        {
            if (_configuration.SortMergeableContentLocations)
            {
                using CancellableCounterStopwatch c = Counters[ContentLocationDatabaseCounters.MergeEntrySorted].Start();
                try
                {
                    // The merged value should not be more bigger than the two original entries.
                    result.ValueBuffer.Resize(value1.Length + value2.Length);

                    var reader1 = value1.AsReader();
                    var reader2 = value2.AsReader();
                    var mergeWriter = result.ValueBuffer.Value.AsWriter();
                    if (!ContentLocationEntry.TryMergeSortedLocations(operationContext, ref reader1, ref reader2, ref mergeWriter))
                    {
                        // We haven't merged, so discarding the counter.
                        c.Discard();
                        return false;
                    }

                    result.ValueBuffer.Resize(mergeWriter.Position);
                    return true;
                }
                catch (Exception e)
                {
                    var errorResult = new ErrorResult(e);
                    errorResult.MakeCritical();
                    Tracer.OperationFinished(operationContext, errorResult, c.Elapsed, message: "Failed merging values without deserialization");

                    // Need to clear the state that could have been left by this operation.
                    result.ValueBuffer.Value.Clear();
                    return false;
                }
            }

            return false;
        }

        private struct CancellableCounterStopwatch : IDisposable
        {
            private readonly CounterCollection.Stopwatch _stopwatch;
            private bool _discarded;

            /// <nodoc />
            public CancellableCounterStopwatch(CounterCollection.Stopwatch stopwatch)
            {
                _stopwatch = stopwatch;
                _discarded = false;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                if (!_discarded)
                {
                    _stopwatch.Dispose();
                }
            }

            /// <inheritdoc cref="CounterCollection.Stopwatch.Elapsed"/>
            public TimeSpan Elapsed => _stopwatch.Elapsed;

            /// <summary>
            /// Discards the current timer
            /// </summary>
            public void Discard()
            {
                _discarded = true;
            }

            public static implicit operator CancellableCounterStopwatch(CounterCollection.Stopwatch stopwatch) =>
                new CancellableCounterStopwatch(stopwatch);
        }

        /// <inheritdoc />
        protected override bool SetMachineExistenceAndUpdateDatabase(
            OperationContext context,
            ShortHash hash,
            MachineId? machine,
            bool existsOnMachine,
            long size,
            UnixTime? lastAccessTime,
            bool reconciling)
        {
            using var counter = Counters[ContentLocationDatabaseCounters.SetMachineExistenceAndUpdateDatabase].Start();

            try
            {
                if (!_configuration.UseMergeOperatorForContentLocations)
                {
                    return base.SetMachineExistenceAndUpdateDatabase(context, hash, machine, existsOnMachine, size, lastAccessTime, reconciling);
                }

                // When the merge is used its hard to track the total content size, just because its not possible to know
                // whether the entry was actually created or not.

                // This is an implementation that uses merge operators which is quite different compared to the standard behavior.
                // 1. We don't need any locks
                // 2. We just create an entry that will be merged on retrieval or compaction

                // machine, existsOnMachine, lastAccessTime: null => add
                // machine: null, existsOnMachine: false => touch
                // machine, existsOnMachine: false => remove

                lastAccessTime ??= Clock.UtcNow;
                ContentLocationEntry? entry = null;
                var now = Clock.UtcNow;

                EntryOperation entryOperation = EntryOperation.Invalid;
                var reason = reconciling ? OperationReason.Reconcile : OperationReason.Unknown;

                if (machine != null && existsOnMachine)
                {
                    // Add
                    entry = ContentLocationEntry.Create(
                        CreateChangeSet(exists: true, machine.Value),
                        size,
                        lastAccessTimeUtc: now,
                        creationTimeUtc: now);
                    Counters[ContentLocationDatabaseCounters.MergeAdd].Increment();
                    entryOperation = EntryOperation.MergeAdd;
                }
                else if (machine == null && existsOnMachine == false)
                {
                    // Touch. Last access time must not be null
                    // Ignoring touch frequency here because in order to avoid a write we would have to read the entry first.
                    Contract.Assert(lastAccessTime != null);
                    entry = ContentLocationEntry.Create(
                        // All the mergeable entries should use machine id set that can track the state changes.
                        GetEmptyChangeSet(),
                        size,
                        lastAccessTimeUtc: lastAccessTime.Value,
                        creationTimeUtc: lastAccessTime.Value);
                    Counters[ContentLocationDatabaseCounters.MergeTouch].Increment();
                    entryOperation = EntryOperation.MergeTouch;
                }
                else if (machine != null && existsOnMachine == false)
                {
                    // Removal
                    entry = ContentLocationEntry.Create(
                        CreateChangeSet(exists: false, machine.Value),
                        size,
                        lastAccessTimeUtc: now,
                        creationTimeUtc: now);
                    Counters[ContentLocationDatabaseCounters.MergeRemove].Increment();
                    entryOperation = EntryOperation.MergeRemove;
                }

                if (entry != null)
                {
                    // Do not tracing touches if configured.
                    if (entryOperation != EntryOperation.MergeTouch || !_configuration.TraceTouches)
                    {
                        NagleOperationTracer?.Enqueue((context, hash, entryOperation, reason));
                    }
                }

                Contract.Assert(entry != null, "Seems like an unknown database operation.");

                // The merge operations are atomic, but because the GC might delete the entry
                // we need to use the read lock here and the write lock by the GC.
                using var locker = GetLock(hash).AcquireReadLock();
                Persist(context, hash, entry);
                return true;
            }
            finally
            {
                context.TracingContext.TrackOperationFinishedMetrics(nameof(SetMachineExistenceAndUpdateDatabase), Tracer.Name, counter.Elapsed);
            }
        }

        protected override LocationChangeMachineIdSet CreateChangeSet(bool exists, params MachineId[] machineIds)
        {
            return MachineIdSet.CreateChangeSet(sortLocations: _configuration.SortMergeableContentLocations, exists, machineIds);
        }

        private LocationChangeMachineIdSet GetEmptyChangeSet() =>
            _configuration.SortMergeableContentLocations ? MachineIdSet.SortedEmptyChangeSet : MachineIdSet.EmptyChangeSet;

        private StoreSlot GetNextSlot(StoreSlot slot)
        {
            return slot == StoreSlot.Slot1 ? StoreSlot.Slot2 : StoreSlot.Slot1;
        }

        private void SaveActiveSlot(Context context)
        {
            try
            {
                File.WriteAllText(_activeSlotFilePath, _activeSlot.ToString());
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                Tracer.Warning(context, $"Failure getting active slot from {_activeSlotFilePath}: {ex}");
            }
        }

        private StoreSlot GetActiveSlot(Context context)
        {
            try
            {
                if (File.Exists(_activeSlotFilePath))
                {
                    var activeSlotString = File.ReadAllText(_activeSlotFilePath);
                    if (Enum.TryParse(activeSlotString, out StoreSlot slot))
                    {
                        return slot;
                    }
                }
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                Tracer.Warning(context, $"Failure getting active slot from {_activeSlotFilePath}: {ex}");
            }

            return StoreSlot.Slot1;
        }

        private string GetStoreLocation(StoreSlot slot)
        {
            return (_configuration.StoreLocation / slot.ToString()).ToString();
        }

        /// <inheritdoc />
        protected override BoolResult SaveCheckpointCore(OperationContext context, AbsolutePath checkpointDirectory)
        {
            try
            {
                LogMemoryUsage(context, columnFamilyName: null);
                LogMemoryUsage(context, columnFamilyName: nameof(Columns.Metadata));

                if (IsStoredEpochInvalid(out var storedEpoch))
                {
                    SetGlobalEntry(nameof(ClusterStateKeys.StoredEpoch), _configuration.Epoch);
                    Tracer.Info(context.TracingContext, $"Updated stored epoch from '{storedEpoch}' to '{_configuration.Epoch}'.");
                }

                var targetDirectory = checkpointDirectory.ToString();
                Tracer.Info(context.TracingContext, $"Saving content location database checkpoint to '{targetDirectory}'.");

                if (Directory.Exists(targetDirectory))
                {
                    FileUtilities.DeleteDirectoryContents(targetDirectory, deleteRootDirectory: true);
                }

                return _keyValueStore.Use(store => store.SaveCheckpoint(targetDirectory)).ToBoolResult();
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                return new BoolResult(ex, "Save checkpoint failed.");
            }
        }

        /// <inheritdoc />
        protected override BoolResult RestoreCheckpointCore(OperationContext context, AbsolutePath checkpointDirectory)
        {
            try
            {
                LogMemoryUsage(context, columnFamilyName: null);
                LogMemoryUsage(context, columnFamilyName: nameof(Columns.Metadata));

                var activeSlot = _activeSlot;

                var newActiveSlot = GetNextSlot(activeSlot);
                var newStoreLocation = GetStoreLocation(newActiveSlot);

                Tracer.Info(context.TracingContext, $"Loading content location database checkpoint from '{checkpointDirectory}' into '{newStoreLocation}'.");

                if (Directory.Exists(newStoreLocation))
                {
                    FileUtilities.DeleteDirectoryContents(newStoreLocation, deleteRootDirectory: true);
                }

                Directory.Move(checkpointDirectory.ToString(), newStoreLocation);

                var possiblyLoaded = Load(context, newActiveSlot, clean: false);
                if (possiblyLoaded.Succeeded)
                {
                    SaveActiveSlot(context.TracingContext);
                }

                return possiblyLoaded;
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                return new BoolResult(ex, "Restore checkpoint failed.");
            }
        }

        private void LogMemoryUsage(OperationContext context, string? columnFamilyName)
        {
            if (_keyValueStore != null)
            {
                _ = context.PerformOperation(Tracer, () =>
                  {
                      return _keyValueStore.Use(store =>
                      {
                          // See: https://github.com/facebook/rocksdb/wiki/Memory-usage-in-RocksDB

                          // Indexes and bloom filters
                          long tableReadersMem = GetLongProperty(store, "rocksdb.estimate-table-readers-mem", columnFamilyName).GetValueOrDefault(-1);

                          // Memtables
                          long curSizeAllMemTables = GetLongProperty(store, "rocksdb.cur-size-all-mem-tables", columnFamilyName).GetValueOrDefault(-1);

                          // Block cache
                          long blockCacheUsage = GetLongProperty(store, "rocksdb.block-cache-usage", columnFamilyName).GetValueOrDefault(-1);

                          // Blocks pinned by iterators
                          long blockCachePinnedUsage = GetLongProperty(store, "rocksdb.block-cache-pinned-usage", columnFamilyName).GetValueOrDefault(-1);

                          return (tableReadersMem, curSizeAllMemTables, blockCacheUsage, blockCachePinnedUsage);
                      }).ToResult();
                  }, traceOperationStarted: false, messageFactory: r =>
                  {
                      if (!r.Succeeded)
                      {
                          return string.Empty;
                      }

                      return $"ColumnFamily=[{columnFamilyName ?? "default"}] TableReadersMemBytes=[{r.Value.tableReadersMem}] CurSizeAllMemTablesBytes=[{r.Value.curSizeAllMemTables}] BlockCacheUsageBytes=[{r.Value.blockCacheUsage}] BlockCachePinnedUsageBytes=[{r.Value.blockCachePinnedUsage}]";
                  });
            }
        }

        /// <inheritdoc />
        public override bool IsImmutable(AbsolutePath dbFile)
        {
            return dbFile.Path.EndsWith(".sst", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public override void SetGlobalEntry(string key, string? value)
        {
            _keyValueStore.Use(
                static (store, state) =>
                {
                    if (state.value == null)
                    {
                        store.Remove(state.key, nameof(Columns.ClusterState));
                    }
                    else
                    {
                        store.Put(state.key, state.value, nameof(Columns.ClusterState));
                    }
                    return Unit.Void;
                },
                (key, value)).ThrowOnError();
        }

        /// <inheritdoc />
        public override bool TryGetGlobalEntry(string key, [NotNullWhen(true)] out string? value)
        {
            value = _keyValueStore.Use(
                static (store, state) =>
                {
                    if (store.TryGetValue(state, out var result, nameof(Columns.ClusterState)))
                    {
                        return result;
                    }
                    else
                    {
                        return null;
                    }
                },
                key).ThrowOnError();

            return value != null;
        }

        /// <inheritdoc />
        protected override IEnumerable<(ShortHash key, ContentLocationEntry? entry)> EnumerateEntriesWithSortedKeysFromStorage(
            OperationContext context,
            EnumerationFilter? valueFilter = null,
            bool returnKeysOnly = false)
        {
            var token = context.Token;
            var keyBuffer = new List<(ShortHash key, ContentLocationEntry? entry)>();
            // Last successfully processed entry, or before-the-start pointer
            var startValue = valueFilter?.StartingPoint?.ToByteArray();

            var reachedEnd = false;
            while (!token.IsCancellationRequested && !reachedEnd)
            {
                var processedKeys = 0;
                keyBuffer.Clear();

                var killSwitchUsed = false;
                context.PerformOperation(Tracer, () =>
                {
                    // NOTE: the killswitch may cause the GC to early stop. After it has been triggered, the next Use()
                    // call will resume with a different database instance.
                    return _keyValueStore.Use(
                        (store, killSwitch) =>
                        {
                            // NOTE: Use the garbage collect procedure to collect which keys to garbage collect. This is
                            // different than the typical use which actually collects the keys specified by the garbage collector.
                            using (var cts = new CancellationTokenSource())
                            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token, killSwitch))
                            {
                                var iterationResult = store.IterateDbContent(
                                    onNextItem: iterator =>
                                    {
                                        var key = iterator.Key().ToArray();
                                        if (processedKeys == 0 && ByteArrayComparer.Instance.Equals(startValue, key))
                                        {
                                            // Start value is the same as the key. Skip it to keep from double processing the start value.
                                            return;
                                        }

                                        if (returnKeysOnly)
                                        {
                                            keyBuffer.Add((DeserializeKey(key), null));
                                        }
                                        else
                                        {
                                            var value = iterator.Value();
                                            // 'null-filter' means that all the values should be provided.
                                            if (valueFilter is null || valueFilter.ShouldEnumerate?.Invoke(value) == true)
                                            {
                                                keyBuffer.Add((DeserializeKey(key), DeserializeContentLocationEntry(value)));
                                            }
                                        }

                                        // We can only update this after the key has been successfully processed.
                                        startValue = key;
                                        processedKeys++;

                                        if (processedKeys == _configuration.EnumerateEntriesWithSortedKeysFromStorageBufferSize)
                                        {
                                            // We reached the limit for the current chunk. Iteration will get cancelled here,
                                            // which will set reachedEnd to false.
                                            cts.Cancel();
                                        }
                                    },
                                    columnFamilyName: null,
                                    startValue: startValue,
                                    token: cancellation.Token);

                                reachedEnd = iterationResult.ReachedEnd;
                            }

                            killSwitchUsed = killSwitch.IsCancellationRequested;
                        }).ToBoolResult();
                },
                messageFactory: _ => $"KillSwitch=[{killSwitchUsed}] ReturnKeysOnly=[{returnKeysOnly}] Canceled=[{token.IsCancellationRequested}]",
                traceErrorsOnly: true).ThrowIfFailure();

                foreach (var key in keyBuffer)
                {
                    yield return key;
                }
            }
        }

        /// <inheritdoc />
        protected override bool TryGetEntryCoreFromStorage(OperationContext context, ShortHash hash, [NotNullWhen(true)] out ContentLocationEntry? entry)
        {
            entry = _keyValueStore.Use(
                    static (store, state) => TryGetEntryCoreHelper(state.hash, store, state.db),
                    (hash, db: this)
                ).ThrowOnError();
            return entry != null;
        }

        // NOTE: This should remain static to avoid allocations in TryGetEntryCore
        internal static ContentLocationEntry? TryGetEntryCoreHelper(ShortHash hash, RocksDbStore store, RocksDbContentLocationDatabase db)
        {
            store.TryDeserializeValue(
                hash.AsSpanUnsafe(),
                columnFamilyName: null,
                static reader => ContentLocationEntry.Deserialize(ref reader),
                out ContentLocationEntry? result);
            return result;
        }

        /// <inheritdoc />
        internal override void Persist(OperationContext context, ShortHash hash, ContentLocationEntry? entry)
        {
            using var counter = Counters[ContentLocationDatabaseCounters.DatabaseChanges].Start();

            try
            {
                if (entry == null)
                {
                    DeleteFromDb(hash);
                }
                else
                {
                    SaveToDb(hash, entry);
                }
            }
            finally
            {
                context.TracingContext.TrackOperationFinishedMetrics(nameof(Persist), Tracer.Name, counter.Elapsed);
            }
        }

        private void SaveToDb(ShortHash hash, ContentLocationEntry entry)
        {
            _keyValueStore.Use(
                static (store, state) => SaveToDbHelper(state.hash, state.entry, store, state.db), (hash, entry, db: this)).ThrowOnError();
        }

        // NOTE: This should remain static to avoid allocations in Store
        private static Unit SaveToDbHelper(ShortHash hash, ContentLocationEntry entry, RocksDbStore store, RocksDbContentLocationDatabase db)
        {
            using var value = db.SerializeContentLocationEntry(entry);

            if (db._configuration.UseMergeOperatorForContentLocations)
            {
                // hash.AsSpan is safe here.
                store.Merge(hash.AsSpanUnsafe(), value.WrittenSpan);
            }
            else
            {
                // hash.AsSpan is safe here.
                store.Put(hash.AsSpanUnsafe(), value.WrittenSpan);
            }

            return Unit.Void;
        }

        private void DeleteFromDb(ShortHash hash)
        {
            _keyValueStore.Use(
                static (store, state) => DeleteFromDbHelper(state.hash, store, state.db), (hash, db: this)).ThrowOnError();
        }

        // NOTE: This should remain static to avoid allocations in Delete
        private static Unit DeleteFromDbHelper(ShortHash hash, RocksDbStore store, RocksDbContentLocationDatabase db)
        {
            // hash.AsSpan is safe here.
            store.Remove(hash.AsSpanUnsafe());
            return Unit.Void;
        }

        private ShortHash DeserializeKey(byte[] key)
        {
            return ShortHash.FromBytes(key);
        }

        private ShortHash DeserializeKey(ReadOnlySpan<byte> key)
        {
            return ShortHash.FromSpan(key);
        }

        /// <inheritdoc />
        public override Result<MetadataEntry?> GetMetadataEntry(OperationContext context, StrongFingerprint strongFingerprint, bool touch)
        {
            // This method calls _keyValueStore.Use with non-static lambda, because this code is complicated
            // and not as perf critical as other places.
            using var key = GetMetadataKey(strongFingerprint);

            MetadataEntry? result = null;
            var status = _keyValueStore.Use(
                store =>
                {
                    if (store.TryDeserializeValue(key, nameof(Columns.Metadata), static reader => MetadataEntry.Deserialize(ref reader), out result)
                        && !_configuration.OpenReadOnly && IsDatabaseWriteable && touch)
                    {
                        // Update the time, only if no one else has changed it in the mean time. We don't
                        // really care if this succeeds or not, because if it doesn't it only means someone
                        // else changed the stored value before this operation but after it was read.
                        Analysis.IgnoreResult(this.CompareExchange(context, strongFingerprint, result.Value.ContentHashListWithDeterminism, result.Value.ContentHashListWithDeterminism));

                        // TODO(jubayard): since we are inside the ContentLocationDatabase, we can validate that all
                        // hashes exist. Moreover, we can prune content.
                    }
                });

            if (!status.Succeeded)
            {
                return new Result<MetadataEntry?>(status.Failure.CreateException());
            }

            return new Result<MetadataEntry?>(result, isNullAllowed: true);
        }

        /// <summary>
        /// Fine-grained locks that used for all operations that mutate Metadata records.
        /// </summary>
        private readonly object[] _metadataLocks = Enumerable.Range(0, byte.MaxValue + 1).Select(s => new object()).ToArray();

        /// <inheritdoc />
        public override Possible<bool> TryUpsert(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism replacement,
            Func<MetadataEntry, bool> shouldReplace,
            DateTime? lastAccessTimeUtc)
        {
            using var key = GetMetadataKey(strongFingerprint);
            return _keyValueStore.Use(
                store =>
                {
                    lock (_metadataLocks[GetMetadataLockIndex(strongFingerprint)])
                    {
                        if (store.TryDeserializeValue(key, nameof(Columns.Metadata), static reader => MetadataEntry.Deserialize(ref reader), out var current))
                        {
                            if (!shouldReplace(current))
                            {
                                if (lastAccessTimeUtc != null)
                                {
                                    // Not updated contents but content should be touched
                                    replacement = current.ContentHashListWithDeterminism;
                                }
                                else
                                {
                                    return false;
                                }
                            }

                            if (lastAccessTimeUtc < current.LastAccessTimeUtc)
                            {
                                lastAccessTimeUtc = current.LastAccessTimeUtc;
                            }
                        }

                        // Don't put if content hash list is null since this represents a touch which arrived before
                        // the initial put for the content hash list.
                        if (replacement.ContentHashList != null)
                        {
                            using var serializedReplacementMetadata = SerializeMetadataEntry(new MetadataEntry(replacement, (lastAccessTimeUtc ?? Clock.UtcNow)));
                            store.Put(key, serializedReplacementMetadata, nameof(Columns.Metadata));
                        }
                    }

                    return true;
                });
        }

        /// <inheritdoc />
        public override IEnumerable<Result<StrongFingerprint>> EnumerateStrongFingerprints(OperationContext context)
        {
            var result = new List<Result<StrongFingerprint>>();
            var status = _keyValueStore.Use(
                static (store, state) =>
                {
                    store.PrefixKeyLookup(
                        state: state,
                        Array.Empty<byte>(),
                        nameof(Columns.Metadata),
                        static (state, key) =>
                    {
                            var strongFingerprint = state.@this.DeserializeStrongFingerprint(key);
                        state.result.Add(Result.Success(strongFingerprint));
                            return true;
                        });

                    return state.result;
                }, (result: result, @this: this));

            if (!status.Succeeded)
            {
                result.Add(new Result<StrongFingerprint>(status.Failure.CreateException()));
            }

            return result;
        }

        /// <inheritdoc />
        public override Result<IReadOnlyList<Selector>> GetSelectors(OperationContext context, Fingerprint weakFingerprint)
        {
            var selectors = new List<(long TimeUtc, Selector Selector)>();
            var status = _keyValueStore.Use(
                static (store, state) =>
                {
                    using var key = state.db.SerializeWeakFingerprint(state.weakFingerprint);

                    // This only works because the strong fingerprint serializes the weak fingerprint first. Hence,
                    // we know that all keys here are strong fingerprints that match the weak fingerprint.
                    store.PrefixLookup(
                        state: state,
                        prefix: key,
                        columnFamilyName: nameof(Columns.Metadata),
                        observeCallback: static (state, key, value) =>
                    {
                                             var strongFingerprint = state.db.DeserializeStrongFingerprint(key);
                                             var timeUtc = state.db.DeserializeMetadataLastAccessTimeUtc(value);
                        state.selectors.Add((timeUtc, strongFingerprint.Selector));
                                             return true;
                    }
                        );

                    return Unit.Void;
                }, (selectors: selectors, db: this, weakFingerprint: weakFingerprint));

            if (!status.Succeeded)
            {
                return new Result<IReadOnlyList<Selector>>(status.Failure.CreateException());
            }

            return new Result<IReadOnlyList<Selector>>(selectors
                .OrderByDescending(entry => entry.TimeUtc)
                .Select(entry => entry.Selector).ToList());
        }

        private long DeserializeMetadataLastAccessTimeUtc(ReadOnlySpan<byte> data)
        {
            return SerializationPool.Deserialize(data, static reader => MetadataEntry.DeserializeLastAccessTimeUtc(reader));
        }

        /// <inheritdoc />
        protected override Result<MetadataGarbageCollectionOutput> GarbageCollectMetadataCore(OperationContext context)
        {
            return _keyValueStore.Use((store, killSwitch) =>
            {
                using var ctx = context.WithCancellationToken(killSwitch);

                long metadataCFSizeBeforeGcBytes = GetLongProperty(
                    store,
                    "rocksdb.estimate-live-data-size",
                    columnFamilyName: nameof(Columns.Metadata)).GetValueOrDefault(-1);

                var output = GarbageCollectMetadataWithMaximumSizeStrategy(ctx, store);

                Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesScanned].Add(output.Scanned);
                Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesRemoved].Add(output.Removed);

                output.MetadataCFSizeBeforeGcBytes = metadataCFSizeBeforeGcBytes;
                output.MetadataCFSizeAfterGcBytes = GetLongProperty(
                    store,
                    "rocksdb.estimate-live-data-size",
                    columnFamilyName: nameof(Columns.Metadata)).GetValueOrDefault(-1);
                output.KillSwitch = killSwitch.IsCancellationRequested;
                return output;
            }).ToResult();
        }

        private MetadataGarbageCollectionOutput GarbageCollectMetadataWithMaximumSizeStrategy(OperationContext context, RocksDbStore store)
        {
            ulong sizeTargetBytes = (ulong)Math.Ceiling(_configuration.MetadataGarbageCollectionMaximumSizeMb * 1e6);

            // This garbage collection algorithm works in two passes:
            //   1. We estimate the quantiles of last access times w.r.t. now, in minutes.
            //   2. We know the current size S, the target size T, and the amount we need to remove R = T - S > 0.
            //      Since we need to remove R bytes of data, that corresponds to a fraction R/S of the current size,
            //      hence, we can search for the quantile R/S of the last access time distribution (i.e. the cut-off
            //      time T at which R/S entries are older).
            //      The second pass goes through the entire DB again, and removes all last access times older than T.
            // The underlying assumption is that the key size distribution is uniform and independent of time.

            // The database snapshot that we traverse will be set at the moment we call PrefixSearch. Any changes to
            // the database after that won't be seen by this method. Hence, last access times are computed w.r.t. now.
            var now = Clock.UtcNow;

            Tracer.Info(context, $"Starting first pass. Now=[{now}]");

            ulong firstPassSumKeySize = 0;
            ulong firstPassSumValueSize = 0;
            ulong firstPassScannedEntries = 0;
            var lastAccessTimeSketch = new DDSketch();
            var strongFingerprintSizeSketch = new DDSketch();
            var metadataEntrySizeSketch = new DDSketch();

            store.PrefixLookup(
                state: string.Empty,
                prefix: ReadOnlySpan<byte>.Empty,
                nameof(Columns.Metadata),
                (state, key, value) =>
                {
                    if (context.Token.IsCancellationRequested)
                    {
                        return false;
                    }

                    var lastAccessTime = DateTime.FromFileTimeUtc(DeserializeMetadataLastAccessTimeUtc(value));
                    var lastAccessDelta = now - lastAccessTime;

                    lastAccessTimeSketch.Insert(lastAccessDelta.TotalMinutes);

                    strongFingerprintSizeSketch.Insert(key.Length);
                    metadataEntrySizeSketch.Insert(value.Length);

                    firstPassSumKeySize += (uint)key.Length;
                    firstPassSumValueSize += (uint)value.Length;
                    firstPassScannedEntries++;

                    return true;
                });

            context.Token.ThrowIfCancellationRequested();

            if (firstPassScannedEntries == 0)
            {
                Tracer.Info(context, $"No entries in database. Early stopping.");
                return new MetadataGarbageCollectionOutput()
                {
                    Scanned = 0,
                    Removed = 0,
                };
            }

            double avgKeySize = (double)firstPassSumKeySize / (double)firstPassScannedEntries;
            double avgValueSize = (double)firstPassSumValueSize / (double)firstPassScannedEntries;
            Tracer.Info(context, $"First pass complete. SumKeySize=[{firstPassSumKeySize}] SumValueSize=[{firstPassSumValueSize}] CountEntries=[{firstPassScannedEntries}] AvgKeySize=[{avgKeySize}] AvgValueSize=[{avgValueSize}]");

            Tracer.Info(context, $"Last Access Time statistics: Max=[{lastAccessTimeSketch.Max}] Min=[{lastAccessTimeSketch.Min}] Avg=[{lastAccessTimeSketch.Average}] P50=[{lastAccessTimeSketch.Quantile(0.5)}] P75=[{lastAccessTimeSketch.Quantile(0.75)}] P90=[{lastAccessTimeSketch.Quantile(0.90)}] P95=[{lastAccessTimeSketch.Quantile(0.95)}]");

            Tracer.Info(context, $"Strong Fingerprint size statistics: Max=[{strongFingerprintSizeSketch.Max}] Min=[{strongFingerprintSizeSketch.Min}] Avg=[{strongFingerprintSizeSketch.Average}] P50=[{strongFingerprintSizeSketch.Quantile(0.5)}] P75=[{strongFingerprintSizeSketch.Quantile(0.75)}] P90=[{strongFingerprintSizeSketch.Quantile(0.90)}] P95=[{strongFingerprintSizeSketch.Quantile(0.95)}]");

            Tracer.Info(context, $"Metadata Entry size statistics: Max=[{metadataEntrySizeSketch.Max}] Min=[{metadataEntrySizeSketch.Min}] Avg=[{metadataEntrySizeSketch.Average}] P50=[{metadataEntrySizeSketch.Quantile(0.5)}] P75=[{metadataEntrySizeSketch.Quantile(0.75)}] P90=[{metadataEntrySizeSketch.Quantile(0.90)}] P95=[{metadataEntrySizeSketch.Quantile(0.95)}]");

            ulong sizeDatabaseBytes = firstPassSumKeySize + firstPassSumValueSize;
            if (sizeDatabaseBytes <= sizeTargetBytes)
            {
                Tracer.Info(context, $"Early stop. SizeBytes=[{sizeDatabaseBytes}] SizeTargetBytes=[{sizeTargetBytes}]");
                return new MetadataGarbageCollectionOutput()
                {
                    Scanned = (long)firstPassScannedEntries,
                    Removed = 0,
                };
            }

            ulong sizeRemovalBytes = sizeDatabaseBytes - sizeTargetBytes;
            double fractionToRemove = (double)sizeRemovalBytes / (double)sizeDatabaseBytes;
            double fractionToKeep = 1.0 - fractionToRemove;

            var keepCutOffMinutes = lastAccessTimeSketch.Quantile(fractionToKeep);

            // Everything older than this point will be removed
            var keepCutOffDateTime = now - TimeSpan.FromMinutes(keepCutOffMinutes);
            var keepCutOffFileTimeUtc = keepCutOffDateTime.ToFileTimeUtc();

            Tracer.Info(context, $"Starting second pass. SizeBytes=[{sizeDatabaseBytes}] SizeRemovalBytes=[{sizeRemovalBytes}] FractionToKeep=[{fractionToKeep}] KeepCutOffMinutes=[{keepCutOffMinutes}] KeepCutOffDateTime=[{keepCutOffDateTime}]");

            var secondPassScannedEntries = 0;
            var secondPassRemovedEntries = 0;
            ulong secondPassSumKeySize = 0;
            ulong secondPassSumValueSize = 0;
            ulong secondPassRemovedKeySize = 0;
            ulong secondPassRemovedValueSize = 0;

            store.PrefixLookup(
                state: string.Empty,
                prefix: ReadOnlySpan<byte>.Empty,
                nameof(Columns.Metadata),
                (state, key, value) =>
                {
                    if (context.Token.IsCancellationRequested)
                    {
                        return false;
                    }

                    var fileTimeUtc = DeserializeMetadataLastAccessTimeUtc(value);
                    var strongFingerprint = key;

                    if (fileTimeUtc < keepCutOffFileTimeUtc)
                    {
                        store.Remove(strongFingerprint, columnFamilyName: nameof(Columns.Metadata));

                        secondPassRemovedKeySize += (ulong)key.Length;
                        secondPassRemovedValueSize += (ulong)value.Length;
                        secondPassRemovedEntries++;

                        var fingerprint = DeserializeStrongFingerprint(strongFingerprint);
                        NagleOperationTracer?.Enqueue((context, fingerprint, EntryOperation.RemoveMetadataEntry, OperationReason.GarbageCollect));
                    }

                    secondPassSumKeySize += (ulong)key.Length;
                    secondPassSumValueSize += (ulong)value.Length;
                    secondPassScannedEntries++;

                    return true;
                });

            // Not throwing if the cancellation was requested here, we trace first and fail at the end of the method.
            Tracer.Info(context, $"Second pass complete. ScannedEntries=[{secondPassScannedEntries}] RemovedEntries=[{secondPassRemovedEntries}] SumKeySize=[{secondPassSumKeySize}] SumValueSize=[{secondPassSumValueSize}] RemovedKeySize=[{secondPassRemovedValueSize}] RemovedValueSize=[{secondPassRemovedValueSize}] SizeBytes=[{sizeDatabaseBytes}] SizeRemovalBytes=[{sizeRemovalBytes}] FractionToKeep=[{fractionToKeep}] KeepCutOffMinutes=[{keepCutOffMinutes}] KeepCutOffDateTime=[{keepCutOffDateTime}]");

            var removedSizeBytes = secondPassRemovedKeySize + secondPassRemovedValueSize;
            var preGcSizeBytes = secondPassSumKeySize + secondPassSumValueSize;
            // NOTE: This math below can overflow.
            var postGcSizeBytes = (long)preGcSizeBytes - (long)removedSizeBytes;
            var sizeTargetDelta = (long)sizeTargetBytes - (long)postGcSizeBytes;
            Tracer.Info(context, $"Final results. PreGcSizeBytes=[{preGcSizeBytes}] PostGcSizeBytes=[{postGcSizeBytes}] RemovedSizeBytes=[{removedSizeBytes}] SizeTargetDelta=[{sizeTargetDelta}]");

            context.Token.ThrowIfCancellationRequested();

            return new MetadataGarbageCollectionOutput()
            {
                Scanned = secondPassScannedEntries,
                Removed = secondPassRemovedEntries,
            };
        }

        /// <nodoc />
        public enum LongProperty
        {
            /// <summary>
            /// Size of live data.
            /// </summary>
            /// <remarks>
            ///  This differs from <see cref="LiveFilesSizeBytes"/> because the files include the size of tombstones
            ///  and other stuff that's in there, not just actual data.
            /// </remarks>
            LiveDataSizeBytes,

            /// <summary>
            /// Size of live files.
            /// </summary>
            LiveFilesSizeBytes,
        }

        /// <nodoc />
        public enum Entity
        {
            /// <nodoc />
            ContentTracking = 0,

            /// <nodoc />
            Metadata = 1,
        }

        /// <nodoc />
        public Result<long> GetLongProperty(LongProperty property, Entity entity)
        {
            var propertyName = property switch
            {
                LongProperty.LiveFilesSizeBytes => "rocksdb.live-sst-files-size",
                LongProperty.LiveDataSizeBytes => "rocksdb.estimate-live-data-size",
                _ => throw new NotImplementedException($"Unhandled property `{property}` for entity `{entity}`"),
            };

            var columnFamilyName = entity switch
            {
                Entity.ContentTracking => null,
                Entity.Metadata => nameof(Columns.Metadata),
                _ => throw new NotImplementedException($"Unhandled entity `{entity}`"),
            };

            return _keyValueStore.Use(store => GetLongProperty(store, propertyName, columnFamilyName)).Result;
        }

        private Result<long> GetLongProperty(RocksDbStore store, string propertyName, string? columnFamilyName = null)
        {
            try
            {
                return long.Parse(store.GetProperty(propertyName, columnFamilyName));
            }
            catch (Exception exception)
            {
                return new Result<long>(exception);
            }
        }

        private class KeyValueStoreGuard : IDisposable
        {
            private KeyValueStoreAccessor _accessor;

            /// <summary>
            /// The kill switch is used to stop all long running operations. Such operations should call the Use
            /// overload that gets a <see cref="CancellationToken"/>, and re-start the operation from the last valid
            /// state when the kill switch gets triggered.
            ///
            /// Operations that do this will have their database switched under them as they are running. They can
            /// also choose to terminate gracefully if possible. For examples, see:
            ///  - <see cref="GarbageCollectMetadataCore(OperationContext)"/>
            ///  - Content GC
            /// </summary>
            private CancellationTokenSource _killSwitch = new CancellationTokenSource();

            private readonly ReaderWriterLockSlim _accessorLock = new ReaderWriterLockSlim(recursionPolicy: LockRecursionPolicy.SupportsRecursion);

            public KeyValueStoreGuard(KeyValueStoreAccessor accessor)
            {
                _accessor = accessor;
            }

            public void Dispose()
            {
                _killSwitch.Cancel();

                using var token = _accessorLock.AcquireWriteLock();

                _accessor.Dispose();
                _killSwitch.Dispose();
            }

            public void Replace(KeyValueStoreAccessor accessor)
            {
                _killSwitch.Cancel();

                using var token = _accessorLock.AcquireWriteLock();

                _accessor.Dispose();
                _accessor = accessor;

                _killSwitch.Dispose();
                _killSwitch = new CancellationTokenSource();
            }

            public Possible<TResult> Use<TState, TResult>(Func<RocksDbStore, TState, TResult> action, TState state)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use(action, state);
            }

            public Possible<Unit> Use(Action<RocksDbStore> action)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use(action);
            }

            public Possible<TResult> Use<TResult>(Func<RocksDbStore, TResult> action)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use(action);
            }

            public Possible<TResult> Use<TState, TResult>(Func<RocksDbStore, TState, CancellationToken, TResult> action, TState state)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use(
                    static (store, innerState) => innerState.action(store, innerState.state, innerState.token),
                    (state, token: _killSwitch.Token, action));
            }

            public Possible<Unit> Use(Action<RocksDbStore, CancellationToken> action)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use(
                    static (store, state) => { state.action(store, state.killSwitch); return Unit.Void; },
                    (killSwitch: _killSwitch.Token, action));
            }

            public Possible<TResult> Use<TResult>(Func<RocksDbStore, CancellationToken, TResult> action)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use(
                    static (store, state) => state.action(store, state.killSwitch),
                    (killSwitch: _killSwitch.Token, action));
            }
        }
    }
}
