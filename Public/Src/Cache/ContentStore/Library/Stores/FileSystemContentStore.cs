// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     An <see cref="IContentStore"/> implemented over <see cref="FileSystemContentStoreInternal"/>
    /// </summary>
    public class FileSystemContentStore : StartupShutdownBase, IContentStore, IAcquireDirectoryLock, IRepairStore, ILocalContentStore, IStreamStore
    {
        private const string Component = nameof(FileSystemContentStore);

        private readonly DirectoryLock _directoryLock;
        private readonly ContentStoreTracer _tracer = new ContentStoreTracer(nameof(FileSystemContentStore));

        /// <summary>
        ///     Gets the underlying store implementation.
        /// </summary>
        internal readonly FileSystemContentStoreInternal Store;

        /// <summary>
        ///     Removes provided cache content snapshot from the content location store.
        /// </summary>
        private readonly TrimBulkAsync _trimBulkAsync;

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        /// <summary>
        /// Backward-compat constructor.
        /// </summary>
        public FileSystemContentStore(
            IAbsFileSystem fileSystem,
            IClock clock,
            AbsolutePath rootPath,
            ConfigurationModel configurationModel,
            NagleQueue<ContentHash> nagleQueue,
            RefCountdown sensitiveSessionCount,
            DistributedEvictionSettings distributedEvictionSettings,
            bool checkFiles,
            TrimBulkAsync trimBulkAsync)
            : this(
                fileSystem,
                clock,
                rootPath,
                configurationModel,
                nagleQueue,
                distributedEvictionSettings,
                trimBulkAsync,
                settings: new ContentStoreSettings() {CheckFiles = checkFiles})
        {

        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FileSystemContentStore" /> class.
        /// </summary>
        public FileSystemContentStore(
            IAbsFileSystem fileSystem,
            IClock clock,
            AbsolutePath rootPath,
            ConfigurationModel configurationModel = null,
            NagleQueue<ContentHash> nagleQueue = null,
            DistributedEvictionSettings distributedEvictionSettings = null,
            TrimBulkAsync trimBulkAsync = null,
            ContentStoreSettings settings = null)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(clock != null);
            Contract.Requires(rootPath != null);

            int singleInstanceTimeoutSeconds = ContentStoreConfiguration.DefaultSingleInstanceTimeoutSeconds;
            if (configurationModel?.InProcessConfiguration != null)
            {
                // TODO: Stop using the configurationModel's SingleInstanceTimeout (bug 1365340)
                // because FileSystemContentStore doesn't respect the config file's value
                singleInstanceTimeoutSeconds = configurationModel.InProcessConfiguration.SingleInstanceTimeoutSeconds;
            }

            // FileSystemContentStore implicitly uses a null component name for compatibility with older versions' directory locks.
            _directoryLock = new DirectoryLock(rootPath, fileSystem, TimeSpan.FromSeconds(singleInstanceTimeoutSeconds));

            Store = new FileSystemContentStoreInternal(
                fileSystem,
                clock,
                rootPath,
                configurationModel,
                nagleQueue,
                distributedEvictionSettings,
                settings);

            _trimBulkAsync = trimBulkAsync;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            BoolResult result;

            var acquireLockResult = await AcquireDirectoryLockAsync(context);
            if (acquireLockResult.Succeeded)
            {
                result = await Store.StartupAsync(context);
            }
            else
            {
                result = acquireLockResult;
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<BoolResult> AcquireDirectoryLockAsync(Context context)
        {
            var aquisitingResult = await _directoryLock.AcquireAsync(context);
            if (aquisitingResult.LockAcquired)
            {
                return BoolResult.Success;
            }

            var errorMessage = aquisitingResult.GetErrorMessage(Component);
            return new BoolResult(errorMessage);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return Store.ShutdownAsync(context);
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            Store.Dispose();
            _directoryLock.Dispose();
        }

        /// <inheritdoc />
        public virtual CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(_tracer, OperationContext(context), name, () =>
            {
                var session = new ReadOnlyFileSystemContentSession(name, Store, implicitPin);
                return new CreateSessionResult<IReadOnlyContentSession>(session);
            });
        }

        /// <inheritdoc />
        public virtual CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run(_tracer, OperationContext(context), name, () =>
            {
                var session = new FileSystemContentSession(name, implicitPin, Store);
                return new CreateSessionResult<IContentSession>(session);
            });
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<ContentStoreTracer>.RunAsync(_tracer, OperationContext(context), () => Store.GetStatsAsync(context));
        }

        /// <inheritdoc />
        public Task<StructResult<long>> RemoveFromTrackerAsync(Context context)
        {
            return RemoveFromTrackerCall<ContentStoreTracer>.RunAsync(_tracer, OperationContext(context), async () =>
            {
                if (_trimBulkAsync == null)
                {
                    return new StructResult<long>("Repair handling not enabled.");
                }

                var contentHashes = await Store.EnumerateContentHashesAsync();
                return await _trimBulkAsync(context, contentHashes, CancellationToken.None, UrgencyHint.Nominal);
            });
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ContentInfo>> GetContentInfoAsync(CancellationToken token)
        {
            // TODO: add cancellation support for EnumerateContentInfoAsync
            return Store.EnumerateContentInfoAsync();
        }

        /// <inheritdoc />
        public bool Contains(ContentHash hash)
        {
            return Store.Contains(hash);
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            return Store.OpenStreamAsync(context, contentHash, pinRequest: null);
        }

        /// <inheritdoc />
        public async Task<FileExistenceResult> CheckFileExistsAsync(Context context, ContentHash contentHash)
        {
            if (await Store.ContainsAsync(context, contentHash, null))
            {
                return new FileExistenceResult();
            }
            else
            {
                return new FileExistenceResult(FileExistenceResult.ResultCode.FileNotFound, $"{contentHash.ToShortString()} wasn't found in the cache");
            }
        }

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash)
        {
            return Store.DeleteAsync(context, contentHash);
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result) { }
    }
}
