// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.SQLite;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    ///     An IMemoizationStore implementation using SQLite.
    /// </summary>
    public class SQLiteMemoizationStore : SQLiteDatabase<SQLiteMemoizationStoreTracer>, IMemoizationStore, IAcquireDirectoryLock
    {
        /// <summary>
        ///     Default name of the database file.
        /// </summary>
        public const string DefaultDatabaseFileName = "Memos.db";

        /// <summary>
        ///     Default synchronization mode.
        /// </summary>
        public const SynchronizationMode DefaultSyncMode = SynchronizationMode.Off;

        private const string Component = nameof(SQLiteMemoizationStore);

        private const string EnvironmentVariablePrefix = "CloudStoreMemo_";
        private const string MemoryCacheSizeEnvironmentVariable = EnvironmentVariablePrefix + "MemoryCacheSize";
        private const string WarmMemoryCacheOnStartupEnvironmentVariable = EnvironmentVariablePrefix + "WarmMemoryCacheOnStartup";
        private const string VacuumOnShutdownEnvironmentVariable = EnvironmentVariablePrefix + "VacuumOnShutdown";
        private const string NoWaitForLruOnShutdownEnvironmentVariable = EnvironmentVariablePrefix + "NoWaitForLruOnShutdown";
        private const string TouchBatchSizeEnvironmentVariableName = EnvironmentVariablePrefix + "TouchBatchSize";
        private const string TouchAfterInactiveMsEnvironmentVariableName = EnvironmentVariablePrefix + "TouchAfterInactiveMs";

        private const int TouchBatchSizeDefault = 100;
        private const int TouchBatchPartitionSize = 100;
        private const int TouchAfterInactiveMsDefault = 20;

        /// <summary>
        ///     Name of counter for current number of content hash lists.
        /// </summary>
        private const string CurrentContentHashListCountName = Component + ".CurrentContentHashListCount";

        private const char HashTypeSeparator = ':';
        private readonly IAbsFileSystem _fileSystem;
        private readonly DirectoryLock _directoryLock;
        private readonly SQLiteMemoizationStoreConfiguration _config;
        private readonly bool _lruEnabled;
        private readonly IClock _clock;
        private readonly CommandPool<int> _purgeCommandPool;
        private readonly CommandPool<ContentHashListWithDeterminism> _getContentHashListCommandPool;
        private readonly CommandPool<IEnumerable<Selector>> _getSelectorsCommandPool;
        private readonly CommandPool<int> _replaceCommandPool;
        private readonly CommandPool<int> _touchPartitionCommandPool;
        private readonly CommandPool<int> _touchSingleCommandPool;
        private readonly long? _memoryCacheSize;
        private readonly bool _warmMemoryCacheOnStartup;
        private readonly bool _vacuumOnShutdown;
        private readonly int _touchBatchSize;
        private readonly int _touchAfterInactiveMs;
        private long _currentRowCount;
        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SQLiteMemoizationStore"/> class.
        /// </summary>
        public SQLiteMemoizationStore
            (
            ILogger logger,
            IClock clock,
            SQLiteMemoizationStoreConfiguration config
            )
            : base(
                  () => new SQLiteMemoizationStoreTracer(logger, Component),
                  config.WithDatabasePath(MakeDatabasePath(config.DatabaseFilePath, DefaultDatabaseFileName))
                  )
        {
            Contract.Requires(config != null);
            Contract.Requires(config.DatabaseFilePath != null);
            Contract.Requires(config.DatabaseFilePath.Parent.Path.Length > 0);
            Contract.Requires(clock != null);

            _fileSystem = new PassThroughFileSystem(logger);
            _directoryLock = new DirectoryLock(DatabaseFilePath.Parent, _fileSystem, TimeSpan.FromSeconds(config.SingleInstanceTimeoutSeconds), Component);
            _clock = clock;
            _config = config;
            _lruEnabled = config.MaxRowCount > 0;
            _purgeCommandPool = CreatePurgeCommandPool();
            _getContentHashListCommandPool = CreateGetContentHashListCommandPool();
            _getSelectorsCommandPool = CreateGetSelectorsCommandPool();
            _replaceCommandPool = CreateReplaceCommandPool();
            _touchPartitionCommandPool = CreateTouchPartitionCommandPool();
            _touchSingleCommandPool = CreateTouchSingleCommandPool();

            // From the SQLite documentation: "If the argument N is negative,
            // then the number of cache pages is adjusted to use approximately abs(N * 1024) bytes of memory."
            var memoryCacheSize = Environment.GetEnvironmentVariable(MemoryCacheSizeEnvironmentVariable);
            if (memoryCacheSize != null)
            {
                long parsedMemoryCacheSize;
                if (!long.TryParse(memoryCacheSize, out parsedMemoryCacheSize))
                {
                    throw new ArgumentException($"Unable to parse the value [{memoryCacheSize}] of experimental environment variable {MemoryCacheSizeEnvironmentVariable}.");
                }

                _memoryCacheSize = parsedMemoryCacheSize;
            }
            else
            {
                _memoryCacheSize = null;
            }

            _warmMemoryCacheOnStartup = Environment.GetEnvironmentVariable(WarmMemoryCacheOnStartupEnvironmentVariable) != null;
            _vacuumOnShutdown = Environment.GetEnvironmentVariable(VacuumOnShutdownEnvironmentVariable) != null;
            _config.WaitForLruOnShutdown &= Environment.GetEnvironmentVariable(NoWaitForLruOnShutdownEnvironmentVariable) == null;
            _touchBatchSize = GetExperimentalSetting(TouchBatchSizeEnvironmentVariableName, TouchBatchSizeDefault);
            _touchAfterInactiveMs = GetExperimentalSetting(TouchAfterInactiveMsEnvironmentVariableName, TouchAfterInactiveMsDefault);
        }

        private static int GetExperimentalSetting(string name, int defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(name);

            if (raw == null)
            {
                return defaultValue;
            }

            uint value;
            if (!uint.TryParse(raw, out value))
            {
                throw new ArgumentException(
                    $"Environment variable {name} has a non-uint value={raw}");
            }

            return (int)value;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _purgeCommandPool.Dispose();
                _getContentHashListCommandPool.Dispose();
                _getSelectorsCommandPool.Dispose();
                _replaceCommandPool.Dispose();
                _touchPartitionCommandPool.Dispose();
                _touchSingleCommandPool.Dispose();
                _directoryLock.Dispose();
                _fileSystem.Dispose();

                _disposed = true;
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> PreStartupAsync(Context context)
        {
            return AcquireDirectoryLockAsync(context);
        }

        /// <inheritdoc />
        public async Task<BoolResult> AcquireDirectoryLockAsync(Context context)
        {
            var aquisitingResult = await _directoryLock.AcquireAsync(context).ConfigureAwait(false);
            if (aquisitingResult.LockAcquired)
            {
                return BoolResult.Success;
            }

            var errorMessage = aquisitingResult.GetErrorMessage(Component);
            return new BoolResult(errorMessage);
        }

        /// <inheritdoc />
        protected override BackgroundWorkerBase CreateBackgroundWorker()
        {
            return new BackgroundWorker(Tracer, this, _config.WaitForLruOnShutdown, _touchBatchSize, _touchAfterInactiveMs);
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyMemoizationSession> CreateReadOnlySession(Context context, string name)
        {
            var session = new ReadOnlySQLiteMemoizationSession(name, this);
            return new CreateSessionResult<IReadOnlyMemoizationSession>(session);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name)
        {
            var session = new SQLiteMemoizationSession(name, this);
            return new CreateSessionResult<IMemoizationSession>(session);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name, IContentSession contentSession)
        {
            var session = new SQLiteMemoizationSession(name, this, contentSession);
            return new CreateSessionResult<IMemoizationSession>(session);
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<MemoizationStoreTracer>.RunAsync(Tracer, new OperationContext(context), () =>
            {
                var counters = new CounterSet();
                counters.Merge(Tracer.GetCounters(), $"{Component}.");
                counters.Merge(Tracer.GetSQLiteDatabaseCounters(), $"{Component}.");

                counters.Add($"{CurrentContentHashListCountName}", _currentRowCount);

                return Task.FromResult(new GetStatsResult(counters));
            });
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> PostInitializeDatabaseAsync(Context context)
        {
            const string initCommand =
                "CREATE TABLE IF NOT EXISTS ContentHashLists" +
                "(" +
                "WeakFingerprint TEXT NOT NULL, " +
                "SelectorContentHash TEXT NOT NULL, " +
                "SelectorOutput BLOB NOT NULL, " +
                "Payload BLOB, " +
                "Determinism BLOB, " +
                "SerializedDeterminism BLOB, " +
                "ContentHashList TEXT NOT NULL, " +
                "FileTimeUtc INTEGER NOT NULL, " +
                "PRIMARY KEY (WeakFingerprint, SelectorContentHash, SelectorOutput)" +
                ");" +
                "CREATE INDEX IF NOT EXISTS StrongFingerprints ON ContentHashLists" +
                " (WeakFingerprint, SelectorContentHash, SelectorOutput);" +
                "CREATE INDEX IF NOT EXISTS ContentHashListLru ON ContentHashLists" +
                " (FileTimeUtc ASC);";

            const string addSerializedDeterminismCommand =
                "ALTER TABLE ContentHashLists ADD COLUMN SerializedDeterminism BLOB;";

            await RunExclusiveAsync(async () =>
            {
                if (_memoryCacheSize.HasValue)
                {
                    context.Debug($"Setting SQLite memory cache_size to {_memoryCacheSize.Value}.");
                    await ExecuteNonQueryAsync("SetMemoryCacheSize", $"PRAGMA cache_size={_memoryCacheSize.Value};");
                }

                await ExecuteNonQueryAsync("CreateTables", initCommand);

                if (!await ColumnExistsAsync("ContentHashLists", "SerializedDeterminism"))
                {
                    await ExecuteNonQueryAsync("AddSerializedDeterminismColumn", addSerializedDeterminismCommand);
                }

                if (_warmMemoryCacheOnStartup)
                {
                    context.Debug("Warming up the SQLite memory cache.");
                    await ExecuteReaderAsync(
                        "WarmMemoryCacheOnStartup",
                        "SELECT * FROM ContentHashLists",
                        reader => ReadResponse.ContinueReading);
                }
            }
            ).ConfigureAwait(false);

            // ReSharper disable once ConvertClosureToMethodGroup
            _currentRowCount = await RunExclusiveAsync(() => GetRowCountAsync()).ConfigureAwait(false);

            Tracer.StartStats(context, _currentRowCount, _lruEnabled);
            Tracer.Debug(context, $"WaitForLruOnShutdown={_config.WaitForLruOnShutdown}");
            Tracer.Debug(context, $"{MemoryCacheSizeEnvironmentVariable}={_memoryCacheSize}");
            Tracer.Debug(context, $"{WarmMemoryCacheOnStartupEnvironmentVariable}={_warmMemoryCacheOnStartup}");
            Tracer.Debug(context, $"{VacuumOnShutdownEnvironmentVariable}={_vacuumOnShutdown}");
            Tracer.Debug(context, $"{TouchBatchSizeEnvironmentVariableName}={_touchBatchSize}");
            Tracer.Debug(context, $"{TouchAfterInactiveMsEnvironmentVariableName}={_touchAfterInactiveMs}");
            Tracer.Debug(context, $"{NoWaitForLruOnShutdownEnvironmentVariable}={Environment.GetEnvironmentVariable(NoWaitForLruOnShutdownEnvironmentVariable)}");

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task PreShutdownAsync(Context context)
        {
            if (_vacuumOnShutdown)
            {
                context.Debug("Vacuuming database.");
                await RunExclusiveAsync(() => ExecuteNonQueryAsync("Vacuum", "VACUUM;"));
            }

            Tracer.EndStats(context, _currentRowCount);
        }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            context.Debug($"{nameof(SQLiteMemoizationStore)}.{nameof(EnumerateStrongFingerprints)}({context.Id})");
            return AsyncEnumerable.CreateEnumerable(
                () =>
                {
                    const long pageLimit = 100;
                    long offset = 0;
                    IEnumerator<StrongFingerprint> strongFingerprints = null;
                    StructResult<StrongFingerprint> error = null;
                    return AsyncEnumerable.CreateEnumerator(
                        async cancellationToken =>
                        {
                            try
                            {
                                if (error != null)
                                {
                                    return false;
                                }

                                if (strongFingerprints == null || !strongFingerprints.MoveNext())
                                {
                                    // ReSharper disable once GenericEnumeratorNotDisposed
                                    strongFingerprints = (await EnumerateStrongFingerprintsAsync(pageLimit, offset)).GetEnumerator();
                                    if (!strongFingerprints.MoveNext())
                                    {
                                        return false;
                                    }
                                }

                                offset++;
                                return true;
                            }
                            catch (Exception e)
                            {
                                error = new StructResult<StrongFingerprint>(e);
                                return true;
                            }
                        },
                        () => error ?? new StructResult<StrongFingerprint>(strongFingerprints.Current),
                        () => { strongFingerprints?.Dispose(); });
                });
        }

        private async Task<IEnumerable<StrongFingerprint>> EnumerateStrongFingerprintsAsync(long limit, long offset)
        {
            var strongFingerprints = new List<StrongFingerprint>();
            await ExecuteReaderAsync(
                "Enumerate",
                "SELECT WeakFingerprint, SelectorContentHash, SelectorOutput FROM ContentHashLists LIMIT @limit OFFSET @offset;",
                reader =>
                {
                    var weakFingerprint = Deserialize((string)reader["WeakFingerprint"]);
                    var contentHash = new ContentHash((string)reader["SelectorContentHash"]);
                    var selector = new Selector(contentHash, (byte[])reader["SelectorOutput"]);
                    strongFingerprints.Add(new StrongFingerprint(weakFingerprint, selector));

                    return ReadResponse.ContinueReading;
                },
                new SQLiteParameter("@limit", limit),
                new SQLiteParameter("@offset", offset)).ConfigureAwait(false);

            return strongFingerprints;
        }

        /// <summary>
        ///     Enumerate known selectors for a given weak fingerprint.
        /// </summary>
        internal Async::System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts)
        {
            return AsyncEnumerableExtensions.CreateSingleProducerTaskAsyncEnumerable(() => getSelectorsCore());

            async Task<IEnumerable<GetSelectorResult>> getSelectorsCore()
            {
                var selectors = await GetSelectorsCoreAsync(context, weakFingerprint);
                if (!selectors)
                {
                    return new GetSelectorResult[] { new GetSelectorResult(selectors) };
                }

                return selectors.Value.Select(s => new GetSelectorResult(s));
            }
        }

        /// <summary>
        ///     Enumerate known selectors for a given weak fingerprint.
        /// </summary>
        internal async Task<Result<Selector[]>> GetSelectorsCoreAsync(
            Context context,
            Fingerprint weakFingerprint)
        {
            var stopwatch = new Stopwatch();
            try
            {
                Tracer.GetSelectorsStart(context, weakFingerprint);
                stopwatch.Start();

                var fingerprint = SerializeWithHashType(weakFingerprint);
                var getSelectorsResult = (await _getSelectorsCommandPool.RunAsync(new SQLiteParameter("@weakFingerprint", fingerprint))).ToArray();

                Tracer.GetSelectorsCount(context, weakFingerprint, getSelectorsResult.Length);
                return getSelectorsResult;
            }
            catch (Exception exception)
            {
                Tracer.Debug(context, $"{Component}.GetSelectors() error=[{exception}]");
                return Result.FromException<Selector[]>(exception);
            }
            finally
            {
                stopwatch.Stop();
                Tracer.GetSelectorsStop(context, stopwatch.Elapsed);
            }
        }

        /// <summary>
        ///     Load a ContentHashList.
        /// </summary>
        internal Task<GetContentHashListResult> GetContentHashListAsync(
            Context context, StrongFingerprint strongFingerprint, CancellationToken cts)
        {
            return GetContentHashListCall.RunAsync(Tracer, context, strongFingerprint, async () =>
            {
                ContentHashListWithDeterminism contentHashListWithDeterminism =
                    await RunConcurrentAsync(() => GetContentHashListAsync(strongFingerprint)).ConfigureAwait(false);
                UpdateLruOnGet(strongFingerprint);
                return new GetContentHashListResult(contentHashListWithDeterminism);
            });
        }

        /// <summary>
        ///     Force LRU.
        /// </summary>
        public async Task PurgeAsync(Context context, bool strict = true)
        {
            var margin = strict ? 0 : _touchBatchSize;
            if (!_lruEnabled || _currentRowCount <= _config.MaxRowCount + margin)
            {
                return;
            }

            await RunExclusiveAsync(async () =>
            {
                var stopwatch = Stopwatch.StartNew();
                _currentRowCount = await GetRowCountAsync();
                var rowsToPurge = _currentRowCount - _config.MaxRowCount;
                Tracer.PurgeStart(context, (int)rowsToPurge);

                if (rowsToPurge > 0)
                {
                    await _purgeCommandPool.RunAsync(new SQLiteParameter("@rowsToPurge", rowsToPurge));
                }

                _currentRowCount = await GetRowCountAsync();
                Tracer.PurgeStop(context, (int)_currentRowCount, stopwatch.Elapsed);
            }).ConfigureAwait(false);
        }

        private static CacheDeterminism ReadDeterminism(byte[] determinismBytes, byte[] oldDeterminismBytes)
        {
            var determinism = CacheDeterminism.Deserialize(determinismBytes);
            var oldDeterminismGuid = new Guid(oldDeterminismBytes);

            // If the old guid was ToolDeterministic, we need to keep it that way.
            if (oldDeterminismGuid == CacheDeterminism.Tool.Guid)
            {
                return CacheDeterminism.Tool;
            }

            // If the old guid was SinglePhaseNonDeterministic, we need to keep it that way.
            if (oldDeterminismGuid == CacheDeterminism.SinglePhaseNonDeterministic.Guid)
            {
                return CacheDeterminism.SinglePhaseNonDeterministic;
            }

            // The newer code always sets the old "Determinism" column to the same determinism guid when it writes a new value,
            // so if we read here that the determinism from the old column is different from
            // the new "SerializedDeterminism" column's guid then the ContentHashList in this row was inserted by the
            // old code and the new column's Determinism value is no longer valid. In that case, we return None.
            return oldDeterminismGuid == determinism.EffectiveGuid
                ? determinism
                : CacheDeterminism.None;
        }

        private Task<ContentHashListWithDeterminism> GetContentHashListAsync(StrongFingerprint strongFingerprint)
        {
            return _getContentHashListCommandPool.RunAsync(
                new SQLiteParameter("@weakFingerprint", SerializeWithHashType(strongFingerprint.WeakFingerprint)),
                new SQLiteParameter("@selectorContentHash", strongFingerprint.Selector.ContentHash.SerializeReverse()),
                new SQLiteParameter("@selectorOutput", strongFingerprint.Selector.Output));
        }

        /// <summary>
        ///     Store a ContentHashList
        /// </summary>
        internal Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            IContentSession contentSession,
            CancellationToken cts)
        {
            return AddOrGetContentHashListCall.RunAsync(Tracer, new OperationContext(context, cts), strongFingerprint, async () =>
            {
                const int maxAttempts = 5;
                int attemptCount = 0;
                while (attemptCount++ <= maxAttempts)
                {
                    var contentHashList = contentHashListWithDeterminism.ContentHashList;
                    var determinism = contentHashListWithDeterminism.Determinism;

                    // Load old value
                    var oldContentHashListWithDeterminism = await GetContentHashListAsync(strongFingerprint);
                    var oldContentHashList = oldContentHashListWithDeterminism.ContentHashList;
                    var oldDeterminism = oldContentHashListWithDeterminism.Determinism;

                    // Make sure we're not mixing SinglePhaseNonDeterminism records
                    if (oldContentHashList != null &&
                        (oldDeterminism.IsSinglePhaseNonDeterministic != determinism.IsSinglePhaseNonDeterministic))
                    {
                        return AddOrGetContentHashListResult.SinglePhaseMixingError;
                    }

                    // Match found.
                    // Replace if incoming has better determinism or some content for the existing entry is missing.
                    if (oldContentHashList == null || oldDeterminism.ShouldBeReplacedWith(determinism) ||
                        !(await contentSession.EnsureContentIsAvailableAsync(context, oldContentHashList, cts).ConfigureAwait(false)))
                    {
                        AddOrGetContentHashListResult contentHashListResult = await RunExclusiveAsync(
                            async () =>
                            {
                                // double check again.
                                var contentHashListInStore = await GetContentHashListAsync(strongFingerprint);

                                if (contentHashListInStore != oldContentHashListWithDeterminism)
                                {
                                    return new AddOrGetContentHashListResult(contentHashListInStore);
                                }

                                var fileTimeUtc = _clock.UtcNow.ToFileTimeUtc();

                                await _replaceCommandPool.RunAsync(
                                    new SQLiteParameter("@weakFingerprint", SerializeWithHashType(strongFingerprint.WeakFingerprint)),
                                    new SQLiteParameter("@selectorContentHash", strongFingerprint.Selector.ContentHash.SerializeReverse()),
                                    new SQLiteParameter("@selectorOutput", strongFingerprint.Selector.Output),
                                    new SQLiteParameter("@fileTimeUtc", fileTimeUtc),
                                    new SQLiteParameter("@payload", contentHashList.Payload?.ToArray()),
                                    new SQLiteParameter("@determinism", determinism.EffectiveGuid.ToByteArray()),
                                    new SQLiteParameter("@serializedDeterminism", determinism.Serialize()),
                                    new SQLiteParameter("@contentHashList", contentHashList.Serialize()));

                                // Bump count if this is a new entry.
                                if (oldContentHashList == null)
                                {
                                    IncrementCountOnAdd();
                                }

                                // Accept the value
                                return new AddOrGetContentHashListResult(
                                    new ContentHashListWithDeterminism(null, determinism));
                            });

                        // our add lost - need to retry.
                        if (contentHashListResult.ContentHashListWithDeterminism.ContentHashList != null)
                        {
                            continue;
                        }

                        if (contentHashListResult.ContentHashListWithDeterminism.ContentHashList == null)
                        {
                            return contentHashListResult;
                        }
                    }

                    if (oldContentHashList != null && oldContentHashList.Equals(contentHashList))
                    {
                        return new AddOrGetContentHashListResult(
                            new ContentHashListWithDeterminism(null, oldDeterminism));
                    }

                    // If we didn't accept a deterministic tool's data, then we're in an inconsistent state
                    if (determinism.IsDeterministicTool)
                    {
                        return new AddOrGetContentHashListResult(
                            AddOrGetContentHashListResult.ResultCode.InvalidToolDeterminismError,
                            oldContentHashListWithDeterminism);
                    }

                    // If we did not accept the given value, return the value in the cache
                    return new AddOrGetContentHashListResult(oldContentHashListWithDeterminism);
                }

                return new AddOrGetContentHashListResult("Hit too many races attempting to add content hash list into the cache");
            });
        }

        private CommandPool<int> CreatePurgeCommandPool()
        {
            return CreateNonQueryCommandPool(
                "DELETE FROM ContentHashLists WHERE ROWID IN (SELECT ROWID FROM ContentHashLists ORDER BY fileTimeUtc ASC LIMIT @rowsToPurge);");
        }

        private CommandPool<ContentHashListWithDeterminism> CreateGetContentHashListCommandPool()
        {
            return new CommandPool<ContentHashListWithDeterminism>(
                Connection,
#pragma warning disable SA1118 // Parameter must not span multiple lines
                "SELECT Payload, Determinism, SerializedDeterminism, ContentHashList FROM ContentHashLists" +
                "  WHERE WeakFingerprint=@weakFingerprint" +
                "    AND SelectorContentHash=@selectorContentHash" +
                "    AND SelectorOutput=@selectorOutput",
#pragma warning restore SA1118 // Parameter must not span multiple lines
                async command =>
                {
                    await Task.Yield();
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        if (await reader.ReadAsync())
                        {
                            var payloadObject = reader["Payload"];
                            var contentHashList = ContentHashList.Deserialize(
                                (string)reader["ContentHashList"], payloadObject == DBNull.Value ? null : (byte[])payloadObject);

                            var serializedDeterminismObject = reader["SerializedDeterminism"];
                            var determinism = ReadDeterminism(
                                serializedDeterminismObject == DBNull.Value ? null : (byte[])serializedDeterminismObject,
                                (byte[])reader["Determinism"]);

                            Contract.Assert(!reader.Read());
                            return new ContentHashListWithDeterminism(contentHashList, determinism);
                        }

                        return new ContentHashListWithDeterminism(null, CacheDeterminism.None);
                    }
                }
                );
        }

        private CommandPool<IEnumerable<Selector>> CreateGetSelectorsCommandPool()
        {
            return new CommandPool<IEnumerable<Selector>>(
                Connection,
                "SELECT SelectorContentHash, SelectorOutput FROM ContentHashLists WHERE WeakFingerprint=@weakFingerprint ORDER BY FileTimeUtc DESC",
                async command =>
                {
                    await Task.Yield();
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        var selectors = new List<Selector>();

                        while (await reader.ReadAsync())
                        {
                            var contentHashHex = (string)reader["SelectorContentHash"];
                            var contentHash = new ContentHash(contentHashHex);
                            var output = (byte[])reader["SelectorOutput"];
                            selectors.Add(new Selector(contentHash, output));
                        }

                        return selectors;
                    }
                }
                );
        }

        private CommandPool<int> CreateReplaceCommandPool()
        {
            return CreateNonQueryCommandPool(
#pragma warning disable SA1118 // Parameter must not span multiple lines
                "INSERT OR REPLACE INTO ContentHashLists" +
                "  (WeakFingerprint, SelectorContentHash, SelectorOutput, FileTimeUtc, Payload, Determinism, SerializedDeterminism, ContentHashList)" +
                "  VALUES (@weakFingerprint, @selectorContentHash, @selectorOutput, @fileTimeUtc, @payload, @determinism, @serializedDeterminism, @contentHashList)"
#pragma warning restore SA1118 // Parameter must not span multiple lines
                );
        }

        private CommandPool<int> CreateTouchPartitionCommandPool()
        {
            const int maxSqlStatementLength = 256;
            var sb = new StringBuilder(TouchBatchPartitionSize * maxSqlStatementLength);

            for (var i = 0; i < TouchBatchPartitionSize; i++)
            {
                sb.AppendLine(
                    $"UPDATE ContentHashLists SET fileTimeUtc=@fileTimeUtc{i}" +
                    $" WHERE  WeakFingerprint=@weakFingerprint{i}" +
                    $" AND SelectorContentHash=@selectorContentHash{i}" +
                    $" AND SelectorOutput=@selectorOutput{i};");
            }

            return CreateNonQueryCommandPool(sb.ToString());
        }

        private CommandPool<int> CreateTouchSingleCommandPool()
        {
            return CreateNonQueryCommandPool(
                "UPDATE ContentHashLists SET fileTimeUtc=@fileTimeUtc" +
                " WHERE  WeakFingerprint=@weakFingerprint" +
                " AND SelectorContentHash=@selectorContentHash" +
                " AND SelectorOutput=@selectorOutput;"
            );
        }

        /// <summary>
        ///     A message identifying a row that needs its timestamp updated for LRU.
        /// </summary>
        private class TouchMessage : RequestMessage
        {
            /// <summary>
            ///     Initializes a new instance of the <see cref="TouchMessage"/> class.
            /// </summary>
            public TouchMessage(StrongFingerprint fingerprint, long fileTimeUtc)
            {
                StrongFingerprint = fingerprint;
                FileTimeUtc = fileTimeUtc;
            }

            /// <summary>
            ///     Gets the strong fingerprint identifies the row we need to touch.
            /// </summary>
            public StrongFingerprint StrongFingerprint { get; }

            /// <summary>
            ///     Gets the new fileTimeUtc for the row.
            /// </summary>
            public long FileTimeUtc { get; }
        }

        private class PurgeMessage : RequestMessage
        {
            public static readonly PurgeMessage Instance = new PurgeMessage();
        }

        /// <summary>
        ///     Update LRU on adding a new ContentHashList.
        /// </summary>
        private void IncrementCountOnAdd()
        {
            if (Interlocked.Increment(ref _currentRowCount) >= (_config.MaxRowCount + _touchBatchSize))
            {
                if (_lruEnabled)
                {
                    Messages.Add(PurgeMessage.Instance);
                }
            }
        }

        private void UpdateLruOnGet(StrongFingerprint strongFingerprint)
        {
            if (_lruEnabled)
            {
                Messages.Add(new TouchMessage(strongFingerprint, _clock.UtcNow.ToFileTimeUtc()));
            }
        }

        private async Task<long> GetRowCountAsync()
        {
            return (long)(await ExecuteScalarAsync("GetRowCount", "SELECT COUNT(*) FROM ContentHashLists"));
        }

        private async Task TouchAsync(Context context, List<TouchMessage> list)
        {
            var stopwatch = Stopwatch.StartNew();
            Tracer.TouchStart(context, list.Count);

            while (list.Count > 0)
            {
                List<TouchMessage> messages = list.Take(TouchBatchPartitionSize).ToList();
                list.RemoveRange(0, messages.Count);

                if (messages.Count == TouchBatchPartitionSize)
                {
                    // We have a full partition and can use the partition prepared statement.
                    var parameters = new SQLiteParameter[TouchBatchPartitionSize * 4];
                    for (int i = 0, j = 0; i < TouchBatchPartitionSize; i++)
                    {
                        var message = messages[i];

                        parameters[j++] = new SQLiteParameter(
                            "@fileTimeUtc" + i, message.FileTimeUtc);
                        parameters[j++] = new SQLiteParameter(
                            "@weakFingerprint" + i, SerializeWithHashType(message.StrongFingerprint.WeakFingerprint));
                        parameters[j++] = new SQLiteParameter(
                            "@selectorContentHash" + i, message.StrongFingerprint.Selector.ContentHash.SerializeReverse());
                        parameters[j++] = new SQLiteParameter(
                            "@selectorOutput" + i, message.StrongFingerprint.Selector.Output);
                    }

                    await RunExclusiveAsync(async () => await _touchPartitionCommandPool.RunAsync(parameters)).ConfigureAwait(false);
                }
                else
                {
                    var parameters = new SQLiteParameter[4];
                    foreach (var message in messages)
                    {
                        parameters[0] = new SQLiteParameter(
                            "@fileTimeUtc", message.FileTimeUtc);
                        parameters[1] = new SQLiteParameter(
                            "@weakFingerprint", SerializeWithHashType(message.StrongFingerprint.WeakFingerprint));
                        parameters[2] = new SQLiteParameter(
                            "@selectorContentHash", message.StrongFingerprint.Selector.ContentHash.SerializeReverse());
                        parameters[3] = new SQLiteParameter(
                            "@selectorOutput", message.StrongFingerprint.Selector.Output);

                        await RunExclusiveAsync(async () => await _touchSingleCommandPool.RunAsync(parameters)).ConfigureAwait(false);
                    }
                }
            }

            Tracer.TouchStop(context, stopwatch.Elapsed);
        }

        private class BackgroundWorker : BackgroundWorkerBase
        {
            private readonly SQLiteMemoizationStore _store;
            private readonly bool _waitForLruOnShutdown;
            private readonly int _touchBatchSize;
            private readonly int _touchAfterInactiveMs;
            private readonly Stopwatch _stopwatch;
            private List<TouchMessage> _touchList;
            private bool _purge;

            public BackgroundWorker
                (
                SQLiteMemoizationStoreTracer tracer,
                SQLiteMemoizationStore store,
                bool waitForLruOnShutdown,
                int touchBatchSize,
                int touchAfterInactiveMs
                )
                : base(tracer)
            {
                _store = store;
                _waitForLruOnShutdown = waitForLruOnShutdown;
                _touchBatchSize = touchBatchSize;
                _touchAfterInactiveMs = touchAfterInactiveMs;
                _stopwatch = Stopwatch.StartNew();
                _touchList = new List<TouchMessage>(_touchBatchSize);
            }

            /// <inheritdoc />
            public override void ProcessBackgroundMessage(Context context, RequestMessage message)
            {
                if (message is TouchMessage item)
                {
                    _touchList.Add(item);
                    _stopwatch.Restart();
                }
                else if (message is PurgeMessage)
                {
                    _purge = true;
                }
                else
                {
                    base.ProcessBackgroundMessage(context, message);
                }
            }

            /// <inheritdoc />
            public override bool DoBackgroundWork(Context context, bool shutdown, bool sync)
            {
                var work = false;
                var force = _purge || shutdown || sync;
                var canWriteTouchBatch = _touchAfterInactiveMs <= 0 || _stopwatch.ElapsedMilliseconds > _touchAfterInactiveMs;

                if ((_touchList.Count > 0 && force) || (_touchList.Count > _touchBatchSize && canWriteTouchBatch))
                {
                    if (shutdown && !_waitForLruOnShutdown)
                    {
                        context.Debug($"Dumping {_touchList.Count} LRU touches on shutdown since !waitForLruOnShutdown");
                        _touchList = null;
                    }
                    else
                    {
                        List<TouchMessage> list = _touchList;
                        _touchList = new List<TouchMessage>(_touchBatchSize);
                        _store.TouchAsync(context, list).Wait();
                        _stopwatch.Restart();
                        work = true;
                    }
                }

                if (force)
                {
                    _store.PurgeAsync(context, strict: shutdown || sync).Wait();
                    _purge = false;
                }

                return work;
            }
        }

        /// <summary>
        ///     Serialize <see cref="Fingerprint"/>
        /// </summary>
        /// <param name="fingerprint">Weak fingerprint</param>
        private static string SerializeWithHashType(Fingerprint fingerprint)
        {
            // We now do not care about the hash type. To maintain compatibility with
            // old users (only BuildXL), hard code the type so we do not break old code.
            return fingerprint.ToHex() + HashTypeSeparator + HashType.SHA1;
        }

        /// <summary>
        ///     Deserialize <see cref="Fingerprint"/>
        /// </summary>
        private static Fingerprint Deserialize(string value)
        {
            var split = value.Split(HashTypeSeparator);
            Contract.Assert(split.Length == 1 || split.Length == 2);
            return new Fingerprint(split[0]);
        }
    }
}
