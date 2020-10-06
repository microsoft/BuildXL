// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.Roxis.Client;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Cache Factory for BuildXL as a cache user for dev machines
    /// </summary>
    /// <remarks>
    /// This is the class responsible for creating the BuildXL to Cache adapter in the Selfhost builds. It configures
    /// the cache on the BuildXL process.
    /// 
    /// Current limitations while we flesh things out:
    /// 1) APIs around tracking named sessions are not implemented
    /// </remarks>
    public class MemoizationStoreCacheFactory : ICacheFactory
    {
        private const string DefaultStreamFolder = "streams";

        /// <summary>
        /// Configuration for <see cref="MemoizationStoreCacheFactory"/>.
        /// </summary>
        public sealed class Config : CasConfig
        {
            /// <summary>
            /// The Id of the cache instance
            /// </summary>
            [DefaultValue(typeof(CacheId))]
            public CacheId CacheId { get; set; }

            /// <summary>
            /// Max number of CasEntries entries.
            /// </summary>
            public uint MaxStrongFingerprints { get; set; }

            /// <summary>
            /// Path to the log file for the cache.
            /// </summary>
            public string CacheLogPath { get; set; }

            /// <summary>
            /// Path to the root of VFS cas
            /// </summary>
            [DefaultValue(null)]
            public string VfsCasRoot { get; set; }

            /// <summary>
            /// Indicates whether symlinks should be used to specify VFS files
            /// </summary>
            [DefaultValue(true)]
            public bool UseVfsSymlinks { get; set; } = true;

            /// <summary>
            /// If true, use a different CAS for streams, specified by <see cref="StreamCAS"/>.
            /// </summary>
            public bool UseStreamCAS { get; set; }

            /// <summary>
            /// Configuration for stream CAS.
            /// </summary>
            [DefaultValue(null)]
            public CasConfig StreamCAS { get; set; }

            /// <summary>
            /// Duration to wait for exclusive access to the cache directory before timing out.
            /// </summary>
            [DefaultValue(0)]
            public uint LogFlushIntervalSeconds { get; set; }

            /// <summary>
            /// Whether to check for file existence before pinning.
            /// </summary>
            [DefaultValue(false)]
            public bool CheckLocalFiles { get; set; }

            /// <summary>
            /// Whether the cache will communicate with a server in a separate process via GRPC.
            /// </summary>
            [DefaultValue(false)]
            public bool EnableContentServer { get; set; }

            /// <summary>
            /// Name of one of the named caches owned by CASaaS.
            /// </summary>
            [DefaultValue(null)]
            public string CacheName { get; set; }

            /// <summary>
            /// The GRPC port to use.
            /// </summary>
            [DefaultValue(0)]
            public int GrpcPort { get; set; }

            /// <summary>
            /// Name of the custom scenario that the CAS connects to.
            /// allows multiple CAS services to coexist in a machine
            /// since this factors into the cache root and the event that
            /// identifies a particular CAS instance.
            /// </summary>
            [DefaultValue(null)]
            public string ScenarioName { get; set; }

            /// <nodoc />
            [DefaultValue(ServiceClientContentStoreConfiguration.DefaultRetryIntervalSeconds)]
            public int RetryIntervalSeconds { get; set; }

            /// <nodoc />
            [DefaultValue(ServiceClientContentStoreConfiguration.DefaultRetryCount)]
            public int RetryCount { get; set; }

            /// <nodoc />
            [DefaultValue(false)]
            public bool ReplaceExistingOnPlaceFile { get; set; }

            /// <summary>
            /// Whether the cache will communicate with a server in a separate process via GRPC.
            /// </summary>
            [DefaultValue(false)]
            public bool EnableMetadataServer { get; set; }

            /// <nodoc />
            [DefaultValue(60 * 60)]
            public int RocksDbMemoizationStoreGarbageCollectionIntervalInSeconds { get; set; }

            /// <nodoc />
            [DefaultValue(500_000)]
            public int RocksDbMemoizationStoreGarbageCollectionMaximumNumberOfEntriesToKeep { get; set; }

            /// <nodoc />
            [DefaultValue(false)]
            public bool RoxisEnabled { get; set; }

            /// <nodoc />
            [DefaultValue("")]
            public string RoxisMetadataStoreHost { get; set; }

            /// <nodoc />
            [DefaultValue(-1)]
            public int RoxisMetadataStorePort { get; set; }

            /// <nodoc />
            [DefaultValue(null)]
            public GrpcEnvironmentOptions GrpcEnvironmentOptions { get; set; }

            /// <nodoc />
            [DefaultValue(null)]
            public GrpcCoreClientOptions GrpcCoreClientOptions { get; set; }

            /// <nodoc />
            public Config()
            {
                CacheId = new CacheId("FileSystemCache");
                MaxStrongFingerprints = 500000;
                CacheLogPath = null;

                MaxCacheSizeInMB = 512000;
                DiskFreePercent = 0;
                CacheRootPath = null;
                SingleInstanceTimeoutInSeconds = ContentStoreConfiguration.DefaultSingleInstanceTimeoutSeconds;
                ApplyDenyWriteAttributesOnContent = ContentStoreConfiguration.DefaultApplyDenyWriteAttributesOnContent;
                UseStreamCAS = false;
                StreamCAS = null;
                ReplaceExistingOnPlaceFile = false;
            }
        }

        /// <summary>
        /// CAS configuration.
        /// </summary>
        public class CasConfig
        {
            /// <summary>
            /// Max size of the cache in MB.
            /// </summary>
            public int MaxCacheSizeInMB { get; set; }

            /// <summary>
            /// Percentage of disk free space to maintain - zero/negative to disable this quota.
            /// </summary>
            public uint DiskFreePercent { get; set; }

            /// <summary>
            /// Root path for storing CAS entries.
            /// </summary>
            public string CacheRootPath { get; set; }

            /// <summary>
            /// Duration to wait for exclusive access to the cache directory before timing out.
            /// </summary>
            public uint SingleInstanceTimeoutInSeconds { get; set; }

            /// <summary>
            /// If true, the cache will set Deny-WriteAttributes ACLs on files for which ReadOnly is requested.
            /// </summary>
            public bool ApplyDenyWriteAttributesOnContent { get; set; }

            /// <summary>
            /// If true, enable elastic CAS.
            /// </summary>
            public bool EnableElasticity { get; set; }

            /// <summary>
            /// Initial size for elastic CAS
            /// </summary>
            public uint InitialElasticSizeInMB { get; set; }

            /// <summary>
            /// Size of history buffer.
            /// </summary>
            public uint HistoryBufferSize { get; set; }

            /// <summary>
            /// Size of history window.
            /// </summary>
            public uint HistoryWindowSize { get; set; }

            /// <nodoc />
            public CasConfig()
            {
                // The size 1G is based on data from an Office builds, where pathset and metadata are of size 80M.
                // The size 1G can give 10 builds before start losing the metadata/pathset.
                MaxCacheSizeInMB = 1024;
                DiskFreePercent = 0;

                // Setting it to empty to support conversion by CacheFactory.
                CacheRootPath = string.Empty;
                SingleInstanceTimeoutInSeconds = ContentStoreConfiguration.DefaultSingleInstanceTimeoutSeconds;
                ApplyDenyWriteAttributesOnContent = ContentStoreConfiguration.DefaultApplyDenyWriteAttributesOnContent;

                // Elasticity.
                EnableElasticity = false;
                InitialElasticSizeInMB = 1024;
                HistoryBufferSize = 0;
                HistoryWindowSize = 0;
            }
        }

        /// <inheritdoc />
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId)
        {
            Contract.Requires(cacheData != null);

            var possibleCacheConfig = cacheData.Create<Config>();
            if (!possibleCacheConfig.Succeeded)
            {
                return possibleCacheConfig.Failure;
            }

            Config cacheConfig = possibleCacheConfig.Result;

            return await InitializeCacheAsync(cacheConfig, activityId);
        }

        /// <summary>
        /// Create cache using configuration
        /// </summary>
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(Config cacheConfig, Guid activityId)
        {
            Contract.Requires(cacheConfig != null);

            try
            {
                var logPath = new AbsolutePath(cacheConfig.CacheLogPath);
                var logger = new DisposeLogger(() => new EtwFileLog(logPath.Path, cacheConfig.CacheId), cacheConfig.LogFlushIntervalSeconds);

                var localCache = cacheConfig.UseStreamCAS
                    ? CreateLocalCacheWithStreamPathCas(cacheConfig, logger)
                    : CreateGrpcCache(cacheConfig, logger);

                var statsFilePath = new AbsolutePath(logPath.Path + ".stats");
                if (!string.IsNullOrEmpty(cacheConfig.VfsCasRoot))
                {
                    localCache = new VirtualizedContentCache(localCache, new ContentStore.Vfs.VfsCasConfiguration.Builder()
                            {
                                RootPath = new AbsolutePath(cacheConfig.VfsCasRoot),
                                UseSymlinks = cacheConfig.UseVfsSymlinks
                            }.Build());
                }

                var cache = new MemoizationStoreAdapterCache(cacheConfig.CacheId, localCache, logger, statsFilePath, cacheConfig.ReplaceExistingOnPlaceFile);

                var startupResult = await cache.StartupAsync();
                if (!startupResult.Succeeded)
                {
                    return startupResult.Failure;
                }

                return cache;
            }
            catch (Exception e)
            {
                return new CacheConstructionFailure(cacheConfig.CacheId, e);
            }
        }

        private static CasConfig GetCasConfig(Config config)
        {
            return new CasConfig
                   {
                       ApplyDenyWriteAttributesOnContent = config.ApplyDenyWriteAttributesOnContent,
                       CacheRootPath = config.CacheRootPath,
                       DiskFreePercent = config.DiskFreePercent,
                       MaxCacheSizeInMB = config.MaxCacheSizeInMB,
                       SingleInstanceTimeoutInSeconds = config.SingleInstanceTimeoutInSeconds,
                       EnableElasticity = config.EnableElasticity,
                       InitialElasticSizeInMB = config.InitialElasticSizeInMB,
                       HistoryBufferSize = config.HistoryBufferSize,
                       HistoryWindowSize = config.HistoryWindowSize,
                   };
        }

        private static ConfigurationModel CreateConfigurationModel(CasConfig casConfig)
        {
            return new ConfigurationModel(
                new ContentStoreConfiguration(
                    new MaxSizeQuota(I($"{casConfig.MaxCacheSizeInMB}MB")),
                    casConfig.DiskFreePercent > 0 ? new DiskFreePercentQuota(casConfig.DiskFreePercent) : null,
                    casConfig.ApplyDenyWriteAttributesOnContent
                        ? DenyWriteAttributesOnContentSetting.Enable
                        : DenyWriteAttributesOnContentSetting.Disable,
                    (int)casConfig.SingleInstanceTimeoutInSeconds,
                    casConfig.EnableElasticity,
                    casConfig.InitialElasticSizeInMB > 0 ? new MaxSizeQuota(I($"{casConfig.InitialElasticSizeInMB}MB")) : null,
                    casConfig.HistoryBufferSize > 0 ? (int?)casConfig.HistoryBufferSize : null,
                    casConfig.HistoryWindowSize > 0 ? (int?)casConfig.HistoryWindowSize : null));
        }

        private static void SetDefaultsForStreamCas(Config config)
        {
            Contract.Requires(config.UseStreamCAS);

            if (config.StreamCAS == null)
            {
                config.StreamCAS = new CasConfig();
            }

            if (string.IsNullOrWhiteSpace(config.StreamCAS.CacheRootPath))
            {
                config.StreamCAS.CacheRootPath = System.IO.Path.Combine(config.CacheRootPath, DefaultStreamFolder);
            }
        }

        private static MemoizationStoreConfiguration GetInProcMemoizationStoreConfiguration(AbsolutePath cacheRoot, Config config, CasConfig configCore)
        {
            if (config.RoxisEnabled)
            {
                var roxisClientConfiguration = new RoxisClientConfiguration();

                if (!string.IsNullOrEmpty(config.RoxisMetadataStoreHost))
                {
                    roxisClientConfiguration.GrpcHost = config.RoxisMetadataStoreHost;
                }

                if (config.RoxisMetadataStorePort > 0)
                {
                    roxisClientConfiguration.GrpcPort = config.RoxisMetadataStorePort;
                }

                return new RoxisMemoizationDatabaseConfiguration()
                {
                    MetadataClientConfiguration = roxisClientConfiguration,
                };
            }
            else
            {
                return new RocksDbMemoizationStoreConfiguration() {
                    Database = new RocksDbContentLocationDatabaseConfiguration(cacheRoot / "RocksDbMemoizationStore") {
                        CleanOnInitialize = false,
                        GarbageCollectionInterval = TimeSpan.FromSeconds(config.RocksDbMemoizationStoreGarbageCollectionIntervalInSeconds),
                        MetadataGarbageCollectionEnabled = true,
                        MetadataGarbageCollectionMaximumNumberOfEntriesToKeep = config.RocksDbMemoizationStoreGarbageCollectionMaximumNumberOfEntriesToKeep,
                        OnFailureDeleteExistingStoreAndRetry = true,
                        LogsKeepLongTerm = false,
                    },
                };
            }
        }

        private static MemoizationStore.Interfaces.Caches.ICache CreateGrpcCache(Config config, DisposeLogger logger)
        {
            Contract.Requires(config.RetryIntervalSeconds >= 0);
            Contract.Requires(config.RetryCount >= 0);

            var serviceClientRpcConfiguration = new ServiceClientRpcConfiguration() {
                GrpcCoreClientOptions = config.GrpcCoreClientOptions,
            };
            if (config.GrpcPort > 0)
            {
                serviceClientRpcConfiguration.GrpcPort = config.GrpcPort;
            }

            ServiceClientContentStoreConfiguration serviceClientContentStoreConfiguration = null;
            if (config.EnableContentServer) {
                new ServiceClientContentStoreConfiguration(config.CacheName, serviceClientRpcConfiguration, config.ScenarioName)
                {
                    RetryIntervalSeconds = (uint)config.RetryIntervalSeconds,
                    RetryCount = (uint)config.RetryCount,
                    GrpcEnvironmentOptions = config.GrpcEnvironmentOptions,
                };
            }

            if (config.EnableContentServer && config.EnableMetadataServer)
            {
                return LocalCache.CreateRpcCache(logger, serviceClientContentStoreConfiguration);
            }
            else
            {
                Contract.Assert(!config.EnableMetadataServer, "It is not supported to use a Metadata server without a Content server");

                var memoizationStoreConfiguration = GetInProcMemoizationStoreConfiguration(new AbsolutePath(config.CacheRootPath), config, GetCasConfig(config));

                return LocalCache.CreateUnknownContentStoreInProcMemoizationStoreCache(logger,
                    new AbsolutePath(config.CacheRootPath),
                    memoizationStoreConfiguration,
                    new LocalCacheConfiguration(serviceClientContentStoreConfiguration),
                    configurationModel: CreateConfigurationModel(GetCasConfig(config)),
                    clock: null,
                    checkLocalFiles: config.CheckLocalFiles);
            }

        }

        private static LocalCache CreateLocalCacheWithStreamPathCas(Config config, DisposeLogger logger)
        {
            Contract.Requires(config.UseStreamCAS);

            SetDefaultsForStreamCas(config);
            var configCoreForPath = GetCasConfig(config);
            return LocalCache.CreateStreamPathContentStoreInProcMemoizationStoreCache(
                logger,
                new AbsolutePath(config.StreamCAS.CacheRootPath),
                new AbsolutePath(configCoreForPath.CacheRootPath),
                GetInProcMemoizationStoreConfiguration(new AbsolutePath(config.CacheRootPath), config, configCoreForPath),
                CreateConfigurationModel(config.StreamCAS),
                CreateConfigurationModel(configCoreForPath));
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, cacheConfig =>
            {
                var failures = new List<Failure>();
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheId, nameof(cacheConfig.CacheId));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheLogPath, nameof(cacheConfig.CacheLogPath));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheRootPath, nameof(cacheConfig.CacheRootPath));

                if (cacheConfig.UseStreamCAS && string.IsNullOrEmpty(cacheConfig.StreamCAS.CacheRootPath))
                {
                    failures.Add(new IncorrectJsonConfigDataFailure($"If {nameof(cacheConfig.UseStreamCAS)} is enabled, {nameof(cacheConfig.StreamCAS)}.{nameof(cacheConfig.StreamCAS.CacheRootPath)} cannot be null or empty."));
                }

                return failures;
            });
        }
    }
}
