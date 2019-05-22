// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
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
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Threading;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

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

        private enum StoreSlot
        {
            Slot1,
            Slot2
        }

        private enum Columns
        {
            ClusterState
        }

        private enum ClusterStateKeys
        {
            MaxMachineId
        }

        /// <inheritdoc />
        public RocksDbContentLocationDatabase(IClock clock, RocksDbContentLocationDatabaseConfiguration configuration, Func<IReadOnlyList<MachineId>> getInactiveMachines)
            : base(clock, configuration, getInactiveMachines)
        {
            _configuration = configuration;
            _activeSlotFilePath = (_configuration.StoreLocation / ActiveStoreSlotFileName).ToString();
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
            var result = Load(context, GetActiveSlot(context.TracingContext), clean: _configuration.CleanOnInitialize);
            if (result && _configuration.TestInitialCheckpointPath != null)
            {
                return RestoreCheckpoint(context, _configuration.TestInitialCheckpointPath);
            }

            return result;
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
                    additionalColumns: new[] { nameof(ClusterState) });
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

                                    if (keyBuffer.Count == KeysChunkSize )
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
                                        keyBuffer.Add((DeserializeKey(key ?? iterator.Key()), Deserialize(value)));
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
                result = db.Deserialize(data);
            }

            return result;
        }

        /// <inheritdoc />
        protected override void Persist(OperationContext context, ShortHash hash, ContentLocationEntry entry)
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
        protected override void PersistBatch(OperationContext context, IEnumerable<KeyValuePair<ShortHash, ContentLocationEntry>> pairs)
        {
            _keyValueStore.Use((store, state) => PersistBatchHelper(store, state.pairs, state.db), (pairs, db: this)).ThrowOnError();
        }

        private static Unit PersistBatchHelper(IBuildXLKeyValueStore store, IEnumerable<KeyValuePair<ShortHash, ContentLocationEntry>> pairs, RocksDbContentLocationDatabase db)
        {
            store.ApplyBatch(
                pairs.Select(pair => db.GetKey(pair.Key)),
                pairs.Select(pair => pair.Value != null ? db.Serialize(pair.Value) : null));
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
            var value = db.Serialize(entry);
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

        private class KeyValueStoreGuard : IDisposable
        {
            private KeyValueStoreAccessor _accessor;
            private readonly ReadWriteLock _rwLock = ReadWriteLock.Create();

            public KeyValueStoreGuard(KeyValueStoreAccessor accessor)
            {
                _accessor = accessor;
            }

            public void Dispose()
            {
                using (_rwLock.AcquireWriteLock())
                {
                    _accessor.Dispose();
                }
            }

            public void Replace(KeyValueStoreAccessor accessor)
            {
                using (_rwLock.AcquireWriteLock())
                {
                    _accessor.Dispose();
                    _accessor = accessor;
                }
            }

            public Possible<TResult> Use<TState, TResult>(Func<IBuildXLKeyValueStore, TState, TResult> action, TState state)
            {
                using (_rwLock.AcquireReadLock())
                {
                    return _accessor.Use(action, state);
                }
            }

            public Possible<Unit> Use(Action<IBuildXLKeyValueStore> action)
            {
                using (_rwLock.AcquireReadLock())
                {
                    return _accessor.Use(action);
                }
            }
        }
    }
}
