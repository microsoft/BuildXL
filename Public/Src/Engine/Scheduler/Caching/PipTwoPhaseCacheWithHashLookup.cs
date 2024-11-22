// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Native.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.Cache
{
    /// <summary>
    /// This class extends the functionality of PipTwoPhaseCache by incorporating hash-to-hash lookup capabilities.
    /// It also provides utility functions for historic metadata cache management.
    /// It does not use historic metadata for pip metadata querying except for performing hash-to-hash lookups.
    /// It handles cache entry publication and retrieval through its base class PipTwoPhaseCache.
    /// This class acts as the base class for HistoricMetaDataCache, which extends its functioanlities to include historic metadata querying capabilities.
    /// </summary>
    public class PipTwoPhaseCacheWithHashLookup : PipTwoPhaseCache
    {
        /// <summary>
        /// The version for format of <see cref="PipTwoPhaseCacheWithHashLookup"/>
        /// </summary>
        public const int FormatVersion = 27;

        /// <summary>
        /// Time-to-live for computed content hashes.
        /// </summary>
        public const int ComputedContentHashTimeToLive = 10;

        /// <summary>
        /// The location of the data files for the cache
        /// </summary>
        public readonly string StoreLocation;

        /// <summary>
        /// The directory for log files for the cache
        /// </summary>
        public readonly string LogDirectoryLocation;

        /// <summary>
        /// The age of the historic metadata cache
        /// </summary>
        public int Age { get; protected set; }

        /// <summary>
        /// Whether the historic metadata cache contains valid information.
        /// This is false if the historic metadata cache version is outdated,
        /// the backing store fails to open, or a read/write operation fails during the lifetime of the store.
        /// </summary>
        public bool Valid { get; protected set; }

        /// <summary>
        /// Whether <see cref="CloseAsync"/> has been called.
        /// </summary>
        public bool Closed
        {
            get
            {
                return Volatile.Read(ref m_closed);
            }
            private set
            {
                Volatile.Write(ref m_closed, value);
            }
        }

        private bool m_closed = false;
        private volatile bool m_closing = false;

        /// <summary>
        /// This task is used to ensures that the historic metadata cache is fully loaded before queries to metadata are allowed.
        /// </summary>
        /// <returns>
        /// Return false if cancellation has been requested.
        /// Throw an exception if HistoricMetadataCache is closed and build is not cancelled.
        /// Otherwise return true after cache is fully loaded
        /// </returns>
        /// <remarks>
        /// This task is is often synchronously awaited, so it's important that it is accessed from this property only to not allow
        /// it to await new tasks that will be processed by the same thread pool for tasks that the synchronous wait itself is blocking 
        /// (this may be intermittent depending on how many threads are processing tasks on the task context and how many of
        /// those tasks synchronously wait on this method - which in the past has happened). 
        /// We thus use a property with a getter (which can't be async) rather than a method to construct this task.
        /// For a specific example see https://dev.azure.com/mseng/1ES/_workitems/edit/2229869
        /// </remarks>
        /// <exception cref="BuildXLException"></exception>
        protected Task<bool> LoadTask
        {
            get
            {
                if (Context.CancellationToken.IsCancellationRequested || SchedulerCancellationToken.IsCancellationRequested)
                {
                    return Task.FromResult(false);
                }

                if (Closed)
                {
                    throw new BuildXLException("Attempts to load the HistoricMetadataCache should not be made after Close is called.");
                }

                Volatile.Write(ref LoadStarted, true);

                return m_lazyLoadTask.Value ?? Task.FromResult(true);
            }
        }

        /// <summary>
        /// An asynchronous task responsible for initializing and loading the cache's data upon startup.
        /// </summary>
        private readonly Lazy<Task<bool>> m_lazyLoadTask;

        /// <summary>
        /// Whether the async initialization of <see cref="LoadTask"/> has started.
        /// </summary>
        protected bool LoadStarted = false;

        private readonly TaskSourceSlim<Unit> m_prepareCompletion;

        /// <summary>
        /// Provides access to the key-value store accessor, returning null if the store is disabled or failed to initialize.
        /// </summary>
        protected KeyValueStoreAccessor StoreAccessor
        {
            get
            {
                // If the store accessor failed during setup or has become disabled, return null
                if (m_storeAccessor.Value == null || m_storeAccessor.Value.Disabled)
                {
                    return null;
                }

                return m_storeAccessor.Value;
            }
        }

        private readonly Lazy<KeyValueStoreAccessor> m_storeAccessor;
        private int m_activeContentHashMappingColumnIndex;

        private readonly ObjectPool<HashingHelper> m_hashingPool;

        internal static readonly ByteArrayPool ContentMappingKeyArrayPool = new(ContentHash.SerializedLength + 1);

        /// <summary>
        /// Given the age of the cache, computes the index of the build manifest column that
        /// should be used for storing new build manifest hashes.        /// 
        /// Essentially, we alternating between two columns every <see cref="ComputedContentHashTimeToLive"/> number of builds.
        /// </summary>
        private static int ComputeContentHashMappingActiveColumnIndex(int age) => age >= 0 ? (age / ComputedContentHashTimeToLive) % 2 : 0;

        /// <nodoc/>
        public PipTwoPhaseCacheWithHashLookup(
            LoggingContext loggingContext,
            EngineCache cache,
            PipExecutionContext context,
            PathExpander pathExpander,
            AbsolutePath storeLocation,
            Func<PipTwoPhaseCacheWithHashLookup, Task> prepareAsync = null,
            AbsolutePath? logDirectoryLocation = null)
            : base(loggingContext, cache, context, pathExpander)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(cache != null);
            Contract.Requires(context != null);
            Contract.Requires(storeLocation.IsValid);

            StoreLocation = storeLocation.ToString(context.PathTable);
            LogDirectoryLocation = logDirectoryLocation.HasValue && logDirectoryLocation.Value.IsValid ? logDirectoryLocation.Value.ToString(context.PathTable) : null;
            m_storeAccessor = new Lazy<KeyValueStoreAccessor>(OpenStore);
            m_hashingPool = new ObjectPool<HashingHelper>(() => new HashingHelper(context.PathTable, recordFingerprintString: false), h => h.Dispose());
            Valid = false;

            Age = 0;
            m_activeContentHashMappingColumnIndex = -1;

            m_prepareCompletion = TaskSourceSlim.Create<Unit>();
            m_lazyLoadTask = new Lazy<Task<bool>>(() => ExecuteLoadTask(prepareAsync));
        }

        /// <summary>
        /// Executes initialization tasks for the cache, updates and manages the cache's age. 
        /// Recalculates the ContentHashMappingColumnIndex, and prepares the database for new data entries. 
        /// </summary>
        protected virtual async Task<bool> ExecuteLoadTask(Func<PipTwoPhaseCacheWithHashLookup, Task> prepareAsync)
        {
            // Unblock the caller
            await Task.Yield();

            try
            {
                var task = prepareAsync?.Invoke(this) ?? Unit.VoidTask;
                await task;

                m_prepareCompletion.TrySetResult(Unit.Void);

                StoreAccessor?.Use(database =>
                {
                    Age = GetAge(database) + 1;
                    SetAge(database, Age);
                    m_activeContentHashMappingColumnIndex = ComputeContentHashMappingActiveColumnIndex(Age);
                    Counters.AddToCounter(PipCachingCounter.HistoricMetadataLoadedAge, Age);
                    PrepareBuildManifestColumn(database);
                });
            }
            catch (Exception ex)
            {
                Logger.Log.HistoricMetadataCacheLoadFailed(LoggingContext, ex.ToString());
                m_prepareCompletion.TrySetResult(Unit.Void);
                Valid = false;
            }

            return true;
        }

        /// <summary>
        /// Starting the loading task for the historic metadata cache
        /// </summary>
        public override void StartLoading(bool waitForCompletion)
        {
            var loadingTask = LoadTask;
            if (waitForCompletion)
            {
                loadingTask.GetAwaiter().GetResult();
            }
        }

        /// <inheritdoc />
        public override async Task CloseAsync()
        {
            // Allow Close to be called multiple times with no side effects
            if (Closed || m_closing)
            {
                return;
            }

            m_closing = true;
            Logger.Log.HistoricMetadataCacheCloseCalled(LoggingContext);

            await DoCloseAsync();
            await base.CloseAsync();

            Closed = true;
        }

        /// <summary>
        /// Closes the cache after ensuring all load operations are complete, and optionally copies the log file to a specified directory.
        /// </summary>
        protected virtual async Task DoCloseAsync()
        {
            // Only save info if historic metadata cache was accessed
            if (Volatile.Read(ref LoadStarted))
            {
                // If a load was started, wait for full completion of the load
                // Otherwise, close and the load initialization can run concurrently and cause race conditions
                await LoadTask;

                StoreAccessor?.Dispose();

                // Once the store accessor has been closed, the log can be safely copied
                if (LogDirectoryLocation != null)
                {
                    try
                    {
                        if (!FileUtilities.Exists(LogDirectoryLocation))
                        {
                            FileUtilities.CreateDirectory(LogDirectoryLocation);
                        }

                        await FileUtilities.CopyFileAsync(
                            Path.Combine(StoreLocation, KeyValueStoreAccessor.LogFileName),
                            Path.Combine(LogDirectoryLocation, KeyValueStoreAccessor.LogFileName));
                    }
                    catch (Exception e)
                    {
                        //  It's not super important that the log file is copied, so just log a message
                        Logger.Log.HistoricMetadataCacheTrace(LoggingContext, "Failed to copy historic metadata cache log file to BuildXL logs folder:" + e.GetLogEventMessage());
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override void TryStoreRemappedContentHash(ContentHash contentHash, ContentHash remappedContentHash)
        {
            if(!LoadTask.GetAwaiter().GetResult())
            {
                return;
            }

            StoreAccessor?.Use(database =>
            {
                using var key = GetRemappingContentHashKey(contentHash, remappedContentHash.HashType);
                if (!database.TryGetValue(key.Value, out var existingValue, StoreColumnNames.ContentHashMappingColumn[m_activeContentHashMappingColumnIndex]))
                {
                    using var value = remappedContentHash.ToPooledByteArray();
                    // While we are checking both columns, we are storing only into the active one.
                    database.Put(key.Value, value.Value, StoreColumnNames.ContentHashMappingColumn[m_activeContentHashMappingColumnIndex]);
                }
            });
        }

        /// <summary>
        /// Get key to store mapped content hash
        /// </summary>
        private ByteArrayPool.PoolHandle GetRemappingContentHashKey(ContentHash contentHash, HashType targetHashType)
        {
            var handle = ContentMappingKeyArrayPool.Get();
            byte[] buffer = handle.Value;

            // Store the hash type in the first byte
            unchecked
            {
                buffer[0] = (byte)targetHashType;
            }

            contentHash.Serialize(buffer, offset: 1);
            return handle;
        }

        /// <inheritdoc/>
        public override ContentHash TryGetMappedContentHash(ContentHash contentHash, HashType hashType)
        {
            if (!LoadTask.GetAwaiter().GetResult())
            {
                return new ContentHash(HashType.Unknown);
            }

            Contract.Assert(contentHash.IsValid, "ContentHash must be valid");

            ContentHash foundHash = default;
            StoreAccessor?.Use(database =>
            {
                Contract.Assert(m_activeContentHashMappingColumnIndex >= 0, "Build manifest column is not initialized");
                using var key = GetRemappingContentHashKey(contentHash, hashType);
                Contract.Assert(key.Value != null, "ByteArray was null for ContentHash");

                // First, check the active column. If the hash is not there, check the other column.
                if (database.TryGetValue(key.Value, out var value, StoreColumnNames.ContentHashMappingColumn[m_activeContentHashMappingColumnIndex]))
                {
                    foundHash = new ContentHash(value);
                }
                else if (database.TryGetValue(key.Value, out value, StoreColumnNames.ContentHashMappingColumn[(m_activeContentHashMappingColumnIndex + 1) % 2]))
                {
                    // need to update the active column
                    database.Put(key.Value, value, StoreColumnNames.ContentHashMappingColumn[m_activeContentHashMappingColumnIndex]);
                    foundHash = new ContentHash(value);
                }
            });

            if (foundHash.IsValid && foundHash.HashType != hashType)
            {
                Contract.Assert(false, $"Mismatched hash retrieval from historic metadata cache. Req: {hashType}. Type: {foundHash.HashType}");
            }

            return foundHash;
        }

        private int GetAge(RocksDbStore store)
        {
            bool ageFound = store.TryGetValue(nameof(StoreKeyNames.Age), out var ageString);
            if (!ageFound || !int.TryParse(ageString, out var age))
            {
                SetAge(store, 0);
                return 0;
            }

            return age;
        }

        private void SetAge(RocksDbStore store, int age)
        {
            store.Put(nameof(StoreKeyNames.Age), age.ToString());
        }

        private void PrepareBuildManifestColumn(RocksDbStore database)
        {
            if (m_activeContentHashMappingColumnIndex != ComputeContentHashMappingActiveColumnIndex(Age - 1))
            {
                // The active column has changed, need to clean it before its first use.
                database.DropColumnFamily(StoreColumnNames.ContentHashMappingColumn[m_activeContentHashMappingColumnIndex]);
                database.CreateColumnFamily(StoreColumnNames.ContentHashMappingColumn[m_activeContentHashMappingColumnIndex]);
            }
        }

        private KeyValueStoreAccessor OpenStore()
        {
            Contract.Assert(StoreColumnNames.ContentHashMappingColumn.Length == 2);
            Contract.Assert(m_prepareCompletion.Task.IsCompleted, "Attempted to open the store before the loading has finished.");

            var keyTrackedColumns = new string[]
            {
                StoreColumnNames.Content
            };

            var possibleAccessor = KeyValueStoreAccessor.OpenWithVersioning(
                StoreLocation,
                FormatVersion,
                additionalColumns: StoreColumnNames.ContentHashMappingColumn,
                additionalKeyTrackedColumns: keyTrackedColumns,
                failureHandler: (f) => HandleStoreFailure(f.Failure),
                onFailureDeleteExistingStoreAndRetry: true,
                onStoreReset: failure =>
                {
                    Logger.Log.HistoricMetadataCacheCreateFailed(LoggingContext, failure.DescribeIncludingInnerFailures(), willResetAndRetry: true);
                });

            if (possibleAccessor.Succeeded)
            {
                Valid = true;
                return possibleAccessor.Result;
            }

            // If we fail when creating a new store, there will be no historic metadata cache, so log a warning
            Logger.Log.HistoricMetadataCacheCreateFailed(LoggingContext, possibleAccessor.Failure.DescribeIncludingInnerFailures(), willResetAndRetry: false);
            return null;
        }

        private void HandleStoreFailure(Failure failure)
        {
            // Conservatively assume all store failures indicate corrupted data and should not be saved
            Valid = false;
            KeyValueStoreUtilities.CheckAndLogRocksDbException(failure, LoggingContext);
            // If the store fails, future performance can be impacted, so log a warning
            Logger.Log.HistoricMetadataCacheOperationFailed(LoggingContext, failure.DescribeIncludingInnerFailures());

            // Note: All operations using the store are no-ops after the first failure,
            // but if there are concurrent operations that fail, then this may log multiple times.
        }

        /// <summary>
        /// Contains the names of the columns used in the key-value store for the historic metadata cache.
        /// </summary>
        protected readonly struct StoreColumnNames
        {
            /// <summary>
            /// The content field containing the actual payload. (This stores the
            /// content blobs for metadata and pathsets)
            /// </summary>
            public static string Content = "Content";

            /// <summary>
            /// We use two columns for storing (contentHash, targetHashType) -> contentHash map. This way we don't need to track the TTL of
            /// individual entries; instead, all entries in a column sort of share the same TTL. 
            /// 
            /// On look up, we check both columns. If entry is found only on the non-active column, we copy it to the active one.
            /// Because of that, the active column always contains all entries that were accessed since it became active.
            /// 
            /// On active column change, we clear the new active column. So any entries that have not been accessed (i.e., copied
            /// to the other column) will be evicted.
            /// </summary>
            /// <remark>
            /// Change the column name can logically drop current columns which is bad 
            /// TODO: rename column to be more generic when bump format version in future
            /// </remark>
            public static string[] ContentHashMappingColumn = { "BuildManifestHash_1", "BuildManifestHash_2" };
        }

        private static byte[] StringToBytes(string str)
        {
            return Encoding.UTF8.GetBytes(str);
        }

        /// <summary>
        /// Contains key definitions for accessing cache version, age, entries blob, and garbage collection cursor in the database.
        /// </summary>
        protected readonly struct StoreKeyNames
        {
            /// <summary>
            /// The key for the cache entries blob
            /// </summary>
            public static readonly byte[] HistoricMetadataCacheEntriesKey = StringToBytes("HistoricMetadataCacheEntriesKeys");

            /// <summary>
            /// The key for the format <see cref="PipTwoPhaseCacheWithHashLookup.FormatVersion"/>
            /// used when building the cache.
            /// </summary>
            public const string FormatVersion = "FormatVersion";

            /// <summary>
            /// The key for the age at which the content was last accessed
            /// </summary>
            public const string Age = "Age";

            /// <summary>
            /// Indicates the last key where garbage collection should resume (if any)
            /// </summary>
            public static readonly byte[] ContentGarbageCollectCursor = StringToBytes(nameof(ContentGarbageCollectCursor));
        }
    }
}