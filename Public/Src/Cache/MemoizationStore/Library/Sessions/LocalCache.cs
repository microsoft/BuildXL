// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Host.Service.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    ///     A one-level, local cache.
    /// </summary>
    public class LocalCache : OneLevelCache
    {
        private const string IdFileName = "Cache.id";

        private readonly IAbsFileSystem _fileSystem;
        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LocalCache" /> class backed by <see cref="FileSystemContentStore"/>
        /// </summary>
        public LocalCache(
            ILogger logger,
            AbsolutePath rootPath,
            MemoizationStoreConfiguration memoConfig,
            LocalCacheConfiguration localCacheConfiguration,
            ConfigurationModel configurationModel = null,
            IClock clock = null,
            bool checkLocalFiles = true,
            bool emptyFileHashShortcutEnabled = false)
            : this(
                  logger,
                  rootPath,
                  new PassThroughFileSystem(logger),
                  clock ?? SystemClock.Instance,
                  configurationModel,
                  memoConfig,
                  new ContentStoreSettings() { CheckFiles = checkLocalFiles, UseEmptyFileHashShortcut = emptyFileHashShortcutEnabled },
                  localCacheConfiguration)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="LocalCache" /> class backed by <see cref="TwoContentStore"/> implemented as <see cref="StreamPathContentStore"/>
        /// </summary>
        public LocalCache(
            ILogger logger,
            AbsolutePath rootPathForStream,
            AbsolutePath rootPathForPath,
            MemoizationStoreConfiguration memoConfig,
            ConfigurationModel configurationModelForStream = null,
            ConfigurationModel configurationModelForPath = null,
            IClock clock = null,
            bool checkLocalFiles = true,
            bool emptyFileHashShortcutEnabled = false)
            : this(
                  logger,
                  rootPathForStream,
                  rootPathForPath,
                  new PassThroughFileSystem(logger),
                  clock ?? SystemClock.Instance,
                  configurationModelForStream,
                  configurationModelForPath,
                  memoConfig,
                  checkLocalFiles,
                  emptyFileHashShortcutEnabled)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="LocalCache" /> class backed by <see cref="ServiceClientContentStore"/>
        /// </summary>
        public LocalCache(
            ILogger logger,
            string cacheName,
            AbsolutePath rootPath,
            ServiceClientRpcConfiguration rpcConfiguration,
            uint retryIntervalSeconds,
            uint retryCount,
            MemoizationStoreConfiguration memoConfig,
            IClock clock = null,
            string scenarioName = null)
            : this(
                  logger,
                  rootPath,
                  new PassThroughFileSystem(logger),
                  clock ?? SystemClock.Instance,
                  new ServiceClientContentStoreConfiguration(cacheName, rpcConfiguration, scenarioName)
                  {
                      RetryCount = retryCount,
                      RetryIntervalSeconds = retryIntervalSeconds,
                  }, 
                  memoConfig)
        {
        }

        private LocalCache(
            ILogger logger,
            AbsolutePath rootPath,
            IAbsFileSystem fileSystem,
            IClock clock,
            ConfigurationModel configurationModel,
            MemoizationStoreConfiguration memoConfig,
            ContentStoreSettings contentStoreSettings,
            LocalCacheConfiguration localCacheConfiguration)
            : base(
                () => localCacheConfiguration.EnableContentServer
                    ? (IContentStore)new ServiceClientContentStore(
                        logger,
                        fileSystem,
                        localCacheConfiguration.CacheName,
                        new ServiceClientRpcConfiguration(localCacheConfiguration.GrpcPort),
                        (uint)localCacheConfiguration.RetryIntervalSeconds,
                        (uint)localCacheConfiguration.RetryCount,
                        scenario: localCacheConfiguration.ScenarioName)
                    : new FileSystemContentStore(
                        fileSystem,
                        clock,
                        rootPath,
                        configurationModel: configurationModel,
                        settings: contentStoreSettings),
                CreateMemoizationStoreFactory(logger, clock, memoConfig),
                PersistentId.Load(fileSystem, rootPath / IdFileName))
        {
            _fileSystem = fileSystem;
        }

        private static Func<IMemoizationStore> CreateMemoizationStoreFactory(ILogger logger, IClock clock, MemoizationStoreConfiguration config)
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

        private LocalCache(
            ILogger logger,
            AbsolutePath rootPathForStream,
            AbsolutePath rootPathForPath,
            IAbsFileSystem fileSystem,
            IClock clock,
            ConfigurationModel configurationModelForStream,
            ConfigurationModel configurationModelForPath,
            MemoizationStoreConfiguration memoConfig,
            bool checkLocalFiles,
            bool emptyFileHashShortcutEnabled)
            : base(
                () => new StreamPathContentStore(
                    () => new FileSystemContentStore(fileSystem, clock ?? SystemClock.Instance, rootPathForStream, configurationModelForStream, settings: new ContentStoreSettings() { CheckFiles = checkLocalFiles, UseEmptyFileHashShortcut = emptyFileHashShortcutEnabled }),
                    () => new FileSystemContentStore(fileSystem, clock ?? SystemClock.Instance, rootPathForPath, configurationModelForPath, settings: new ContentStoreSettings() { CheckFiles = checkLocalFiles, UseEmptyFileHashShortcut = emptyFileHashShortcutEnabled })),
                CreateMemoizationStoreFactory(logger, clock, memoConfig),
                PersistentId.Load(fileSystem, rootPathForPath / IdFileName))
        {
            _fileSystem = fileSystem;
        }
        
        private LocalCache(
            ILogger logger,
            AbsolutePath rootPath,
            IAbsFileSystem fileSystem,
            IClock clock,
            ServiceClientContentStoreConfiguration configuration,
            MemoizationStoreConfiguration memoConfig)
            : base(
                () => new ServiceClientContentStore(
                    logger, fileSystem, configuration),
                CreateMemoizationStoreFactory(logger, clock, memoConfig),
                PersistentId.Load(fileSystem, rootPath / IdFileName))
        {
            _fileSystem = fileSystem;
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
