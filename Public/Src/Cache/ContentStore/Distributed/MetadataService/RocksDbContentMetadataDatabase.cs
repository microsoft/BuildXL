// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using RocksDbSharp;
using static BuildXL.Cache.ContentStore.Distributed.MetadataService.RocksDbOperations;
using static BuildXL.Engine.Cache.KeyValueStores.RocksDbStore;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public class RocksDbContentMetadataDatabaseConfiguration : RocksDbContentLocationDatabaseConfiguration
    {
        public RocksDbContentMetadataDatabaseConfiguration(AbsolutePath storeLocation)
            : base(storeLocation)
        {
            TraceOperations = false;
        }

        public TimeSpan ContentRotationInterval { get; set; } = TimeSpan.FromHours(6);

        public TimeSpan MetadataRotationInterval { get; set; } = TimeSpan.FromDays(7);

        public ByteSizeSetting? MetadataSizeRotationThreshold { get; set; }
    }

    /// <summary>
    /// RocksDb-based version of <see cref="ContentLocationDatabase"/> used by the global location store.
    /// </summary>
    public class RocksDbContentMetadataDatabase : ContentLocationDatabase
    {
        private readonly RocksDbContentMetadataDatabaseConfiguration _configuration;

        protected override Tracer Tracer { get; } = new Tracer(nameof(RocksDbContentMetadataDatabase));

        private KeyValueStoreGuard _keyValueStore;
        private const string ActiveStoreSlotFileName = "activeSlot.txt";

        private readonly IAbsFileSystem _fileSystem = new PassThroughFileSystem();

        private StoreSlot _activeSlot = StoreSlot.Slot1;
        private readonly string _activeSlotFilePath;

        private Dictionary<Columns, ColumnMetadata> _columnMetadata = EnumTraits<Columns>
            .EnumerateValues()
            .ToDictionary(c => c, _ => new ColumnMetadata(group: ColumnGroup.One, lastGcTimeUtc: DateTime.MinValue));

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

            MergeContent,

            SstMergeContent
        }

        public enum ColumnGroup
        {
            One,

            Two
        }

        private static bool IsRotatedColumn(Columns columns)
        {
            return columns != Columns.SstMergeContent;
        }

        private static readonly string[][] ColumnNames =
            EnumTraits<Columns>
                    .EnumerateValues()
                    .Select(c => !IsRotatedColumn(c) ? new[] { c.ToString() } : Enumerable.Range(1, 2).Select(i => c.ToString() + i).ToArray())
                    .ToArray();

        private static ReadOnlyArray<string> AllMergeContentColumnNames { get; } =
            NamesOf(Columns.MergeContent).Concat(NamesOf(Columns.SstMergeContent)).ToReadOnlyArray();

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

        /// <summary>
        /// Gets a temporary directory inside the database root
        /// </summary>
        internal DisposableDirectory CreateTempDirectory(string baseName)
        {
            return new DisposableDirectory(_fileSystem, _configuration.StoreLocation / "tmp" / baseName + Path.GetRandomFileName());
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

        private MergeOperator MergeContentMergeOperator { get; } = MergeOperators.CreateAssociative(
                        "MergeContent",
                        merge: RocksDbOperations.MergeLocations,
                        transformSingle: RocksDbOperations.ProcessSingleLocationEntry);

        private bool IsStoredEpochInvalid([NotNullWhen(true)] out string? epoch)
        {
            TryGetGlobalEntry(nameof(GlobalKeys.StoredEpoch), out epoch);
            return _configuration.Epoch != epoch;
        }

        private Dictionary<string, MergeOperator> GetMergeOperators()
        {
            IEnumerable<(Columns column, MergeOperator merger)> enumerate()
            {
                yield return
                (
                    Columns.MergeContent,
                    MergeContentMergeOperator
                );

                yield return
                (
                    Columns.SstMergeContent,
                    MergeContentMergeOperator
                );
            }

            return enumerate()
                .SelectMany(t => NamesOf(t.column).Select(name => (name, t.merger)))
                .ToDictionary(t => t.name, t => t.merger);
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

                var settings = new RocksDbStoreConfiguration(storeLocation)
                               {
                                   AdditionalColumns = ColumnNames.SelectMany(n => n),
                                   RotateLogsMaxFileSizeBytes = 0L,
                                   RotateLogsNumFiles = 60,
                                   RotateLogsMaxAge = TimeSpan.FromHours(12),
                                   EnableStatistics = true,
                                   FastOpen = true,
                                   LeveledCompactionDynamicLevelTargetSizes = true,
                                   Compression = Compression.Zstd,
                                   UseReadOptionsWithSetTotalOrderSeekInDbEnumeration = true,
                                   UseReadOptionsWithSetTotalOrderSeekInGarbageCollection = true,
                                   MergeOperators = GetMergeOperators()
                               };

                RocksDbUtilities.ConfigureRocksDbTracingIfNeeded(context, _configuration, settings, Tracer, componentName: nameof(RocksDbContentMetadataDatabase));

                Tracer.Info(context, $"Creating RocksDb store at '{storeLocation}'. Clean={clean}, Configured Epoch='{_configuration.Epoch}', TracingLevel={_configuration.RocksDbTracingLevel}");

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
                    var oldKeyValueStore = _keyValueStore;
                    var store = possibleStore.Result;

                    if (oldKeyValueStore == null)
                    {
                        _keyValueStore = new KeyValueStoreGuard(store);

                        _keyValueStore.UseExclusive((db, _) =>
                        {
                            _columnMetadata = LoadColumnGroups(context, db);
                            SaveColumnGroups(db, _columnMetadata);
                            return true;
                        },
                        Unit.Void).ThrowOnError();
                    }
                    else
                    {
                        // Just replace the inner accessor
                        oldKeyValueStore.Replace(store, (db, _) =>
                        {
                            _columnMetadata = LoadColumnGroups(context, db);
                            SaveColumnGroups(db, _columnMetadata);
                        }, Unit.Void).ThrowOnError();
                    }

                    _activeSlot = activeSlot;
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

            /// <summary>
            /// Parameterless constructor needed for json serialization
            /// </summary>
            public ColumnMetadata()
            {
            }
        }

        public ColumnGroup GetCurrentColumnGroup(Columns column)
        {
            return _columnMetadata[column].Group;
        }

        private Dictionary<Columns, ColumnMetadata> LoadColumnGroups(OperationContext context, RocksDbStore db)
        {
            var now = Clock.UtcNow;
            var defaulted = EnumTraits<Columns>
                        .EnumerateValues()
                        .ToDictionary(c => c, _ => new ColumnMetadata(group: ColumnGroup.One, lastGcTimeUtc: now));

            if (db.TryGetValue(nameof(GlobalKeys.ActiveColumnGroups), out var serializedActiveColumnGroups))
            {
                // Current version of the DB, has a dictionary of column metadata
                if (!TryDeserializeColumnMetadata(context, serializedActiveColumnGroups, out var activeColumnGroups))
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

        private bool TryDeserializeColumnMetadata(OperationContext context, string activeColumnGroups, [NotNullWhen(true)] out Dictionary<Columns, ColumnMetadata>? output)
        {
            try
            {
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
            }
            catch (Exception ex)
            {
                context.TracingContext.Warning(ex, "Error deserializing column metadata", Tracer.Name);
                output = null;
                return false;
            }
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

        public Task<BoolResult> GarbageCollectAsync(OperationContext context, bool force)
        {
            return Task.FromResult(GarbageCollectColumns(context, force));
        }

        /// <inheritdoc />
        public BoolResult GarbageCollectColumns(OperationContext context, bool force)
        {
            return context.PerformOperation(Tracer,
               () =>
               {
                   var now = Clock.UtcNow;

                   // Get exclusive lock to prevent concurrent access while deleting column families
                   // without this, we might try to read from a column family which does not exist
                   return _keyValueStore.UseExclusive(
                       (store, _) =>
                       {
                           foreach (var column in EnumTraits<Columns>.EnumerateValues().Where(IsRotatedColumn))
                           {
                               if (column == Columns.MetadataHeaders)
                               {
                                   // Metadata headers is handled with Metadata column
                                   continue;
                               }

                               var rotationInterval = column switch
                               {
                                   Columns.Content => _configuration.ContentRotationInterval,
                                   Columns.MergeContent => _configuration.ContentRotationInterval,
                                   Columns.Metadata => _configuration.MetadataRotationInterval,
                                   _ => _configuration.GarbageCollectionInterval,
                               };

                               var delta = now - _columnMetadata[column].LastGcTimeUtc;

                               var sizeInfo = new[]
                               {
                                   GetColumnSizeInfo(context, store, NameOf(column, ColumnGroup.One)),
                                   GetColumnSizeInfo(context, store, NameOf(column, ColumnGroup.Two)),
                               };

                               if (column == Columns.Metadata)
                               {
                                   sizeInfo = sizeInfo.Concat(new[]
                                       {
                                           GetColumnSizeInfo(context, store, NameOf(Columns.MetadataHeaders, ColumnGroup.One)),
                                           GetColumnSizeInfo(context, store, NameOf(Columns.MetadataHeaders, ColumnGroup.Two)),
                                       }).ToArray();
                               }

                               var totalSize = sizeInfo.Sum(s => s.EffectiveSize);

                               bool shouldRotate(out long maxSize)
                               {
                                   maxSize = -1;
                                   if (force)
                                   {
                                       return true;
                                   }

                                   if (column == Columns.Metadata
                                        && _configuration.MetadataSizeRotationThreshold is ByteSizeSetting sizeRotationThreshold)
                                   {
                                       maxSize = sizeRotationThreshold.Value;
                                       return totalSize >= maxSize;
                                   }
                                   else
                                   {
                                       return rotationInterval > TimeSpan.Zero && delta >= rotationInterval;
                                   }
                               }

                               bool skip = !shouldRotate(out var maxSize);
                               Tracer.Info(context, $"Garbage collection for column family {NameOf(column)}. Skip[{skip}]. Now=[{now}] " +
                                   $"LastRotation=[{_columnMetadata[column].LastGcTimeUtc}] RotationInterval=[{rotationInterval}] " +
                                   $"Delta=[{delta}] Force=[{force}] MaxSize={maxSize}] {string.Join(", ", sizeInfo.AsEnumerable())}");

                               if (!skip)
                               {
                                   GarbageCollectColumnFamily(context, store, column).IgnoreFailure();
                                   if (column == Columns.Metadata)
                                   {
                                       GarbageCollectColumnFamily(context, store, Columns.MetadataHeaders).IgnoreFailure();
                                   }
                               }
                           }

                           return BoolResult.Success;
                       },
                       this).ThrowOnError();
               },
               counter: Counters[ContentLocationDatabaseCounters.GarbageCollectContent],
               isCritical: true);
        }

        /// <summary>
        /// Internal for testing purposes only.
        /// </summary>
        internal BoolResult GarbageCollectColumnFamily(OperationContext context, Columns column)
        {
            return _keyValueStore.UseExclusive(
                (store, _) =>
                {
                    return GarbageCollectColumnFamily(context, store, column);
                },
                this).ThrowOnError();
        }

        private BoolResult GarbageCollectColumnFamily(OperationContext context, RocksDbStore store, Columns column)
        {
            var nextGroup = GetFormerColumnGroup(column);
            var nextName = NameOf(column, nextGroup);

            var msg = $"Column=[{column}] NextGroup=[{nextGroup}] NextName=[{nextName}]";

            return context.PerformOperation(Tracer, () =>
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
                caller: "GarbageCollectColumnFamily");
        }

        private ColumnGroup GetFormerColumnGroup(Columns columnFamily)
        {
            return _columnMetadata[columnFamily].Group == ColumnGroup.One
                ? ColumnGroup.Two
                : ColumnGroup.One;
        }

        private static string[] NamesOf(Columns columnFamily)
        {
            return ColumnNames[(int)columnFamily];
        }

        public string NameOf(Columns columnFamily, ColumnGroup? group = null)
        {
            return NameOf(columnFamily, out _, group);
        }

        private string NameOf(Columns columnFamily, out ColumnGroup resolvedGroup, ColumnGroup? group = null)
        {
            resolvedGroup = IsRotatedColumn(columnFamily)
                ? group ?? _columnMetadata[columnFamily].Group
                : ColumnGroup.One;
            return ColumnNames[(int)columnFamily][(int)resolvedGroup];
        }

        internal RocksDbStore UnsafeGetStore()
        {
            return _keyValueStore.Use(store => store).ToResult().Value!;
        }

        /// <summary>
        /// Ingests sst files from the given paths for the <see cref="Columns.SstMergeContent"/> column
        /// </summary>
        internal BoolResult IngestMergeContentSstFiles(OperationContext context, IEnumerable<AbsolutePath> files)
        {
            return _keyValueStore.Use(store => store.Database.IngestExternalFiles(
                files.Select(f => f.Path).ToArray(),
                new IngestExternalFileOptions().SetMoveFiles(true)
                ,
                store.GetColumn(NameOf(Columns.SstMergeContent))))
            .ToBoolResult();
        }

        /// <summary>
        /// Create an <see cref="SstFileWriter"/> at the given path for the <see cref="Columns.SstMergeContent"/> column
        /// </summary>
        internal Result<SstFileWriter> CreateContentSstWriter(OperationContext context, AbsolutePath path)
        {
            return CreateSstFileWriter(context, path, Columns.SstMergeContent);
        }

        /// <summary>
        /// Create an <see cref="SstFileWriter"/> at the given path for the given column
        /// </summary>
        private Result<SstFileWriter> CreateSstFileWriter(OperationContext context, AbsolutePath path, Columns columns)
        {
            return _keyValueStore.Use(store => store.CreateSstFileWriter(path.Path, NameOf(columns))).ToResult();
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

                GarbageCollectColumns(context, force: false).IgnoreFailure();

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

        protected override bool SetMachineExistenceAndUpdateDatabase(
            OperationContext context,
            ShortHash hash,
            MachineId? machine,
            bool existsOnMachine,
            long size,
            UnixTime? lastAccessTime,
            bool reconciling)
        {
            if (_configuration.UseMergeOperators)
            {
                return _keyValueStore.Use(
                    static (store, state) =>
                    {
                        store.ApplyBatch(
                            state,
                            state.db.NameOf(Columns.MergeContent),
                            static (batch, state, columnHandle) =>
                            {
                                CompactTime? lastAccessTime = state.lastAccessTime?.ToDateTime().ToCompactTime();
                                long? size = state.existsOnMachine ? state.size : (long?)null;
                                var columnWriter = new RocksDbColumnWriter(batch, columnHandle);

                                columnWriter.MergeLocationEntry(
                                    state.hash,
                                    state.machine,
                                    new MachineContentInfo(size, latestAccessTime: lastAccessTime),
                                    isRemove: !state.existsOnMachine);
                            });

                        return true;
                    },
                    (hash, size, lastAccessTime, existsOnMachine, machine, db: this)
                ).ThrowOnError();
            }

            return base.SetMachineExistenceAndUpdateDatabase(context, hash, machine, existsOnMachine, size, lastAccessTime, reconciling);
        }

        public bool LocationAdded(OperationContext context, MachineId machine, IReadOnlyList<ShortHashWithSize> hashes, bool touch)
        {
            if (_configuration.UseMergeOperators)
            {
                return _keyValueStore.Use(
                    static (store, state) =>
                    {
                        store.ApplyBatch(
                            state,
                            state.db.NameOf(Columns.MergeContent),
                            static (batch, state, columnHandle) =>
                            {
                                CompactTime? lastAccessTime = state.touch ? state.db.Clock.UtcNow : null;
                                var columnWriter = new RocksDbColumnWriter(batch, columnHandle);

                                foreach ((ShortHash hash, long size) in state.hashes.AsStructEnumerable())
                                {
                                    columnWriter.MergeLocationEntry(hash, state.machine, new MachineContentInfo(size, latestAccessTime: lastAccessTime));
                                }
                            });

                        return true;
                    },
                    (hashes, touch, machine, db: this)
                ).ThrowOnError();
            }
            else
            {
                foreach (var hash in hashes.AsStructEnumerable())
                {
                    base.LocationAdded(context, hash.Hash, machine, hash.Size, updateLastAccessTime: touch);
                }

                return true;
            }
        }

        private static Span<byte> TrimTrailingZeros(Span<byte> span)
        {
#if NETCOREAPP
            return span.TrimEnd<byte>(0);
#else
            for (int i = span.Length - 1; i >= 0; i--)
            {
                if (span[i] != 0)
                {
                    return span.Slice(0, i + 1);
                }
            }

            return Span<byte>.Empty;
#endif
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

        private static int SizeOf<T>()
            where T : unmanaged
        {
            Span<T> span = stackalloc T[1];
            var size = MemoryMarshal.AsBytes(span).Length;
            return size;
        }

        private static readonly ContentLocationEntry EmptyLocationEntry = ContentLocationEntry.Create(MachineIdSet.Empty, -1, DateTime.UtcNow, DateTime.UtcNow);

        // NOTE: This should remain static to avoid allocations in TryGetEntryCore
        private static ContentLocationEntry? TryGetEntryCoreHelper(ShortHash hash, RocksDbStore store, RocksDbContentMetadataDatabase db)
        {
            ContentLocationEntry? result = null;

            Span<long> size = stackalloc long[1];
            List<MachineId>? machineIdsBuffer = null;
            UnixTime? lastAccessTime = null;

            // AsSpan is safe here because 'hash' lives on the stack.
            var key = hash.AsSpanUnsafe();

            // 1. Read combined entry
            db.TryDeserializeValue(store, key, Columns.Content, static reader => ContentLocationEntry.Deserialize(ref reader), out result);

            // 2. Read merged entry
            foreach (var column in AllMergeContentColumnNames)
            {
                if (store.TryGetPinnableValue(MemoryMarshal.AsBytes(stackalloc[] { hash.AsEntryKey() }), out var mergeData, column))
                {
                    machineIdsBuffer ??= new List<MachineId>();

                    using var mergedValue = mergeData.Value;
                    ReadMergedContentLocationEntry(mergedValue.UnsafePin(), out var machines, out var info);
                    if (info.Size != null)
                    {
                        size[0] = info.Size.Value;
                    }

                    lastAccessTime = info.LatestAccessTime?.ToDateTime().ToUnixTime();

                    foreach (var machine in machines)
                    {
                        if (!machine.IsRemove)
                        {
                            machineIdsBuffer.Add(machine.AsMachineId());
                        }
                    }
                }
            }

            // Merge results
            if (machineIdsBuffer?.Count > 0)
            {
                result ??= ContentLocationEntry.Missing;
                result = result.SetMachineExistence(MachineIdCollection.Create(machineIdsBuffer), exists: true, size: size[0], lastAccessTime: lastAccessTime);
            }

            return result;
        }

        public Result<Optional<TResult>> TryDeserializeValue<TResult>(ReadOnlyMemory<byte> key, Columns columns, DeserializeValue<TResult> deserializer)
        {
            return _keyValueStore.Use((store, state) =>
                {
                    bool found = TryDeserializeValue(store, state.key.Span, state.columns, state.deserializer, out var result);
                    return (found, result);
                },
                (key, columns, deserializer))
            .Then(r => r.found ? new Optional<TResult>(r.result!) : default)
            .ToResult();
        }

        private bool TryGetValue(RocksDbStore store, ReadOnlySpan<byte> key, [NotNullWhen(true)] out byte[]? value, Columns columns)
        {
            return store.TryGetValue(key, out value, NameOf(columns))
                || store.TryGetValue(key, out value, NameOf(columns, GetFormerColumnGroup(columns)));
        }

        private bool TryRead(RocksDbStore store, ReadOnlySpan<byte> key, Span<byte> valueBuffer, Columns columns)
        {
            return store.TryReadValue(key, valueBuffer, NameOf(columns)) >= 0
                || store.TryReadValue(key, valueBuffer, NameOf(columns, GetFormerColumnGroup(columns))) >= 0;
        }

        public bool TryDeserializeValue<TResult>(RocksDbStore store, ReadOnlySpan<byte> key, Columns columns, DeserializeValue<TResult> deserializer, [NotNullWhen(true)] out TResult? result)
        {
            return store.TryDeserializeValue(key, NameOf(columns), deserializer, out result)
                   || IsRotatedColumn(columns) && store.TryDeserializeValue(key, NameOf(columns, GetFormerColumnGroup(columns)), deserializer, out result);
        }

        private bool TryGetValue(RocksDbStore store, ReadOnlySpan<byte> key, [NotNullWhen(true)] out byte[]? value, out ColumnGroup resolvedGroup, Columns columns)
        {
            return store.TryGetValue(key, out value, NameOf(columns, out resolvedGroup))
                || store.TryGetValue(key, out value, NameOf(columns, out resolvedGroup, GetFormerColumnGroup(columns)));
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

            // AsSpan is safe, because 'hash' variable lives on the stack.
            store.Put(hash.AsSpanUnsafe(), value.WrittenSpan, db.NameOf(Columns.Content));

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
            // AsSpan usage is safe here.
            store.Remove(hash.AsSpanUnsafe(), db.NameOf(Columns.Content));
            return Unit.Void;
        }

        /// <nodoc />
        public Result<SerializedMetadataEntry> GetSerializedContentHashList(OperationContext context, StrongFingerprint strongFingerprint)
        {
            // This method calls _keyValueStore.Use with non-static lambda, because this code is complicated
            // and not as perf critical as other places.
            using var keyHolder = GetMetadataKey(strongFingerprint);
            var key = keyHolder.Buffer;
            var result = _keyValueStore.Use(
                store =>
                {
                    using (_metadataLocks[GetMetadataLockIndex(strongFingerprint)].AcquireReadLock())
                    {
                        if (TryDeserializeValue(store, key.Span, Columns.MetadataHeaders, static reader => DeserializeMetadataEntryHeader(reader.Remaining), out var header)
                            && TryGetValue(store, key.Span, out var data, out var dataGroup, Columns.Metadata))
                        {
                            if (!_configuration.OpenReadOnly)
                            {
                                // Update last access time in database
                                header.LastAccessTimeUtc = Clock.UtcNow;

                                using var serializedHeader = SerializeMetadataEntryHeader(header);

                                // Ensure updated header goes to same group as data so there is not a case where
                                // data comes from different group than metadata
                                store.Put(key.Span, serializedHeader, NameOf(Columns.MetadataHeaders, dataGroup));
                            }

                            return new SerializedMetadataEntry()
                            {
                                ExternalDataStorageId = header.ExternalDataStorageId,
                                SequenceNumber = header.SequenceNumber,
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

        /// <inheritdoc />
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
                    using var keyHandle = GetMetadataKey(strongFingerprint);
                    var key = keyHandle.Buffer;

                    using (_metadataLocks[GetMetadataLockIndex(strongFingerprint)].AcquireWriteLock())
                    {
                        MetadataEntryHeader header = default;

                        // Just create a 1 byte span for testing if the data is present without
                        // actually reading. We need to test for the presence of the data block
                        // here to ensure we can replace if Metadata and MetadataHeaders are out of sync. 
                        Span<byte> dataSpan = stackalloc byte[1];

                        if (TryGetValue(store, key.Span, out var headerData, Columns.MetadataHeaders)
                            && TryRead(store, key.Span, dataSpan, Columns.Metadata))
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
                            ExternalDataStorageId = replacement.ExternalDataStorageId
                        };

                        // Don't put if content hash list is null since this represents a touch which arrived before
                        // the initial put for the content hash list.
                        if (replacement.Data != null)
                        {
                            store.Put(key.Span, replacement.Data, NameOf(Columns.Metadata));
                        }

                        using var serializedHeader = SerializeMetadataEntryHeader(header);
                        store.Put(key.Span, serializedHeader, NameOf(Columns.MetadataHeaders));
                    }

                    return true;
                });
        }

        public Result<IterateDbContentResult> IterateSstMergeContentEntries(OperationContext context, Action<MachineContentEntry> onEntry)
        {
            return _keyValueStore.Use(
                static (store, state) =>
                {
                    var hashKeySize = Unsafe.SizeOf<ShardHash>();
                    return store.IterateDbContent(
                        iterator =>
                        {
                            var key = iterator.Key();
                            if (key.Length != hashKeySize)
                            {
                                return;
                            }

                            var hash = MemoryMarshal.Read<ShardHash>(key);
                            RocksDbOperations.ReadMergedContentLocationEntry(iterator.Value(), out var machines, out var info);
                            foreach (var machine in machines)
                            {
                                var entry = new MachineContentEntry(hash, machine, info.Size!.Value, info.LatestAccessTime ?? CompactTime.Zero);
                                state.onEntry(entry);
                            }
                        },
                        state.@this.NameOf(Columns.SstMergeContent),
                        startValue: (byte[]?)null,
                        state.context.Token);

                }, (@this: this, onEntry, context))
                .ToResult();
        }

        /// <inheritdoc />
        public override IEnumerable<Result<StrongFingerprint>> EnumerateStrongFingerprints(OperationContext context)
        {
            var result = new List<Result<StrongFingerprint>>();
            var status = _keyValueStore.Use(
                static (store, state) =>
                {
                    store.PrefixKeyLookup(
                        state,
                        ReadOnlySpan<byte>.Empty,
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
                    state.@this.PrefixLookup(
                        store,
                        state,
                        key,
                        Columns.MetadataHeaders,
                        static (state, key, value) =>
                        {
                            var strongFingerprint = state.@this.DeserializeStrongFingerprint(key);
                            var timeUtc = DeserializeMetadataEntryHeader(value).LastAccessTimeUtc;
                            state.selectors.Add((timeUtc, strongFingerprint.Selector));
                            return true;
                        });

                    return Unit.Void;
                }, (selectors: selectors, @this: this, weakFingerprint: weakFingerprint));

            if (!status.Succeeded)
            {
                return new Result<IReadOnlyList<Selector>>(status.Failure.CreateException());
            }

            selectors.Sort(SelectorComparer);

            return new Result<IReadOnlyList<Selector>>(selectors.SelectList(t => t.Selector));
        }

        private void PrefixLookup<TState>(RocksDbStore store, TState state, ReadOnlySpan<byte> key, Columns column, RocksDbStore.ObserveKeyValuePairCallback<TState> observeCallback)
        {
            store.PrefixLookup(state, key, columnFamilyName: NameOf(column), observeCallback);
            store.PrefixLookup(state, key, columnFamilyName: NameOf(column, GetFormerColumnGroup(column)), observeCallback);
        }

        private void PrefixKeyLookup<TState>(RocksDbStore store, TState state, ReadOnlySpan<byte> key, Columns column, RocksDbStore.ObserveKeyCallback<TState> observeCallback)
        {
            store.PrefixKeyLookup(state, key, columnFamilyName: NameOf(column), observeCallback);
            store.PrefixKeyLookup(state, key, columnFamilyName: NameOf(column, GetFormerColumnGroup(column)), observeCallback);
        }

        private PooledBuffer SerializeMetadataEntryHeader(MetadataEntryHeader value)
        {
            return SerializationPool.SerializePooled(value, static (instance, writer) => MetadataServiceSerializer.TypeModel.Serialize(writer.BaseStream, instance));
        }

        private static MetadataEntryHeader DeserializeMetadataEntryHeader(ReadOnlySpan<byte> data)
        {
            return MetadataServiceSerializer.TypeModel.Deserialize<MetadataEntryHeader>((ReadOnlySpan<byte>)data);
        }

        public record ColumnSizeInfo(string? Column, long? LiveDataSizeBytes = null, long? LiveFilesSizeBytes = null)
        {
            public long EffectiveSize => LiveDataSizeBytes ?? LiveFilesSizeBytes ?? 0;
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

        protected virtual ColumnSizeInfo GetColumnSizeInfo(OperationContext context, RocksDbStore store, string? columnFamilyName)
        {
            return new ColumnSizeInfo(
                Column: columnFamilyName,
                LiveDataSizeBytes: GetLongProperty(context, store, LongProperty.LiveDataSizeBytes, columnFamilyName).TryGetValue(out var dataSize) ? dataSize : default(long?),
                LiveFilesSizeBytes: GetLongProperty(context, store, LongProperty.LiveFilesSizeBytes, columnFamilyName).TryGetValue(out var fileSize) ? fileSize : default(long?));
        }

        /// <nodoc />
        private Result<long> GetLongProperty(OperationContext context, RocksDbStore store, LongProperty property, string? columnFamilyName = null)
        {
            var propertyName = property switch
            {
                LongProperty.LiveFilesSizeBytes => "rocksdb.live-sst-files-size",
                LongProperty.LiveDataSizeBytes => "rocksdb.estimate-live-data-size",
                _ => throw new NotImplementedException($"Unhandled property `{property}` for entity `{columnFamilyName}`"),
            };

            try
            {
                return long.Parse(store.GetProperty(propertyName, columnFamilyName));
            }
            catch (Exception exception)
            {
                Tracer.Warning(context, exception, $"Error retrieving or parsing property '{propertyName}' for column '{columnFamilyName}'.");
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
