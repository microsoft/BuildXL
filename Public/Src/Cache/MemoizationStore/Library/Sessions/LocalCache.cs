// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Service;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    ///     A single-level local cache.
    /// </summary>
    /// <remarks>
    ///     "Local" here is used in the sense that it is located in this machine, and not over the network. For
    ///     example, the cache could live in a different process and communicate over gRPC.
    /// </remarks>
    public class LocalCache : OneLevelCache
    {
        private const string IdFileName = "Cache.id";

        private readonly IAbsFileSystem _fileSystem;
        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LocalCache" /> class backed by
        ///     <see cref="FileSystemContentStore"/>. Its content store may actually be backed by a
        ///     <see cref="ServiceClientContentStore"/> if <see cref="LocalCacheConfiguration.EnableContentServer"/> is
        ///     true.
        /// </summary>
        public static LocalCache CreateUnknownContentStoreInProcMemoizationStoreCache(
            ILogger logger,
            AbsolutePath rootPath,
            MemoizationStoreConfiguration memoizationStoreConfiguration,
            LocalCacheConfiguration localCacheConfiguration,
            ConfigurationModel configurationModel = null,
            IClock clock = null,
            bool checkLocalFiles = true,
            bool emptyFileHashShortcutEnabled = false)
        {
            clock = clock ?? SystemClock.Instance;

            var fileSystem = new PassThroughFileSystem(logger);
            var contentStoreSettings = new ContentStoreSettings() {
                CheckFiles = checkLocalFiles,
                UseEmptyFileHashShortcut = emptyFileHashShortcutEnabled
            };

            Func<IContentStore> contentStoreFactory = () =>
            {
                if (localCacheConfiguration.EnableContentServer)
                {
                    return new ServiceClientContentStore(
                                    logger,
                                    fileSystem,
                                    localCacheConfiguration.CacheName,
                                    new ServiceClientRpcConfiguration(localCacheConfiguration.GrpcPort),
                                    (uint)localCacheConfiguration.RetryIntervalSeconds,
                                    (uint)localCacheConfiguration.RetryCount,
                                    scenario: localCacheConfiguration.ScenarioName);
                }
                else
                {
                    return new FileSystemContentStore(
                                    fileSystem,
                                    clock,
                                    rootPath,
                                    configurationModel: configurationModel,
                                    settings: contentStoreSettings);
                }
            };

            var memoizationStoreFactory = CreateInProcessLocalMemoizationStoreFactory(logger, clock, memoizationStoreConfiguration);
            return new LocalCache(fileSystem, contentStoreFactory, memoizationStoreFactory, LoadPersistentCacheGuid(rootPath, fileSystem));
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="LocalCache" /> class backed by <see cref="TwoContentStore"/> implemented as <see cref="StreamPathContentStore"/>
        /// </summary>
        public static LocalCache CreateStreamPathContentStoreInProcMemoizationStoreCache(ILogger logger,
            AbsolutePath rootPathForStream,
            AbsolutePath rootPathForPath,
            MemoizationStoreConfiguration memoConfig,
            ConfigurationModel configurationModelForStream = null,
            ConfigurationModel configurationModelForPath = null,
            IClock clock = null,
            bool checkLocalFiles = true,
            bool emptyFileHashShortcutEnabled = false)
        {
            var fileSystem = new PassThroughFileSystem(logger);
            clock = clock ?? SystemClock.Instance;

            var contentStoreSettings = new ContentStoreSettings() { CheckFiles = checkLocalFiles, UseEmptyFileHashShortcut = emptyFileHashShortcutEnabled };

            Func<IContentStore> contentStoreFactory = () => new StreamPathContentStore(
                                () => new FileSystemContentStore(fileSystem, clock, rootPathForStream, configurationModelForStream, settings: contentStoreSettings),
                                () => new FileSystemContentStore(fileSystem, clock, rootPathForPath, configurationModelForPath, settings: contentStoreSettings));
            var memoizationStoreFactory = CreateInProcessLocalMemoizationStoreFactory(logger, clock, memoConfig);
            return new LocalCache(fileSystem, contentStoreFactory, memoizationStoreFactory, LoadPersistentCacheGuid(rootPathForStream, fileSystem));
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="LocalCache" /> class for which content is backed by
        ///     <see cref="ServiceClientContentStore"/>, and metadata is backed by a local memoization store.
        /// </summary>
        public static LocalCache CreateRpcContentStoreInProcMemoizationStoreCache(ILogger logger,
            AbsolutePath rootPath,
            ServiceClientContentStoreConfiguration serviceClientContentStoreConfiguration,
            MemoizationStoreConfiguration memoizationStoreConfiguration,
            IClock clock = null)
        {
            var fileSystem = new PassThroughFileSystem(logger);
            clock = clock ?? SystemClock.Instance;

            Func<IContentStore> remoteContentStoreFactory = () => new ServiceClientContentStore(logger, fileSystem, serviceClientContentStoreConfiguration);
            var localMemoizationStoreFactory = CreateInProcessLocalMemoizationStoreFactory(logger, clock, memoizationStoreConfiguration);
            return new LocalCache(fileSystem, remoteContentStoreFactory, localMemoizationStoreFactory, LoadPersistentCacheGuid(rootPath, fileSystem));
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="LocalCache" /> class entirely backed by
        ///     <see cref="ServiceClientCache"/>
        /// </summary>
        public static LocalCache CreateRpcCache(
            ILogger logger,
            AbsolutePath rootPath,
            ServiceClientContentStoreConfiguration serviceClientCacheConfiguration)
        {
            var fileSystem = new PassThroughFileSystem(logger);
            var remoteCache = new ServiceClientCache(logger, fileSystem, serviceClientCacheConfiguration);
            return new LocalCache(fileSystem, () => remoteCache, () => remoteCache, LoadPersistentCacheGuid(rootPath, fileSystem));
        }

        private LocalCache(IAbsFileSystem fileSystem, Func<IContentStore> contentStoreFunc, Func<IMemoizationStore> memoizationStoreFunc, Guid id)
            : base(contentStoreFunc, memoizationStoreFunc, id)
        {
            Contract.Requires(fileSystem != null);

            _fileSystem = fileSystem;
        }

        private static Guid LoadPersistentCacheGuid(AbsolutePath rootPath, IAbsFileSystem fileSystem)
        {
            return PersistentId.Load(fileSystem, rootPath / IdFileName);
        }

        private static Func<IMemoizationStore> CreateInProcessLocalMemoizationStoreFactory(ILogger logger, IClock clock, MemoizationStoreConfiguration config)
        {
            Contract.Requires(config != null);

            if (config is SQLiteMemoizationStoreConfiguration sqliteConfig)
            {
                return () => new SQLiteMemoizationStore(
                    logger,
                    clock ?? SystemClock.Instance,
                    sqliteConfig);
            }
            else if (config is RocksDbMemoizationStoreConfiguration rocksDbConfig)
            {
                return () => new RocksDbMemoizationStore(
                    logger,
                    clock ?? SystemClock.Instance,
                    rocksDbConfig);
            }
            else
            {
                throw new NotSupportedException($"Configuration type '{config.GetType()}' for memoization store is unhandled.");
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> PreStartupAsync(Context context)
        {
            var contentStore = ContentStore as IAcquireDirectoryLock;
            if (contentStore != null)
            {
                var acquireLockResult = await contentStore.AcquireDirectoryLockAsync(context);
                if (!acquireLockResult.Succeeded)
                {
                    return acquireLockResult;
                }
            }

            if (MemoizationStore is IAcquireDirectoryLock memoizationStore)
            {
                var acquireLockResult = await memoizationStore.AcquireDirectoryLockAsync(context);
                if (!acquireLockResult.Succeeded)
                {
                    if (contentStore != null)
                    {
                        ContentStore?.Dispose(); // Dispose to release the content store's directory lock.
                    }

                    return acquireLockResult;
                }
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _fileSystem?.Dispose();
            }

            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
