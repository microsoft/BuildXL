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
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore.Sketching;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using Microsoft.WindowsAzure.Storage.Table;
using static BuildXL.Cache.ContentStore.Distributed.Tracing.TracingStructuredExtensions;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// RocksDb-based version of <see cref="ContentLocationDatabase"/>.
    /// </summary>
    public sealed class RocksDbContentMetadataDatabase : ContentLocationDatabase
    {
        private readonly RocksDbContentLocationDatabaseConfiguration _configuration;

        private KeyValueStoreGuard _keyValueStore;
        private const string ActiveStoreSlotFileName = "activeSlot.txt";
        private StoreSlot _activeSlot = StoreSlot.Slot1;
        private ColumnGroup _activeColumnsGroup = ColumnGroup.One;
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
            Content,

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

            Blobs,
        }

        public enum ColumnGroup
        {
            One,

            Two
        }

        private static readonly string[] ColumnNames =
            Enumerable.Range(1, 2).SelectMany(i =>
                EnumTraits<Columns>
                    .EnumerateValues()
                    .Select(c => c.ToString() + i))
                    .ToArray();

        private enum GlobalKeys
        {
            StoredEpoch,
            ActiveColummGroup
        }

        /// <inheritdoc />
        public RocksDbContentMetadataDatabase(IClock clock, RocksDbContentLocationDatabaseConfiguration configuration)
            : base(clock, configuration, () => Array.Empty<MachineId>())
        {
            Contract.Requires(configuration.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep > 0);

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

            // We backup the logs right before loading the first DB we load
            var storeLocation = GetStoreLocation(activeSlot);

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
            TryGetGlobalEntry(nameof(GlobalKeys.StoredEpoch), out epoch);
            return _configuration.Epoch != epoch;
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

                Tracer.Info(context, $"Creating RocksDb store at '{storeLocation}'. Clean={clean}, Configured Epoch='{_configuration.Epoch}'");

                var possibleStore = KeyValueStoreAccessor.Open(
                    new RocksDbStoreConfiguration(storeLocation)
                    {
                        AdditionalColumns = ColumnNames,
                        RotateLogsMaxFileSizeBytes = _configuration.LogsKeepLongTerm ? 0ul : ((ulong)"1MB".ToSize()),
                        RotateLogsNumFiles = _configuration.LogsKeepLongTerm ? 60ul : 1,
                        RotateLogsMaxAge = TimeSpan.FromHours(_configuration.LogsKeepLongTerm ? 12 : 1),
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
                        Compression = _configuration.Compression,
                        UseReadOptionsWithSetTotalOrderSeekInDbEnumeration = _configuration.UseReadOptionsWithSetTotalOrderSeekInDbEnumeration,
                        UseReadOptionsWithSetTotalOrderSeekInGarbageCollection = _configuration.UseReadOptionsWithSetTotalOrderSeekInGarbageCollection,
                    },
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
                    var oldKeyValueStore = _keyValueStore;
                    var store = possibleStore.Result;

                    if (oldKeyValueStore == null)
                    {
                        _keyValueStore = new KeyValueStoreGuard(store);
                        _keyValueStore.UseExclusive((db, state) =>
                        {
                            if (db.TryGetValue(nameof(GlobalKeys.ActiveColummGroup), out var activeColumnGroup))
                            {
                                _activeColumnsGroup = (ColumnGroup)Enum.Parse(typeof(ColumnGroup), activeColumnGroup);
                            }
                            else
                            {
                                _activeColumnsGroup = ColumnGroup.One;
                            }

                            return true;
                        },
                        this).ThrowOnError();
                    }
                    else
                    {
                        // Just replace the inner accessor
                        oldKeyValueStore.Replace(store, db =>
                        {
                            if (db.TryGetValue(nameof(GlobalKeys.ActiveColummGroup), out var activeColumnGroup))
                            {
                                _activeColumnsGroup = (ColumnGroup)Enum.Parse(typeof(ColumnGroup), activeColumnGroup);
                            }
                            else
                            {
                                _activeColumnsGroup = ColumnGroup.One;
                            }
                        }).ThrowOnError();
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
        public override Task<BoolResult> GarbageCollectAsync(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer,
               () =>
               {
                   // Get exclusive lock to prevent concurrent access while deleting column families
                   // without this, we might try to read from a column family which does not exist
                   return _keyValueStore.UseExclusive(
                       (store, db) =>
                       {
                           var otherColumnGroup = db.GetFormerColumnGroup();

                           store.Put(nameof(GlobalKeys.ActiveColummGroup), otherColumnGroup.ToString());

                           foreach (var column in EnumTraits<Columns>.EnumerateValues())
                           {
                               var columnName = db.NameOf(column, otherColumnGroup);

                               // Clear the column family by dropping and recreating
                               store.DropColumnFamily(columnName);
                               store.CreateColumnFamily(columnName);
                           }

                           _activeColumnsGroup = otherColumnGroup;
                           return BoolResult.SuccessTask;
                       },
                       this).ThrowOnError();
               },
               counter: Counters[ContentLocationDatabaseCounters.GarbageCollectContent],
               isCritical: true);
        }

        private ColumnGroup GetFormerColumnGroup()
        {
            return _activeColumnsGroup == ColumnGroup.One
                ? ColumnGroup.Two
                : ColumnGroup.One;
        }

        /// <inheritdoc />
        protected override BoolResult SaveCheckpointCore(OperationContext context, AbsolutePath checkpointDirectory)
        {
            try
            {
                if (IsStoredEpochInvalid(out var storedEpoch))
                {
                    SetGlobalEntry(nameof(GlobalKeys.StoredEpoch), _configuration.Epoch);
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

                // At this point in time, we have unloaded the old database and loaded the new one. This means we're
                // free to backup the old one's logs.
                var oldStoreLocation = GetStoreLocation(activeSlot);

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

        private string NameOf(Columns columns, ColumnGroup? group = null)
        {
            return ColumnNames[(int)(group ?? _activeColumnsGroup) * EnumTraits<Columns>.ValueCount + (int)columns];
        }

        public bool PutBlob(ShortHash key, byte[] value)
        {
            return _keyValueStore.Use(
                static (store, state) =>
                {
                    if (!store.Contains(state.key))
                    {
                        store.Put(state.key, state.value, state.db.NameOf(Columns.Blobs));
                        return true;
                    }

                    return false;
                },
                (key: key.ToByteArray(), value, db: this)).ThrowOnError();
        }

        public bool TryGetBlob(ShortHash key, [NotNullWhen(true)] out byte[]? value)
        {
            value = _keyValueStore.Use(
                static (store, state) =>
                {
                    if (store.TryGetValue(state.key, out var result, state.db.NameOf(Columns.Blobs)))
                    {
                        return result;
                    }
                    else if (store.TryGetValue(state.key, out result, state.db.NameOf(Columns.Blobs, state.db.GetFormerColumnGroup())))
                    {
                        return result;
                    }
                    else
                    {
                        return null;
                    }
                },
                (key: key.ToByteArray(), db: this)).ThrowOnError();

            return value != null;
        }

        /// <inheritdoc />
        public override void SetGlobalEntry(string key, string? value)
        {
            _keyValueStore.Use(
                static (store, state) =>
                {
                    if (state.value == null)
                    {
                        store.Remove(state.key);
                    }
                    else
                    {
                        store.Put(state.key, state.value);
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
                    if (store.TryGetValue(state, out var result))
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

        protected override IEnumerable<(ShortHash key, ContentLocationEntry? entry)> EnumerateEntriesWithSortedKeysFromStorage(OperationContext context, EnumerationFilter? filter = null, bool returnKeysOnly = false)
        {
            throw new NotImplementedException();
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
        private static ContentLocationEntry? TryGetEntryCoreHelper(ShortHash hash, RocksDbStore store, RocksDbContentMetadataDatabase db)
        {
            ContentLocationEntry? result = null;
            if (store.TryGetValue(db.GetKey(hash), out var data, db.NameOf(Columns.Content)))
            {
                result = db.DeserializeContentLocationEntry(data);
            }
            // NOTE: This relies on the fact that updates to entries use copy modify write. Otherwise, we would need to merge
            // the entries between the current and former column group.
            // TODO: Write a test to detect if this breaks.
            else if (store.TryGetValue(db.GetKey(hash), out data, db.NameOf(Columns.Content, db.GetFormerColumnGroup())))
            {
                result = db.DeserializeContentLocationEntry(data);
            }

            return result;
        }

        /// <inheritdoc />
        internal override void Persist(OperationContext context, ShortHash hash, ContentLocationEntry? entry)
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

        /// <inheritdoc />
        internal override void PersistBatch(OperationContext context, IEnumerable<KeyValuePair<ShortHash, ContentLocationEntry>> pairs)
        {
            _keyValueStore.Use(static (store, state) => PersistBatchHelper(store, state.pairs, state.db), (pairs, db: this)).ThrowOnError();
        }

        private static Unit PersistBatchHelper(RocksDbStore store, IEnumerable<KeyValuePair<ShortHash, ContentLocationEntry>> pairs, RocksDbContentMetadataDatabase db)
        {
            store.ApplyBatch(
                pairs.SelectWithState(
                    static (kvp, db) => new KeyValuePair<byte[], byte[]?>(db.GetKey(kvp.Key), kvp.Value != null ? db.SerializeContentLocationEntry(kvp.Value) : null),
                    state: db));
            return Unit.Void;
        }

        private void SaveToDb(ShortHash hash, ContentLocationEntry entry)
        {
            _keyValueStore.Use(
                static (store, state) => SaveToDbHelper(state.hash, state.entry, store, state.db), (hash, entry, db: this)).ThrowOnError();
        }

        // NOTE: This should remain static to avoid allocations in Store
        private static Unit SaveToDbHelper(ShortHash hash, ContentLocationEntry entry, RocksDbStore store, RocksDbContentMetadataDatabase db)
        {
            var value = db.SerializeContentLocationEntry(entry);
            store.Put(db.GetKey(hash), value, db.NameOf(Columns.Content));

            return Unit.Void;
        }

        private void DeleteFromDb(ShortHash hash)
        {
            _keyValueStore.Use(
                static (store, state) => DeleteFromDbHelper(state.hash, store, state.db), (hash, db: this)).ThrowOnError();
        }

        // NOTE: This should remain static to avoid allocations in Delete
        private static Unit DeleteFromDbHelper(ShortHash hash, RocksDbStore store, RocksDbContentMetadataDatabase db)
        {
            store.Remove(db.GetKey(hash), db.NameOf(Columns.Content));
            return Unit.Void;
        }

        private ShortHash DeserializeKey(byte[] key)
        {
            return new ShortHash(new ReadOnlyFixedBytes(key));
        }

        private byte[] GetKey(ShortHash hash)
        {
            return hash.ToByteArray();
        }

        /// <inheritdoc />
        public override Result<MetadataEntry?> GetMetadataEntry(OperationContext context, StrongFingerprint strongFingerprint, bool touch)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override Result<IReadOnlyList<Selector>> GetSelectors(OperationContext context, Fingerprint weakFingerprint)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override IEnumerable<Result<StrongFingerprint>> EnumerateStrongFingerprints(OperationContext context)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override IEnumerable<ShortHash> EnumerateSortedKeysFromStorage(OperationContext context)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override Possible<bool> TryUpsert(OperationContext context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism replacement, Func<MetadataEntry, bool> shouldReplace, DateTime? lastAccessTimeUtc)
        {
            throw new NotImplementedException();
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

            public Possible<Unit> Replace(KeyValueStoreAccessor accessor, Action<RocksDbStore> action)
            {
                _killSwitch.Cancel();

                using var token = _accessorLock.AcquireWriteLock();

                _accessor.Dispose();
                _accessor = accessor;

                _killSwitch.Dispose();
                _killSwitch = new CancellationTokenSource();

                return _accessor.Use(action);
            }

            public Possible<TResult> UseExclusive<TState, TResult>(Func<RocksDbStore, TState, TResult> action, TState state)
            {
                using var token = _accessorLock.AcquireWriteLock();
                return _accessor.Use(action, state);
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
