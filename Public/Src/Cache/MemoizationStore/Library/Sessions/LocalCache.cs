// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
            SQLiteMemoizationStoreConfiguration memoConfig,
            ConfigurationModel configurationModel = null,
            IClock clock = null,
            bool checkLocalFiles = true,
            bool emptyFileHashShortcutEnabled = false,
            bool createServer = false,
            int grpcPort = 0,
            int maxQuotaMB = 0,
            string cacheName = null,
            string stampId = null,
            string ringId = null,
            string scenarioName = null)
            : this(
                  logger,
                  rootPath,
                  new PassThroughFileSystem(logger),
                  clock ?? SystemClock.Instance,
                  configurationModel,
                  memoConfig,
                  new ContentStoreSettings() { CheckFiles = checkLocalFiles, UseEmptyFileHashShortcut = emptyFileHashShortcutEnabled },
                  createServer: createServer,
                  grpcPort: grpcPort,
                  maxQuotaMB: maxQuotaMB,
                  cacheName: cacheName,
                  stampId: stampId,
                  ringId: ringId,
                  scenarioName: scenarioName)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="LocalCache" /> class backed by <see cref="TwoContentStore"/> implemented as <see cref="StreamPathContentStore"/>
        /// </summary>
        public LocalCache(
            ILogger logger,
            AbsolutePath rootPathForStream,
            AbsolutePath rootPathForPath,
            SQLiteMemoizationStoreConfiguration memoConfig,
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
            SQLiteMemoizationStoreConfiguration memoConfig,
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
            SQLiteMemoizationStoreConfiguration memoConfig,
            ContentStoreSettings contentStoreSettings,
            bool createServer,
            int grpcPort = 0,
            int maxQuotaMB = 0,
            string cacheName = null,
            string stampId = null,
            string ringId = null,
            string scenarioName = null)
            : base(
                () => createServer
                    ? (IContentStore)new DualServerClientContentStore( // TODO: Finish this!
                        fileSystem,
                        logger,
                        new GrpcFileCopier(new Context(logger), grpcPort),
                        new GrpcDistributedPathTransformer(),
                        maxQuotaMB,
                        cacheName,
                        grpcPort,
                        stampId,
                        ringId,
                        scenarioName,
                        rootPath)
                    : new FileSystemContentStore(
                        fileSystem, 
                        clock,
                        rootPath,
                        configurationModel: configurationModel,
                        settings: contentStoreSettings),
                () => new SQLiteMemoizationStore(
                    logger,
                    clock ?? SystemClock.Instance,
                    memoConfig),
                PersistentId.Load(fileSystem, rootPath / IdFileName))
        {
            _fileSystem = fileSystem;
        }

        private LocalCache(
            ILogger logger,
            AbsolutePath rootPathForStream,
            AbsolutePath rootPathForPath,
            IAbsFileSystem fileSystem,
            IClock clock,
            ConfigurationModel configurationModelForStream,
            ConfigurationModel configurationModelForPath,
            SQLiteMemoizationStoreConfiguration memoConfig,
            bool checkLocalFiles,
            bool emptyFileHashShortcutEnabled)
            : base(
                () => new StreamPathContentStore(
                    () => new FileSystemContentStore(fileSystem, clock ?? SystemClock.Instance, rootPathForStream, configurationModelForStream, settings: new ContentStoreSettings() { CheckFiles = checkLocalFiles, UseEmptyFileHashShortcut = emptyFileHashShortcutEnabled }),
                    () => new FileSystemContentStore(fileSystem, clock ?? SystemClock.Instance, rootPathForPath, configurationModelForPath, settings: new ContentStoreSettings() { CheckFiles = checkLocalFiles, UseEmptyFileHashShortcut = emptyFileHashShortcutEnabled })),
                () => new SQLiteMemoizationStore(
                    logger,
                    clock ?? SystemClock.Instance,
                    memoConfig),
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
            SQLiteMemoizationStoreConfiguration memoConfig)
            : base(
                () => new ServiceClientContentStore(
                    logger, fileSystem, configuration),
                () => new SQLiteMemoizationStore(
                    logger, clock ?? SystemClock.Instance, memoConfig),
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
