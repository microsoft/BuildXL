// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.SymbolStore;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using RocksDbSharp;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public class RocksDbContentMetadataDatabaseConfiguration : RocksDbContentLocationDatabaseConfiguration
    {
        public RocksDbContentMetadataDatabaseConfiguration(AbsolutePath storeLocation)
            : base(storeLocation)
        {
        }

        public TimeSpan ContentRotationInterval { get; set; } = TimeSpan.FromHours(6);

        public TimeSpan MetadataRotationInterval { get; set; } = TimeSpan.FromDays(7);

        public TimeSpan BlobRotationInterval { get; set; } = TimeSpan.FromHours(1);
    }

    /// <summary>
    /// RocksDb-based version of <see cref="ContentLocationDatabase"/>.
    /// </summary>
    public sealed class RocksDbContentMetadataDatabase : ContentLocationDatabase
    {
        private readonly RocksDbContentMetadataDatabaseConfiguration _configuration;

        protected override Tracer Tracer { get; } = new Tracer(nameof(RocksDbContentMetadataDatabase));

        private KeyValueStoreGuard _keyValueStore;
        private const string ActiveStoreSlotFileName = "activeSlot.txt";

        private StoreSlot _activeSlot = StoreSlot.Slot1;
        private readonly string _activeSlotFilePath;

        private Dictionary<Columns, ColumnMetadata> _columnMetadata = EnumTraits<Columns>
            .EnumerateValues()
            .ToDictionary(c => c, _ => new ColumnMetadata(group: ColumnGroup.One, lastGcTimeUtc: DateTime.MinValue));

        private string? _storeLocation;

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
        public enum Columns
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

            MetadataHeaders,

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

            // WARNING: do NOT rename!. Doing so will cause issues in production databases, because the name of the
            // GlobalKey is used inside the DBs.
            ActiveColummGroup,

            ActiveColumnGroups,
        }

        /// <inheritdoc />
        public RocksDbContentMetadataDatabase(IClock clock, RocksDbContentMetadataDatabaseConfiguration configuration)
            : base(clock, configuration, () => Array.Empty<MachineId>())
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

            // We backup the logs right before loading the first DB we load
            var storeLocation = GetStoreLocation(activeSlot);

            var result = Load(context, activeSlot, clean);

            var reload = false;

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

                var dbAlreadyExists = Directory.Exists(storeLocation);
                Directory.CreateDirectory(storeLocation);

                Tracer.Info(context, $"Creating RocksDb store at '{storeLocation}'. Clean={clean}, Configured Epoch='{_configuration.Epoch}'");

                var possibleStore = KeyValueStoreAccessor.Open(
                    new RocksDbStoreConfiguration(storeLocation)
                    {
                        AdditionalColumns = ColumnNames,
                        RotateLogsMaxFileSizeBytes = 0L,
                        RotateLogsNumFiles = 60,
                        RotateLogsMaxAge = TimeSpan.FromHours(12),
                        EnableStatistics = true,
                        FastOpen = true,
                        LeveledCompactionDynamicLevelTargetSizes = true,
                        Compression = RocksDbSharp.Compression.Zstd,
                        UseReadOptionsWithSetTotalOrderSeekInDbEnumeration = true,
                        UseReadOptionsWithSetTotalOrderSeekInGarbageCollection = true,
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

                        _keyValueStore.UseExclusive(static (db, @this) =>
                        {
                            @this._columnMetadata = @this.LoadColumnGroups(db);
                            @this.SaveColumnGroups(db, @this._columnMetadata);
                            return true;
                        },
                        this).ThrowOnError();
                    }
                    else
                    {
                        // Just replace the inner accessor
                        oldKeyValueStore.Replace(store, (db, @this) =>
                        {
                            @this._columnMetadata = @this.LoadColumnGroups(db);
                            @this.SaveColumnGroups(db, @this._columnMetadata);
                        }, this).ThrowOnError();
                    }

                    _activeSlot = activeSlot;
                    _storeLocation = storeLocation;
                }

                return possibleStore.Succeeded ? BoolResult.Success : new BoolResult($"Failed to initialize a RocksDb store at {storeLocation}:", possibleStore.Failure.DescribeIncludingInnerFailures());
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                return new BoolResult(ex);
            }
        }

        private class ColumnMetadata
        {
            public ColumnGroup Group { get; set; }

            public DateTime LastGcTimeUtc { get; set; }

            public ColumnMetadata(ColumnGroup group, DateTime lastGcTimeUtc)
            {
                Group = group;
                LastGcTimeUtc = lastGcTimeUtc;
            }
        }

        public ColumnGroup GetCurrentColumnGroup(Columns column)
        {
            return _columnMetadata[column].Group;
        }

        private Dictionary<Columns, ColumnMetadata> LoadColumnGroups(RocksDbStore db)
        {
            var now = Clock.UtcNow;
            var defaulted = EnumTraits<Columns>
                        .EnumerateValues()
                        .ToDictionary(c => c, _ => new ColumnMetadata(group: ColumnGroup.One, lastGcTimeUtc: now));

            if (db.TryGetValue(nameof(GlobalKeys.ActiveColumnGroups), out var serializedActiveColumnGroups))
            {
                // Current version of the DB, has a dictionary of column metadata
                if (!TryDeserializeColumnMetadata(serializedActiveColumnGroups, out var activeColumnGroups))
                {
                    return defaulted;
                }

                // If we happened to add any new columns in the current version of the code, it is possible they won't
                // be a part of the currently stored information.
                foreach (var column in EnumTraits<Columns>.EnumerateValues())
                {
                    if (activeColumnGroups.TryGetValue(column, out _))
                    {
                        continue;
                    }

                    activeColumnGroups[column] = new ColumnMetadata(group: ColumnGroup.One, lastGcTimeUtc: now);
                }

                return activeColumnGroups;
            }
            else if (db.TryGetValue(nameof(GlobalKeys.ActiveColummGroup), out var activeColumnGroup))
            {
                // Old version of the DB, has column group for all columns at the same version
                var group = (ColumnGroup)Enum.Parse(typeof(ColumnGroup), activeColumnGroup);

                return EnumTraits<Columns>
                    .EnumerateValues()
                    .ToDictionary(c => c, _ => new ColumnMetadata(group: group, lastGcTimeUtc: now));
            }
            else
            {
                return defaulted;
            }
        }

        private bool TryDeserializeColumnMetadata(string activeColumnGroups, [NotNullWhen(true)] out Dictionary<Columns, ColumnMetadata>? output)
        {
            try
            {
#pragma warning disable CS8762
                output = new Dictionary<Columns, ColumnMetadata>();

                // We need to do this because System.Text.Json doesn't support non-string keyed dicts
                var temporary = JsonSerializer.Deserialize<Dictionary<string, ColumnMetadata>>(activeColumnGroups);
                if (temporary is null)
                {
                    return false;
                }

                foreach (var kvp in temporary!)
                {
                    if (!Enum.TryParse<Columns>(kvp.Key, out var column))
                    {
                        // Skip columns that no longer exist
                        continue;
                    }

                    output[column] = kvp.Value;
                }

                return true;
#pragma warning restore CS8762
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception)
            {
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                output = null;
                return false;
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        private void SaveColumnGroups(RocksDbStore db, Dictionary<Columns, ColumnMetadata> metadata)
        {
            // We need to do this because System.Text.Json doesn't support non-string keyed dicts
            var temporary = metadata.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);

            db.Put(nameof(GlobalKeys.ActiveColumnGroups), JsonSerializer.Serialize(temporary));
            db.Remove(nameof(GlobalKeys.ActiveColummGroup));
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

        public override Task<BoolResult> GarbageCollectAsync(OperationContext context)
        {
            return GarbageCollectAsync(context, force: false);
        }

        /// <inheritdoc />
        public Task<BoolResult> GarbageCollectAsync(OperationContext context, bool force)
        {
            return context.PerformOperationAsync(Tracer,
               () =>
               {
                   var now = Clock.UtcNow;

                   // Get exclusive lock to prevent concurrent access while deleting column families
                   // without this, we might try to read from a column family which does not exist
                   return _keyValueStore.UseExclusive(
                       (store, _) =>
                       {
                           foreach (var column in EnumTraits<Columns>.EnumerateValues())
                           {
                               var rotationInterval = column switch
                               {
                                   Columns.Content => _configuration.ContentRotationInterval,
                                   Columns.Metadata => _configuration.MetadataRotationInterval,
                                   Columns.MetadataHeaders => _configuration.MetadataRotationInterval,
                                   Columns.Blobs => _configuration.BlobRotationInterval,
                                   _ => _configuration.GarbageCollectionInterval,
                               };

                               var delta = now - _columnMetadata[column].LastGcTimeUtc;

                               if (!force && (rotationInterval <= TimeSpan.Zero || delta < rotationInterval))
                               {
                                   Tracer.Info(context, $"Skipping garbage collection for column family {NameOf(column)}. Now=[{now}] LastRotation=[{_columnMetadata[column].LastGcTimeUtc}] RotationInterval=[{rotationInterval}] Delta=[{delta}]");
                                   continue;
                               }

                               Tracer.Info(context, $"Garbage collecting column family {NameOf(column)}. Now=[{now}] LastRotation=[{_columnMetadata[column].LastGcTimeUtc}] RotationInterval=[{rotationInterval}] Delta=[{delta}] Force=[{force}]");
                               garbageCollectColumnFamily(context, store, column);
                           }

                           return BoolResult.SuccessTask;
                       },
                       this).ThrowOnError();
               },
               counter: Counters[ContentLocationDatabaseCounters.GarbageCollectContent],
               isCritical: true);

            void garbageCollectColumnFamily(OperationContext context, RocksDbStore store, Columns column)
            {
                var nextGroup = GetFormerColumnGroup(column);
                var nextName = NameOf(column, nextGroup);

                var msg = $"Column=[{column}] NextGroup=[{nextGroup}] NextName=[{nextName}]";

                context.PerformOperation(Tracer, () =>
                    {
                        // Clear the column family by dropping and recreating
                        store.DropColumnFamily(nextName);
                        store.CreateColumnFamily(nextName);
                        _columnMetadata[column] = new ColumnMetadata(group: nextGroup, lastGcTimeUtc: Clock.UtcNow);
                        SaveColumnGroups(store, _columnMetadata);
                        return BoolResult.Success;
                    },
                    extraStartMessage: msg,
                    messageFactory: _ => msg,
                    traceOperationStarted: false,
                    caller: "GarbageCollectColumnFamily").IgnoreFailure();
            }
        }

        private ColumnGroup GetFormerColumnGroup(Columns columnFamily)
        {
            return _columnMetadata[columnFamily].Group == ColumnGroup.One
                ? ColumnGroup.Two
                : ColumnGroup.One;
        }

        private string NameOf(Columns columnFamily, ColumnGroup? group = null)
        {
            return ColumnNames[(int)(group ?? _columnMetadata[columnFamily].Group) * EnumTraits<Columns>.ValueCount + (int)columnFamily];
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
                (key: key.ToByteArray(), value: value, db: this)).ThrowOnError();
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
                    else if (store.TryGetValue(state.key, out result, state.db.NameOf(Columns.Blobs, state.db.GetFormerColumnGroup(Columns.Blobs))))
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

            // TODO: unify with RocksDbContentLocationDatabase
            using var keyHandle = hash.ToPooledByteArray();
            if (db.TryGetPinnableValue(store, keyHandle.Value, out var data, Columns.Content))
            {
                result = db.DeserializeContentLocationEntry(data.Value);
            }

            return result;
        }

        private ContentLocationEntry DeserializeContentLocationEntry(RocksDbPinnableSpan span)
        {
            // Please do not convert the delegate to a method group, because this code is called many times
            // and method group allocates a delegate on each conversion to a delegate.
            using (span)
            {
                unsafe
                {
                    using var stream = new UnmanagedMemoryStream((byte*)span.ValuePtr.ToPointer(), (long)span.LengthPtr);
                    return SerializationPool.Deserialize(stream, static reader => ContentLocationEntry.Deserialize(reader));
                }
            }
        }

        private bool TryGetValue(RocksDbStore store, byte[] key, [NotNullWhen(true)] out byte[]? value, Columns columns)
        {
            return store.TryGetValue(key, out value, NameOf(columns))
                || store.TryGetValue(key, out value, NameOf(columns, GetFormerColumnGroup(columns)));
        }

        private bool TryGetPinnableValue(RocksDbStore store, byte[] key, [NotNullWhen(true)] out RocksDbPinnableSpan? value, Columns columns)
        {
            return store.TryGetPinnableValue(key, out value, NameOf(columns))
                || store.TryGetPinnableValue(key, out value, NameOf(columns, GetFormerColumnGroup(columns)));
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

        private void SaveToDb(ShortHash hash, ContentLocationEntry entry)
        {
            _keyValueStore.Use(
                static (store, state) => SaveToDbHelper(state.hash, state.entry, store, state.db), (hash, entry, db: this)).ThrowOnError();
        }

        // NOTE: This should remain static to avoid allocations in Store
        private static Unit SaveToDbHelper(ShortHash hash, ContentLocationEntry entry, RocksDbStore store, RocksDbContentMetadataDatabase db)
        {
            using var value = db.SerializeContentLocationEntry(entry);
            store.Put(db.GetKey(hash).AsSpan(), value, db.NameOf(Columns.Content));

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

        /// <nodoc />
        public Result<SerializedMetadataEntry> GetSerializedContentHashList(OperationContext context, StrongFingerprint strongFingerprint)
        {
            // This method calls _keyValueStore.Use with non-static lambda, because this code is complicated
            // and not as perf critical as other places.
            var key = GetMetadataKey(strongFingerprint);
            var result = _keyValueStore.Use(
                store =>
                {
                    using (_metadataLocks[strongFingerprint.WeakFingerprint[0]].AcquireReadLock())
                    {
                        if (TryGetValue(store, key, out var headerData, Columns.MetadataHeaders)
                            && TryGetValue(store, key, out var data, Columns.Metadata))
                        {
                            var header = DeserializeMetadataEntryHeader(headerData);

                            // Update last access time in database
                            header.LastAccessTimeUtc = Clock.UtcNow;

                            using var serializedHeader = SerializeMetadataEntryHeader(header);
                            store.Put(key.AsSpan(), serializedHeader, NameOf(Columns.MetadataHeaders));

                            return new SerializedMetadataEntry()
                            {
                                ReplacementToken = header.ReplacementToken,
                                Data = data,
                            };
                        }
                        else
                        {
                            return null;
                        }
                    }
                });

            return result.ToResult(isNullAllowed: true)!;
        }

        /// <inheritdoc />
        public override Result<MetadataEntry?> GetMetadataEntry(OperationContext context, StrongFingerprint strongFingerprint, bool touch)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Fine-grained locks that used for all operations that mutate Metadata records.
        /// </summary>
        private readonly ReaderWriterLockSlim[] _metadataLocks = Enumerable.Range(0, byte.MaxValue + 1).Select(s => new ReaderWriterLockSlim()).ToArray();

        public override Possible<bool> TryUpsert(OperationContext context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism replacement, Func<MetadataEntry, bool> shouldReplace, DateTime? lastAccessTimeUtc)
        {
            throw new NotImplementedException();
        }

        /// <nodoc />
        public Possible<bool> CompareExchange(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            SerializedMetadataEntry replacement,
            string expectedReplacementToken,
            DateTime? lastAccessTimeUtc)
        {
            return _keyValueStore.Use(
                store =>
                {
                    var key = GetMetadataKey(strongFingerprint);

                    using (_metadataLocks[strongFingerprint.WeakFingerprint[0]].AcquireWriteLock())
                    {
                        MetadataEntryHeader header = default;

                        if (TryGetValue(store, key, out var headerData, Columns.MetadataHeaders))
                        {
                            header = DeserializeMetadataEntryHeader(headerData);

                            // If sequence number is present, we are replaying meaning
                            // the latest sequence number should take precedence
                            bool shouldReplace = replacement.SequenceNumber == null
                                ? header.ReplacementToken == expectedReplacementToken
                                : replacement.SequenceNumber > header.SequenceNumber;
                            if (!shouldReplace)
                            {
                                return false;
                            }

                            // Set the sequence number so it can be used when replaying
                            replacement.SequenceNumber ??= header.SequenceNumber + 1;
                        }

                        header = new MetadataEntryHeader()
                        {
                            LastAccessTimeUtc = lastAccessTimeUtc ?? Clock.UtcNow,
                            ReplacementToken = replacement.ReplacementToken,
                            SequenceNumber = replacement.SequenceNumber ?? 1,
                        };

                        // Don't put if content hash list is null since this represents a touch which arrived before
                        // the initial put for the content hash list.
                        if (replacement.Data != null)
                        {
                            store.Put(key, replacement.Data, NameOf(Columns.Metadata));
                        }

                        using var serializedHeader = SerializeMetadataEntryHeader(header);
                        store.Put(key.AsSpan(), serializedHeader, NameOf(Columns.MetadataHeaders));
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
                    foreach (var kvp in store.PrefixSearch((byte[]?)null, nameof(Columns.Metadata)))
                    {
                        // TODO(jubayard): since this method only needs the keys and not the values, it wouldn't hurt
                        // to make an alternative prefix search that doesn't even read the values from RocksDB.
                        var strongFingerprint = state.@this.DeserializeStrongFingerprint(kvp.Key);
                        state.result.Add(Result.Success(strongFingerprint));
                    }

                    return state.result;
                }, (result: result, @this: this));

            if (!status.Succeeded)
            {
                result.Add(new Result<StrongFingerprint>(status.Failure.CreateException()));
            }

            return result;
        }

        /// <inheritdoc />
        protected override IEnumerable<ShortHash> EnumerateSortedKeysFromStorage(OperationContext context)
        {
            throw new NotImplementedException();
        }

        private static readonly Comparer<(DateTime TimeUtc, Selector Selector)> SelectorComparer =
            Comparer<(DateTime TimeUtc, Selector Selector)>.Create((x, y) => y.TimeUtc.CompareTo(x.TimeUtc));

        /// <inheritdoc />
        public override Result<IReadOnlyList<Selector>> GetSelectors(OperationContext context, Fingerprint weakFingerprint)
        {
            var selectors = new List<(DateTime TimeUtc, Selector Selector)>();
            var status = _keyValueStore.Use(
                static (store, state) =>
                {
                    var @this = state.@this;
                    var key = @this.SerializeWeakFingerprint(state.weakFingerprint);

                    // This only works because the strong fingerprint serializes the weak fingerprint first. Hence,
                    // we know that all keys here are strong fingerprints that match the weak fingerprint.
                    foreach (var kvp in state.@this.PrefixSearch(store, key, Columns.MetadataHeaders))
                    {
                        var strongFingerprint = @this.DeserializeStrongFingerprint(kvp.Key);
                        var timeUtc = @this.DeserializeMetadataEntryHeader(kvp.Value).LastAccessTimeUtc;
                        state.selectors.Add((timeUtc, strongFingerprint.Selector));
                    }

                    return Unit.Void;
                }, (selectors: selectors, @this: this, weakFingerprint: weakFingerprint));

            if (!status.Succeeded)
            {
                return new Result<IReadOnlyList<Selector>>(status.Failure.CreateException());
            }

            selectors.Sort(SelectorComparer);

            return new Result<IReadOnlyList<Selector>>(selectors.SelectList(t => t.Selector));
        }

        private IEnumerable<KeyValuePair<byte[], byte[]>> PrefixSearch(RocksDbStore store, byte[] key, Columns column)
        {
            return store.PrefixSearch(key, columnFamilyName: NameOf(column))
                .Concat(store.PrefixSearch(key, columnFamilyName: NameOf(column, GetFormerColumnGroup(column))));
        }

        private byte[] SerializeWeakFingerprint(Fingerprint weakFingerprint)
        {
            return SerializationPool.Serialize(weakFingerprint, static (instance, writer) => instance.Serialize(writer));
        }

        private byte[] SerializeStrongFingerprint(StrongFingerprint strongFingerprint)
        {
            return SerializationPool.Serialize(strongFingerprint, static (instance, writer) => instance.Serialize(writer));
        }

        private StrongFingerprint DeserializeStrongFingerprint(byte[] bytes)
        {
            return SerializationPool.Deserialize(bytes, static reader => StrongFingerprint.Deserialize(reader));
        }

        private byte[] GetMetadataKey(StrongFingerprint strongFingerprint)
        {
            return SerializeStrongFingerprint(strongFingerprint);
        }

        private PooledBuffer SerializeMetadataEntryHeader(MetadataEntryHeader value)
        {
            return SerializationPool.SerializePooled(value, static (instance, writer) => MetadataServiceSerializer.TypeModel.Serialize(writer.BaseStream, instance));
        }

        private MetadataEntryHeader DeserializeMetadataEntryHeader(ReadOnlySpan<byte> data)
        {
            return MetadataServiceSerializer.TypeModel.Deserialize<MetadataEntryHeader>((ReadOnlySpan<byte>)data);
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

            public Possible<Unit> Replace<TState>(KeyValueStoreAccessor accessor, Action<RocksDbStore, TState> action, TState state)
            {
                _killSwitch.Cancel();

                using var token = _accessorLock.AcquireWriteLock();

                _accessor.Dispose();
                _accessor = accessor;

                _killSwitch.Dispose();
                _killSwitch = new CancellationTokenSource();

                return _accessor.Use(static (store, state) =>
                {
                    state.action(store, state.state);
                    return Unit.Void;
                }, (action, state));
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
