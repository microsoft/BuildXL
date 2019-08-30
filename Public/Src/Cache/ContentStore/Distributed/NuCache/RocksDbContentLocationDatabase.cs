// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Threading;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using Unit = BuildXL.Utilities.Tasks.Unit;

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
        private string _storeLocation;
        private readonly string _activeSlotFilePath;
        private Timer _compactionTimer;

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
            Metadata
        }

        private enum ClusterStateKeys
        {
            MaxMachineId
        }

        /// <inheritdoc />
        public RocksDbContentLocationDatabase(IClock clock, RocksDbContentLocationDatabaseConfiguration configuration, Func<IReadOnlyList<MachineId>> getInactiveMachines)
            : base(clock, configuration, getInactiveMachines)
        {
            Contract.Requires(configuration.FlushPreservePercentInMemory >= 0 && configuration.FlushPreservePercentInMemory <= 1);
            Contract.Requires(configuration.FlushDegreeOfParallelism > 0);
            Contract.Requires(configuration.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep > 0);

            _configuration = configuration;
            _activeSlotFilePath = (_configuration.StoreLocation / ActiveStoreSlotFileName).ToString();
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            lock (TimerChangeLock)
            {
                _compactionTimer?.Dispose();
                _compactionTimer = null;
            }

            _keyValueStore?.Dispose();

            return base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        protected override BoolResult InitializeCore(OperationContext context)
        {
            if (_configuration.FullRangeCompactionInterval != Timeout.InfiniteTimeSpan)
            {
                _compactionTimer = new Timer(
                    _ => FullRangeCompaction(context),
                    null,
                    IsDatabaseWriteable ? _configuration.FullRangeCompactionInterval : Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);
            }

            var result = Load(context, GetActiveSlot(context.TracingContext), clean: _configuration.CleanOnInitialize);
            if (result && _configuration.TestInitialCheckpointPath != null)
            {
                return RestoreCheckpoint(context, _configuration.TestInitialCheckpointPath);
            }

            return result;
        }

        /// <inheritdoc />
        public override void SetDatabaseMode(bool isDatabaseWriteable)
        {
            if (IsDatabaseWriteable != isDatabaseWriteable)
            {
                // Shutdown can't happen simultaneously, so no need to take the lock
                var nextCompactionTimeSpan = isDatabaseWriteable ? _configuration.FullRangeCompactionInterval : Timeout.InfiniteTimeSpan;
                _compactionTimer?.Change(nextCompactionTimeSpan, Timeout.InfiniteTimeSpan);
            }

            base.SetDatabaseMode(isDatabaseWriteable);
        }

        private void FullRangeCompaction(OperationContext context)
        {
            if (ShutdownStarted)
            {
                return;
            }

            context.PerformOperation(Tracer, () =>
                _keyValueStore.Use(store =>
                {
                    foreach (var columnFamilyName in new[] { "default", nameof(Columns.ClusterState), nameof(Columns.Metadata) })
                    {
                        if (context.Token.IsCancellationRequested)
                        {
                            break;
                        }

                        var result = context.PerformOperation(Tracer, () =>
                        {
                            store.CompactRange((byte[])null, null, columnFamilyName: columnFamilyName);
                            return BoolResult.Success;
                        }, messageFactory: _ => $"ColumnFamily={columnFamilyName}");

                        if (!result.Succeeded)
                        {
                            break;
                        }
                    }
                }).ToBoolResult()).IgnoreFailure();

            if (!ShutdownStarted)
            {
                lock (TimerChangeLock)
                {
                    // No try-catch required here.
                    _compactionTimer?.Change(_configuration.FullRangeCompactionInterval, Timeout.InfiniteTimeSpan);
                }
            }
        }

        private BoolResult Load(OperationContext context, StoreSlot activeSlot, bool clean = false)
        {
            try
            {
                var storeLocation = GetStoreLocation(activeSlot);

                if (clean && Directory.Exists(storeLocation))
                {
                    BuildXL.Native.IO.FileUtilities.DeleteDirectoryContents(storeLocation, deleteRootDirectory: true);
                }

                Directory.CreateDirectory(storeLocation);

                Tracer.Info(context, $"Creating rocksdb store at '{storeLocation}'.");

                var possibleStore = KeyValueStoreAccessor.Open(storeLocation,
                    additionalColumns: new[] { nameof(Columns.ClusterState), nameof(Columns.Metadata) }, rotateLogs: true);
                if (possibleStore.Succeeded)
                {
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
                var nextActiveSlot = GetNextSlot(_activeSlot);
                var newStoreLocation = GetStoreLocation(nextActiveSlot);

                Tracer.Info(context.TracingContext, $"Loading content location database checkpoint from '{checkpointDirectory}' into '{newStoreLocation}'.");

                if (Directory.Exists(newStoreLocation))
                {
                    FileUtilities.DeleteDirectoryContents(newStoreLocation, deleteRootDirectory: true);
                }

                Directory.Move(checkpointDirectory.ToString(), newStoreLocation);

                var possiblyLoaded = Load(context, nextActiveSlot);
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

        /// <inheritdoc />
        public override bool IsImmutable(AbsolutePath dbFile)
        {
            return dbFile.Path.EndsWith(".sst", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        protected override void UpdateClusterStateCore(OperationContext context, ClusterState clusterState, bool write)
        {
            _keyValueStore.Use(
                    store =>
                    {
                        int maxMachineId = ClusterState.InvalidMachineId;
                        if (!store.TryGetValue(nameof(ClusterStateKeys.MaxMachineId), out var maxMachinesString, nameof(Columns.ClusterState)) ||
                            !int.TryParse(maxMachinesString, out maxMachineId))
                        {
                            Tracer.OperationDebug(context, $"Unable to load cluster state from db. MaxMachineId='{maxMachinesString}'");
                            if (!write)
                            {
                                // No machine state in db. Return if we are not updating the db.
                                return;
                            }
                        }

                        void logSynchronize()
                        {
                            Tracer.OperationDebug(context, $"Synchronizing cluster state: MaxMachineId={clusterState.MaxMachineId}, Database.MaxMachineId={maxMachineId}]");
                        }

                        if (clusterState.MaxMachineId > maxMachineId && write)
                        {
                            logSynchronize();

                            // Update db with values from cluster state
                            for (int machineIndex = maxMachineId + 1; machineIndex <= clusterState.MaxMachineId; machineIndex++)
                            {
                                if (clusterState.TryResolve(new MachineId(machineIndex), out var machineLocation))
                                {
                                    Tracer.OperationDebug(context, $"Storing machine mapping ({machineIndex}={machineLocation})");
                                    store.Put(machineIndex.ToString(), machineLocation.Path, nameof(Columns.ClusterState));
                                }
                                else
                                {
                                    throw Contract.AssertFailure($"Unabled to resolve machine location for machine id={machineIndex}");
                                }
                            }

                            store.Put(nameof(ClusterStateKeys.MaxMachineId), clusterState.MaxMachineId.ToString(), nameof(Columns.ClusterState));
                        }
                        else if (maxMachineId > clusterState.MaxMachineId)
                        {
                            logSynchronize();

                            // Update cluster state with values from db
                            var unknownMachines = new Dictionary<MachineId, MachineLocation>();
                            for (int machineIndex = clusterState.MaxMachineId + 1; machineIndex <= maxMachineId; machineIndex++)
                            {
                                if (store.TryGetValue(machineIndex.ToString(), out var machineLocationData, nameof(Columns.ClusterState)))
                                {
                                    var machineId = new MachineId(machineIndex);
                                    var machineLocation = new MachineLocation(machineLocationData);
                                    context.LogMachineMapping(Tracer, machineId, machineLocation);
                                    unknownMachines[machineId] = machineLocation;
                                }
                                else
                                {
                                    throw Contract.AssertFailure($"Unabled to find machine location for machine id={machineIndex}");
                                }
                            }

                            clusterState.AddUnknownMachines(maxMachineId, unknownMachines);
                        }
                    }).ThrowOnError();
        }

        /// <inheritdoc />
        protected override IEnumerable<ShortHash> EnumerateSortedKeysFromStorage(CancellationToken token)
        {
            var keyBuffer = new List<ShortHash>();
            byte[] startValue = null;

            const int KeysChunkSize = 100000;
            while (!token.IsCancellationRequested)
            {
                keyBuffer.Clear();

                _keyValueStore.Use(
                    store =>
                    {
                        // NOTE: Use the garbage collect procedure to collect which keys to garbage collect. This is
                        // different than the typical use which actually collects the keys specified by the garbage collector.
                        var cts = new CancellationTokenSource();
                        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token))
                        {
                            store.GarbageCollect(
                                canCollect: key =>
                                {
                                    if (keyBuffer.Count == 0 && ByteArrayComparer.Instance.Equals(startValue, key))
                                    {
                                        // Start value is the same as the key. Skip it to keep from double processing the start value.
                                        return false;
                                    }

                                    keyBuffer.Add(DeserializeKey(key));
                                    startValue = key;

                                    if (keyBuffer.Count == KeysChunkSize)
                                    {
                                        cts.Cancel();
                                    }

                                    return false;
                                },
                                cancellationToken: cancellation.Token,
                                startValue: startValue);
                        }

                    }).ThrowOnError();

                if (keyBuffer.Count == 0)
                {
                    break;
                }

                foreach (var key in keyBuffer)
                {
                    yield return key;
                }
            }
        }

        /// <inheritdoc />
        protected override IEnumerable<(ShortHash key, ContentLocationEntry entry)> EnumerateEntriesWithSortedKeysFromStorage(
            CancellationToken token,
            EnumerationFilter filter = null)
        {
            var keyBuffer = new List<(ShortHash key, ContentLocationEntry entry)>();
            const int KeysChunkSize = 100000;
            byte[] startValue = null;
            while (!token.IsCancellationRequested)
            {
                keyBuffer.Clear();

                _keyValueStore.Use(
                    store =>
                    {
                        // NOTE: Use the garbage collect procedure to collect which keys to garbage collect. This is
                        // different than the typical use which actually collects the keys specified by the garbage collector.
                        var cts = new CancellationTokenSource();
                        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token))
                        {
                            store.GarbageCollectByKeyValue(
                                canCollect: (iterator) =>
                                {
                                    byte[] key = null;
                                    if (keyBuffer.Count == 0 && ByteArrayComparer.Instance.Equals(startValue, key = iterator.Key()))
                                    {
                                        // Start value is the same as the key. Skip it to keep from double processing the start value.

                                        // Set startValue to null to indicate that we potentially could have reached the end of the database.
                                        startValue = null;
                                        return false;
                                    }

                                    startValue = null;
                                    byte[] value = null;
                                    if (filter != null && filter(value = iterator.Value()))
                                    {
                                        keyBuffer.Add((DeserializeKey(key ?? iterator.Key()), DeserializeContentLocationEntry(value)));
                                    }

                                    if (keyBuffer.Count == KeysChunkSize)
                                    {
                                        // We reached the limit for a current chunk.
                                        // Reading the iterator to get the new start value.
                                        startValue = iterator.Key();
                                        cts.Cancel();
                                    }

                                    return false;
                                },
                                cancellationToken: cancellation.Token,
                                startValue: startValue);
                        }

                    }).ThrowOnError();

                foreach (var key in keyBuffer)
                {
                    yield return key;
                }

                // Null value in startValue variable means that the database reached it's end.
                if (startValue == null)
                {
                    break;
                }
            }
        }

        /// <inheritdoc />
        protected override bool TryGetEntryCoreFromStorage(OperationContext context, ShortHash hash, out ContentLocationEntry entry)
        {
            entry = _keyValueStore.Use(
                    (store, state) => TryGetEntryCoreHelper(state.hash, store, state.db),
                    (hash, db: this)
                ).ThrowOnError();
            return entry != null;
        }

        // NOTE: This should remain static to avoid allocations in TryGetEntryCore
        private static ContentLocationEntry TryGetEntryCoreHelper(ShortHash hash, IBuildXLKeyValueStore store, RocksDbContentLocationDatabase db)
        {
            ContentLocationEntry result = null;
            if (store.TryGetValue(db.GetKey(hash), out var data))
            {
                result = db.DeserializeContentLocationEntry(data);
            }

            return result;
        }

        /// <inheritdoc />
        internal override void Persist(OperationContext context, ShortHash hash, ContentLocationEntry entry)
        {
            if (entry == null)
            {
                DeleteFromDb(context, hash);
            }
            else
            {
                SaveToDb(context, hash, entry);
            }
        }

        /// <inheritdoc />
        internal override void PersistBatch(OperationContext context, IEnumerable<KeyValuePair<ShortHash, ContentLocationEntry>> pairs)
        {
            _keyValueStore.Use((store, state) => PersistBatchHelper(store, state.pairs, state.db), (pairs, db: this)).ThrowOnError();
        }

        private static Unit PersistBatchHelper(IBuildXLKeyValueStore store, IEnumerable<KeyValuePair<ShortHash, ContentLocationEntry>> pairs, RocksDbContentLocationDatabase db)
        {
            store.ApplyBatch(pairs.Select(
                kvp => new KeyValuePair<byte[], byte[]>(db.GetKey(kvp.Key), kvp.Value != null ? db.SerializeContentLocationEntry(kvp.Value) : null)));
            return Unit.Void;
        }

        private void SaveToDb(OperationContext context, ShortHash hash, ContentLocationEntry entry)
        {
            _keyValueStore.Use(
                (store, state) => SaveToDbHelper(state.hash, state.entry, store, state.db), (hash, entry, db: this)).ThrowOnError();
        }

        // NOTE: This should remain static to avoid allocations in Store
        private static Unit SaveToDbHelper(ShortHash hash, ContentLocationEntry entry, IBuildXLKeyValueStore store, RocksDbContentLocationDatabase db)
        {
            var value = db.SerializeContentLocationEntry(entry);
            store.Put(db.GetKey(hash), value);

            return Unit.Void;
        }

        private void DeleteFromDb(OperationContext context, ShortHash hash)
        {
            _keyValueStore.Use(
                (store, state) => DeleteFromDbHelper(state.hash, store, state.db), (hash, db: this)).ThrowOnError();
        }

        // NOTE: This should remain static to avoid allocations in Delete
        private static Unit DeleteFromDbHelper(ShortHash hash, IBuildXLKeyValueStore store, RocksDbContentLocationDatabase db)
        {
            store.Remove(db.GetKey(hash));
            return Unit.Void;
        }

        private ShortHash DeserializeKey(byte[] key)
        {
            return new ShortHash(new FixedBytes(key));
        }

        private byte[] GetKey(ShortHash hash)
        {
            return hash.ToByteArray();
        }

        /// <inheritdoc />
        public override GetContentHashListResult GetContentHashList(OperationContext context, StrongFingerprint strongFingerprint)
        {
            return context.PerformOperation(
                Tracer,
                () =>
                {
                    var key = GetMetadataKey(strongFingerprint);
                    ContentHashListWithDeterminism? result = null;
                    var status = _keyValueStore.Use(
                        store =>
                        {
                            if (store.TryGetValue(key, out var data, nameof(Columns.Metadata)))
                            {
                                var metadata = DeserializeMetadataEntry(data);
                                result = metadata.ContentHashListWithDeterminism;

                                // Update the time, only if no one else has changed it in the mean time. We don't
                                // really care if this succeeds or not, because if it doesn't it only means someone
                                // else changed the stored value before this operation but after it was read.
                                Analysis.IgnoreResult(CompareExchange(context, strongFingerprint, metadata.ContentHashListWithDeterminism, metadata.ContentHashListWithDeterminism));

                                // TODO(jubayard): since we are inside the ContentLocationDatabase, we can validate that all
                                // hashes exist. Moreover, we can prune content.
                            }
                        });

                    if (!status.Succeeded)
                    {
                        return new GetContentHashListResult(status.Failure.CreateException());
                    }

                    if (result is null)
                    {
                        return new GetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None));
                    }

                    return new GetContentHashListResult(result.Value);
                }, Counters[ContentLocationDatabaseCounters.GetContentHashList]);
        }

        /// <summary>
        /// Fine-grained locks that used for all operations that mutate Metadata records.
        /// </summary>
        private readonly object[] _metadataLocks = Enumerable.Range(0, byte.MaxValue + 1).Select(s => new object()).ToArray();

        /// <inheritdoc />
        public override Possible<bool> CompareExchange(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism expected,
            ContentHashListWithDeterminism replacement)
        {
            return _keyValueStore.Use(
                store =>
                {
                    var key = GetMetadataKey(strongFingerprint);

                    lock (_metadataLocks[key[0]])
                    {
                        if (store.TryGetValue(key, out var data, nameof(Columns.Metadata)))
                        {
                            var current = DeserializeMetadataEntry(data);
                            if (!current.ContentHashListWithDeterminism.Equals(expected))
                            {
                                return false;
                            }
                        }
                        
                        var replacementMetadata = new MetadataEntry(replacement, Clock.UtcNow.ToFileTimeUtc());
                        store.Put(key, SerializeMetadataEntry(replacementMetadata), nameof(Columns.Metadata));
                    }

                    return true;
                });
        }

        /// <inheritdoc />
        public override IEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(OperationContext context)
        {
            var result = new List<StructResult<StrongFingerprint>>();
            var status = _keyValueStore.Use(
                store =>
                {
                    foreach (var kvp in store.PrefixSearch((byte[])null, nameof(Columns.Metadata)))
                    {
                        // TODO(jubayard): since this method only needs the keys and not the values, it wouldn't hurt
                        // to make an alternative prefix search that doesn't even read the values from RocksDB.
                        var strongFingerprint = DeserializeStrongFingerprint(kvp.Key);
                        result.Add(StructResult.Create(strongFingerprint));
                    }

                    return result;
                });

            if (!status.Succeeded)
            {
                result.Add(new StructResult<StrongFingerprint>(status.Failure.CreateException()));
            }

            return result;
        }
        
        /// <inheritdoc />
        public override Result<IReadOnlyList<Selector>> GetSelectors(OperationContext context, Fingerprint weakFingerprint)
        {
            var selectors = new List<(long TimeUtc, Selector Selector)>();
            var status = _keyValueStore.Use(
                store =>
                {
                    var key = SerializeWeakFingerprint(weakFingerprint);

                    // This only works because the strong fingerprint serializes the weak fingerprint first. Hence,
                    // we know that all keys here are strong fingerprints that match the weak fingerprint.
                    foreach (var kvp in store.PrefixSearch(key, columnFamilyName: nameof(Columns.Metadata)))
                    {
                        var strongFingerprint = DeserializeStrongFingerprint(kvp.Key);
                        var timeUtc = DeserializeMetadataLastAccessTimeUtc(kvp.Value);
                        selectors.Add((timeUtc, strongFingerprint.Selector));
                    }
                });

            if (!status.Succeeded)
            {
                return new Result<IReadOnlyList<Selector>>(status.Failure.CreateException());
            }

            return new Result<IReadOnlyList<Selector>>(selectors
                .OrderByDescending(entry => entry.TimeUtc)
                .Select(entry => entry.Selector).ToList());
        }

        private byte[] SerializeWeakFingerprint(Fingerprint weakFingerprint)
        {
            return SerializeCore(weakFingerprint, (instance, writer) => instance.Serialize(writer));
        }

        private byte[] SerializeStrongFingerprint(StrongFingerprint strongFingerprint)
        {
            return SerializeCore(strongFingerprint, (instance, writer) => instance.Serialize(writer));
        }

        private StrongFingerprint DeserializeStrongFingerprint(byte[] bytes)
        {
            return DeserializeCore(bytes, reader => StrongFingerprint.Deserialize(reader));
        }
        
        private byte[] GetMetadataKey(StrongFingerprint strongFingerprint)
        {
            return SerializeStrongFingerprint(strongFingerprint);
        }

        private byte[] SerializeMetadataEntry(MetadataEntry value)
        {
            return SerializeCore(value, (instance, writer) => instance.Serialize(writer));
        }

        private MetadataEntry DeserializeMetadataEntry(byte[] data)
        {
            return DeserializeCore(data, reader => MetadataEntry.Deserialize(reader));
        }

        private long DeserializeMetadataLastAccessTimeUtc(byte[] data)
        {
            return DeserializeCore(data, reader => MetadataEntry.DeserializeLastAccessTimeUtc(reader));
        }

        /// <inheritdoc />
        protected override BoolResult GarbageCollectMetadataCore(OperationContext context)
        {
            return _keyValueStore.Use(store => {
                // The strategy here is to follow what the SQLite memoization store does: we want to keep the top K
                // elements by last access time (i.e. an LRU policy). This is slightly worse than that, because our
                // iterator will go stale as time passes: since we iterate over a snapshot of the DB, we can't
                // guarantee that an entry we remove is truly the one we should be removing. Moreover, since we store
                // information what the last access times were, our internal priority queue may go stale over time as
                // well.
                var liveDbSizeInBytesBeforeGc = int.Parse(store.GetProperty(
                    "rocksdb.estimate-live-data-size",
                    columnFamilyName: nameof(Columns.Metadata)));

                var scannedEntries = 0;
                var removedEntries = 0;

                // This is a min-heap using lexicographic order: an element will be at the `Top` if its `fileTimeUtc`
                // is the smallest (i.e. the oldest). Hence, we always know what the cut-off point is for the top K: if
                // a new element is smaller than the Top, it's not in the top K, if larger, it is.
                var entries = new PriorityQueue<(long fileTimeUtc, byte[] strongFingerprint)>(
                    capacity: _configuration.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep + 1,
                    comparer: Comparer<(long fileTimeUtc, byte[] strongFingerprint)>.Create((x, y) => x.fileTimeUtc.CompareTo(y.fileTimeUtc)));
                foreach (var keyValuePair in store.PrefixSearch((byte[])null, nameof(Columns.Metadata)))
                {
                    // NOTE(jubayard): the expensive part of this is iterating over the whole database; the less we
                    // take _while_ we do that, the better. An alternative is to compute a quantile sketch and remove
                    // unneeded entries as we go. We could also batch deletions here.

                    if (context.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    var entry = (fileTimeUtc: DeserializeMetadataLastAccessTimeUtc(keyValuePair.Value),
                        strongFingerprint: keyValuePair.Key);

                    byte[] strongFingerprintToRemove = null;

                    if (entries.Count >= _configuration.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep && entries.Top.fileTimeUtc > entry.fileTimeUtc)
                    {
                        // If we already reached the maximum number of elements to keep, and the current entry is older
                        // than the oldest in the top K, we can just remove the current entry.
                        strongFingerprintToRemove = entry.strongFingerprint;
                    }
                    else
                    {
                        // We either didn't reach the number of elements we want to keep, or the entry has a last
                        // access time larger than the current smallest one in the top K.
                        entries.Push(entry);

                        if (entries.Count > _configuration.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep)
                        {
                            strongFingerprintToRemove = entries.Top.strongFingerprint;
                            entries.Pop();
                        }
                    }

                    if (!(strongFingerprintToRemove is null))
                    {
                        store.Remove(strongFingerprintToRemove, columnFamilyName: nameof(Columns.Metadata));
                        removedEntries++;
                    }

                    scannedEntries++;
                }

                Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesRemoved].Add(removedEntries);
                Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesScanned].Add(scannedEntries);

                var liveDbSizeInBytesAfterGc = int.Parse(store.GetProperty(
                    "rocksdb.estimate-live-data-size",
                    columnFamilyName: nameof(Columns.Metadata)));

                // NOTE(jubayard): since we report the live DB size, it is possible it may increase after GC, because
                // new tombstones have been added. However, there is no way to compute how much we added/removed that
                // doesn't involve either keeping track of the values, or doing two passes over the column family.
                Tracer.Debug(context, $"Metadata Garbage Collection results: ScannedEntries={scannedEntries}, RemovedEntries={removedEntries}, LiveDbSizeInBytesBeforeGc={liveDbSizeInBytesBeforeGc}, LiveDbSizeInBytesAfterGc={liveDbSizeInBytesAfterGc}");

                return Unit.Void;
            }).ToBoolResult();
        }

        /// <summary>
        /// Metadata that is stored inside the <see cref="Columns.Metadata"/> column family.
        /// </summary>
        private readonly struct MetadataEntry
        {
            /// <summary>
            /// Effective <see cref="ContentHashList"/> that we want to store, along with information about its cache
            /// determinism.
            /// </summary>
            public ContentHashListWithDeterminism ContentHashListWithDeterminism { get; }

            /// <summary>
            /// Last update time, stored as output by <see cref="DateTime.ToFileTimeUtc"/>.
            /// </summary>
            public long LastAccessTimeUtc { get; }

            public MetadataEntry(ContentHashListWithDeterminism contentHashListWithDeterminism, long lastAccessTimeUtc)
            {
                ContentHashListWithDeterminism = contentHashListWithDeterminism;
                LastAccessTimeUtc = lastAccessTimeUtc;
            }

            public static MetadataEntry Deserialize(BuildXLReader reader)
            {
                var lastUpdateTimeUtc = reader.ReadInt64Compact();
                var contentHashListWithDeterminism = ContentHashListWithDeterminism.Deserialize(reader);
                return new MetadataEntry(contentHashListWithDeterminism, lastUpdateTimeUtc);
            }

            public static long DeserializeLastAccessTimeUtc(BuildXLReader reader)
            {
                return reader.ReadInt64Compact();
            }
            
            public void Serialize(BuildXLWriter writer)
            {
                writer.WriteCompact(LastAccessTimeUtc);
                ContentHashListWithDeterminism.Serialize(writer);
            }
        }

        private class KeyValueStoreGuard : IDisposable
        {
            private KeyValueStoreAccessor _accessor;
            private readonly ReadWriteLock _accessorLock = ReadWriteLock.Create();
            private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

            public KeyValueStoreGuard(KeyValueStoreAccessor accessor)
            {
                _accessor = accessor;
            }

            public void Dispose()
            {
                using (_accessorLock.AcquireWriteLock())
                {
                    _accessor.Dispose();
                }
            }

            public void Replace(KeyValueStoreAccessor accessor)
            {
                using (_accessorLock.AcquireWriteLock())
                {
                    _accessor.Dispose();
                    _accessor = accessor;
                }
            }

            public Possible<TResult> Use<TState, TResult>(Func<IBuildXLKeyValueStore, TState, TResult> action, TState state)
            {
                using (_accessorLock.AcquireReadLock())
                {
                    return _accessor.Use(action, state);
                }
            }

            public Possible<Unit> Use(Action<IBuildXLKeyValueStore> action)
            {
                using (_accessorLock.AcquireReadLock())
                {
                    return _accessor.Use(action);
                }
            }

            public Possible<TResult> Use<TResult>(Func<IBuildXLKeyValueStore, TResult> action)
            {
                using (_accessorLock.AcquireReadLock())
                {
                    return _accessor.Use(action);
                }
            }
        }
    }
}
