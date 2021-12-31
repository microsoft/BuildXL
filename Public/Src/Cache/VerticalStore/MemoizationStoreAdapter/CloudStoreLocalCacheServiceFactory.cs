// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Cache Factory for BuildXL as a CASaaS client in CloudBuild
    /// </summary>
    /// <remarks>
    /// This is the class responsible for creating the BuildXL to Cache adapter in the CloudBuild environment. It
    /// configures the cache client for the BuildXL process.
    /// </remarks>
    public class CloudStoreLocalCacheServiceFactory : ICacheFactory
    {
        private sealed class Config
        {
            /// <summary>
            /// The Id of the cache instance.
            /// </summary>
            [DefaultValue(typeof(CacheId))]
            public CacheId CacheId { get; set; }

            /// <summary>
            /// Root path for storing metadata entries.
            /// </summary>
            public string MetadataRootPath { get; set; }

            /// <summary>
            /// Path to the log file for the cache.
            /// </summary>
            public string MetadataLogPath { get; set; }

            /// <summary>
            /// Name of one of the named caches owned by CASaaS.
            /// </summary>
            public string CacheName { get; set; }

            /// <summary>
            /// How many seconds each call should wait for a CASaaS connection before retrying.
            /// </summary>
            [DefaultValue(5)]
            public uint ConnectionRetryIntervalSeconds { get; set; }

            /// <summary>
            /// How many times each call should retry connecting to CASaaS before timing out.
            /// </summary>
            [DefaultValue(12)]
            public uint ConnectionRetryCount { get; set; }

            /// <summary>
            /// A custom scenario to connect to for the CAS service.
            /// </summary>
            [DefaultValue(null)]
            public string ScenarioName { get; set; }

            /// <summary>
            /// A custom scenario to connect to for the CAS service. If set, overrides GrpcPortFileName.
            /// </summary>
            [DefaultValue(0)]
            public uint GrpcPort { get; set; }

            /// <summary>
            /// Custom name of the memory-mapped file to read from to find the GRPC port used for the CAS service.
            /// </summary>
            [DefaultValue(null)]
            public string GrpcPortFileName { get; set; }

            /// <nodoc />
            [DefaultValue(false)]
            public bool GrpcTraceOperationStarted { get; set; }

            /// <summary>
            /// Duration to wait for exclusive access to the cache directory before timing out.
            /// </summary>
            [DefaultValue(0)]
            public uint LogFlushIntervalSeconds { get; set; }

            [DefaultValue(false)]
            public bool ReplaceExistingOnPlaceFile { get; set; }

            [DefaultValue(false)]
            public bool EnableMetadataServer { get; set; }

            [DefaultValue(60 * 60)]
            public int RocksDbMemoizationStoreGarbageCollectionIntervalInSeconds { get; set; }

            [DefaultValue(500_000)]
            public int RocksDbMemoizationStoreGarbageCollectionMaximumNumberOfEntriesToKeep { get; set; }

            /// <summary>
            /// Path to the root of VFS cas
            /// </summary>
            [DefaultValue(null)]
            public string VfsCasRoot { get; set; }

            /// <summary>
            /// Indicates whether symlinks should be used to specify VFS files
            /// </summary>
            [DefaultValue(true)]
            public bool VfsUseSymlinks { get; set; } = true;

            /// <nodoc />
            [DefaultValue(null)]
            public GrpcEnvironmentOptions GrpcEnvironmentOptions { get; set; }

            /// <nodoc />
            [DefaultValue(null)]
            public GrpcCoreClientOptions GrpcCoreClientOptions { get; set; }
        }

        /// <inheritdoc />
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId, ICacheConfiguration cacheConfiguration = null)
        {
            Contract.Requires(cacheData != null);

            var possibleCacheConfig = cacheData.Create<Config>();
            if (!possibleCacheConfig.Succeeded)
            {
                return possibleCacheConfig.Failure;
            }

            Config cacheConfig = possibleCacheConfig.Result;

            try
            {
                Contract.Assert(cacheConfig.CacheName != null);
                var logPath = new AbsolutePath(cacheConfig.MetadataLogPath);
                var logger = new DisposeLogger(() => new EtwFileLog(logPath.Path, cacheConfig.CacheId), cacheConfig.LogFlushIntervalSeconds);

                logger.Debug($"Creating CASaaS backed LocalCache using cache name: {cacheConfig.CacheName}");

                var cache = CreateCache(cacheConfig, logPath, logger);

                var startupResult = await cache.StartupAsync();
                if (!startupResult.Succeeded)
                {
                    logger.Error($"Error while initializing the cache [{cacheConfig.CacheId}]. Failure: {startupResult.Failure}");
                    return startupResult.Failure;
                }

                logger.Debug("Successfully started CloudStoreLocalCacheService client.");
                return cache;
            }
            catch (Exception e)
            {
                return new CacheConstructionFailure(cacheConfig.CacheId, e);
            }
        }

        private static MemoizationStoreAdapterCache CreateCache(Config cacheConfig, AbsolutePath logPath, ILogger logger)
        {
            var serviceClientContentStoreConfiguration = CreateGrpcServiceConfiguration(cacheConfig, logger);

            MemoizationStore.Interfaces.Caches.ICache localCache;
            if (cacheConfig.EnableMetadataServer)
            {
                // CaChaaS path.
                localCache = LocalCache.CreateRpcCache(logger, serviceClientContentStoreConfiguration);
            }
            else
            {
                // CASaaS path. We construct an in-proc memoization store in this case.
                var metadataRootPath = new AbsolutePath(cacheConfig.MetadataRootPath);

                localCache = LocalCache.CreateRpcContentStoreInProcMemoizationStoreCache(logger,
                    metadataRootPath,
                    serviceClientContentStoreConfiguration,
                    CreateInProcMemoizationStoreConfiguration(cacheConfig, metadataRootPath));
            }

            var statsFilePath = new AbsolutePath(logPath.Path + ".stats");
            if (!string.IsNullOrEmpty(cacheConfig.VfsCasRoot))
            {
                // Vfs path. Vfs wraps around whatever cache we are using to virtualize
                logger.Debug($"Creating virtualized cache");

                localCache = new VirtualizedContentCache(localCache, new ContentStore.Vfs.VfsCasConfiguration.Builder()
                {
                    RootPath = new AbsolutePath(cacheConfig.VfsCasRoot),

                }.Build());
            }

            return new MemoizationStoreAdapterCache(cacheConfig.CacheId, localCache, logger, statsFilePath, cacheConfig.ReplaceExistingOnPlaceFile);
        }

        private static ServiceClientContentStoreConfiguration CreateGrpcServiceConfiguration(Config cacheConfig, ILogger logger)
        {
            var rpcConfiguration = CreateGrpcClientConfiguration(cacheConfig, logger);

            var serviceClientContentStoreConfiguration = new ServiceClientContentStoreConfiguration(cacheConfig.CacheName, rpcConfiguration, cacheConfig.ScenarioName)
            {
                RetryCount = cacheConfig.ConnectionRetryCount,
                RetryIntervalSeconds = cacheConfig.ConnectionRetryIntervalSeconds,
                TraceOperationStarted = cacheConfig.GrpcTraceOperationStarted,
            };

            serviceClientContentStoreConfiguration.GrpcEnvironmentOptions = cacheConfig.GrpcEnvironmentOptions;
            return serviceClientContentStoreConfiguration;
        }

        private static ServiceClientRpcConfiguration CreateGrpcClientConfiguration(Config cacheConfig, ILogger logger)
        {
            ServiceClientRpcConfiguration rpcConfiguration = new ServiceClientRpcConfiguration();
            if (cacheConfig.GrpcPort > 0)
            {
                rpcConfiguration.GrpcPort = (int)cacheConfig.GrpcPort;
            }
            else
            {
                var factory = new MemoryMappedFileGrpcPortSharingFactory(logger, cacheConfig.GrpcPortFileName);
                var portReader = factory.GetPortReader();
                rpcConfiguration.GrpcPort = portReader.ReadPort();
            }
            rpcConfiguration.GrpcCoreClientOptions = cacheConfig.GrpcCoreClientOptions;
            return rpcConfiguration;
        }

        private static MemoizationStoreConfiguration CreateInProcMemoizationStoreConfiguration(Config cacheConfig, AbsolutePath cacheRootPath)
        {
            return new RocksDbMemoizationStoreConfiguration()
            {
                Database = new RocksDbContentLocationDatabaseConfiguration(cacheRootPath / "RocksDbMemoizationStore")
                {
                    CleanOnInitialize = false,
                    GarbageCollectionInterval = TimeSpan.FromSeconds(cacheConfig.RocksDbMemoizationStoreGarbageCollectionIntervalInSeconds),
                    MetadataGarbageCollectionEnabled = true,
                    MetadataGarbageCollectionMaximumNumberOfEntriesToKeep = cacheConfig.RocksDbMemoizationStoreGarbageCollectionMaximumNumberOfEntriesToKeep,
                    OnFailureDeleteExistingStoreAndRetry = true,
                    LogsKeepLongTerm = true,
                },
            };
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, cacheConfig =>
            {
                var failures = new List<Failure>();
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheId, nameof(cacheConfig.CacheId));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheName, nameof(cacheConfig.CacheName));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.MetadataLogPath, nameof(cacheConfig.MetadataLogPath));
                return failures;
            });
        }
    }
}
