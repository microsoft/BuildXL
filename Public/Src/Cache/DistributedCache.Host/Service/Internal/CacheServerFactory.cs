// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Service;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.Host.Service.Internal
{
    /// <summary>
    /// Creates and configures cache server instances.
    /// </summary>
    internal class CacheServerFactory
    {
        private readonly IAbsFileSystem _fileSystem;
        private readonly ILogger _logger;
        private readonly DistributedCacheServiceArguments _arguments;

        public CacheServerFactory(DistributedCacheServiceArguments arguments)
        {
            _arguments = arguments;
            _logger = arguments.Logger;
            _fileSystem = new PassThroughFileSystem(_logger);
        }

        public (LocalContentServer contentServer, LocalCacheServer cacheServer) Create()
        {
            var cacheConfig = _arguments.Configuration;
            cacheConfig.LocalCasSettings = cacheConfig.LocalCasSettings.FilterUnsupportedNamedCaches(_arguments.HostInfo.Capabilities, _logger);

            var distributedSettings = cacheConfig.DistributedContentSettings;
            var isLocal = distributedSettings == null || !distributedSettings.IsDistributedContentEnabled;

            var serviceConfiguration = CreateServiceConfiguration(_logger, _fileSystem, cacheConfig.LocalCasSettings, new AbsolutePath(_arguments.DataRootPath), isDistributed: !isLocal);
            var localServerConfiguration = CreateLocalServerConfiguration(cacheConfig.LocalCasSettings.ServiceSettings, serviceConfiguration);

            if (isLocal)
            {
                // In practice, we don't really pass in a null distributedSettings. Hence, we'll enable the metadata
                // store whenever its set to true. This can only happen in the Application verb, because the Service
                // verb doesn't change the defaults.
                return CreateLocalServer(localServerConfiguration, distributedSettings);
            }
            else
            {
                return CreateDistributedServer(localServerConfiguration, distributedSettings);
            }
        }

        private (LocalContentServer, LocalCacheServer) CreateLocalServer(LocalServerConfiguration localServerConfiguration, DistributedContentSettings distributedSettings = null)
        {
            Func<AbsolutePath, IContentStore> contentStoreFactory = path => ContentStoreFactory.CreateContentStore(_fileSystem, path, evictionAnnouncer: null, distributedEvictionSettings: default, contentStoreSettings: default, trimBulkAsync: null);

            if (distributedSettings?.EnableMetadataStore == true)
            {
                Func<AbsolutePath, ICache> cacheFactory = path =>
                {
                    var factory = CreateDistributedContentStoreFactory();
                    return new OneLevelCache(
                        contentStoreFunc: () => contentStoreFactory(path),
                        memoizationStoreFunc: () => CreateServerSideLocalMemoizationStore(path, factory),
                        Guid.NewGuid(),
                        passContentToMemoization: true);
                };

                return (null, new LocalCacheServer(
                    _fileSystem,
                    _logger,
                    _arguments.Configuration.LocalCasSettings.ServiceSettings.ScenarioName,
                    cacheFactory,
                    localServerConfiguration));
            }
            else
            {
                return (new LocalContentServer(
                    _fileSystem,
                    _logger,
                    _arguments.Configuration.LocalCasSettings.ServiceSettings.ScenarioName,
                    contentStoreFactory,
                    localServerConfiguration), null);
            }
        }

        private DistributedContentStoreFactory CreateDistributedContentStoreFactory()
        {
            var cacheConfig = _arguments.Configuration;

            var hostInfo = _arguments.HostInfo;
            _logger.Debug($"Creating on stamp id {hostInfo.StampId} with scenario {cacheConfig.LocalCasSettings.ServiceSettings.ScenarioName ?? string.Empty}");

            var secretRetriever = new DistributedCacheSecretRetriever(_arguments);

            return new DistributedContentStoreFactory(
                _arguments,
                cacheConfig.DistributedContentSettings.GetRedisConnectionSecretNames(hostInfo.StampId),
                secretRetriever);
        }

        private (LocalContentServer, LocalCacheServer) CreateDistributedServer(LocalServerConfiguration localServerConfiguration, DistributedContentSettings distributedSettings)
        {
            var cacheConfig = _arguments.Configuration;

            var hostInfo = _arguments.HostInfo;
            _logger.Debug($"Creating on stamp id {hostInfo.StampId} with scenario {cacheConfig.LocalCasSettings.ServiceSettings.ScenarioName ?? string.Empty}");

            var secretRetriever = new DistributedCacheSecretRetriever(_arguments);

            var factory = CreateDistributedContentStoreFactory();

            Func<AbsolutePath, IContentStore> contentStoreFactory = path =>
            {
                var cacheSettingsByCacheName = cacheConfig.LocalCasSettings.CacheSettingsByCacheName;
                var drivesWithContentStore = new Dictionary<string, IContentStore>(StringComparer.OrdinalIgnoreCase);

                foreach (var settings in cacheSettingsByCacheName)
                {
                    _logger.Debug($"Using [{settings.Key}]'s settings: {settings.Value}");

                    var rootPath = cacheConfig.LocalCasSettings.GetCacheRootPathWithScenario(settings.Key);
                    drivesWithContentStore[GetRoot(rootPath)] = factory.CreateContentStore(rootPath);
                }

                return new MultiplexedContentStore(drivesWithContentStore, cacheConfig.LocalCasSettings.PreferredCacheDrive);
            };

            if (distributedSettings.EnableMetadataStore)
            {
                Func<AbsolutePath, ICache> cacheFactory = path =>
                {
                    return new OneLevelCache(
                        contentStoreFunc: () => contentStoreFactory(path),
                        memoizationStoreFunc: () => CreateServerSideLocalMemoizationStore(path, factory),
                        Guid.NewGuid(),
                        passContentToMemoization: true);
                };

                // NOTE(jubayard): When generating the service configuration, we create a single named cache root in
                // the distributed case. This means that the factories will be called exactly once, so we will have
                // a single MultiplexedContentStore and MemoizationStore. The latter will be located in the last cache
                // root listed as per production configuration, which currently (8/27/2019) points to the SSD drives.
                return (null, new LocalCacheServer(
                    _fileSystem,
                    _logger,
                    _arguments.Configuration.LocalCasSettings.ServiceSettings.ScenarioName,
                    cacheFactory,
                    localServerConfiguration));
            }
            else
            {
                return (new LocalContentServer(
                    _fileSystem,
                    _logger,
                    cacheConfig.LocalCasSettings.ServiceSettings.ScenarioName,
                    contentStoreFactory,
                    localServerConfiguration), null);
            }
        }

        private IMemoizationStore CreateServerSideLocalMemoizationStore(AbsolutePath path, DistributedContentStoreFactory factory = null)
        {
            var distributedSettings = _arguments.Configuration.DistributedContentSettings;

            if (distributedSettings.UseRedisMetadataStore)
            {
                return factory.CreateMemoizationStoreAsync(path).GetAwaiter().GetResult();
            }
            else
            {
                var config = new RocksDbMemoizationStoreConfiguration()
                {
                    Database = new RocksDbContentLocationDatabaseConfiguration(path / "RocksDbMemoizationStore")
                    {
                        MetadataGarbageCollectionEnabled = true,
                    },
                };

                if (distributedSettings != null)
                {
                    config.Database.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep = distributedSettings.MaximumNumberOfMetadataEntriesToStore;
                }

                return new RocksDbMemoizationStore(_logger, SystemClock.Instance, config);
            }
        }

        private static LocalServerConfiguration CreateLocalServerConfiguration(LocalCasServiceSettings localCasServiceSettings, ServiceConfiguration serviceConfiguration)
        {
            serviceConfiguration.GrpcPort = localCasServiceSettings.GrpcPort;
            serviceConfiguration.BufferSizeForGrpcCopies = localCasServiceSettings.BufferSizeForGrpcCopies;
            serviceConfiguration.GzipBarrierSizeForGrpcCopies = localCasServiceSettings.GzipBarrierSizeForGrpcCopies;

            var localContentServerConfiguration = new LocalServerConfiguration(serviceConfiguration);

            if (localCasServiceSettings.UnusedSessionTimeoutMinutes.HasValue)
            {
                localContentServerConfiguration.UnusedSessionTimeout = TimeSpan.FromMinutes(localCasServiceSettings.UnusedSessionTimeoutMinutes.Value);
            }

            if (localCasServiceSettings.UnusedSessionHeartbeatTimeoutMinutes.HasValue)
            {
                localContentServerConfiguration.UnusedSessionHeartbeatTimeout = TimeSpan.FromMinutes(localCasServiceSettings.UnusedSessionHeartbeatTimeoutMinutes.Value);
            }

            return localContentServerConfiguration;
        }

        private static ServiceConfiguration CreateServiceConfiguration(ILogger logger, IAbsFileSystem fileSystem, LocalCasSettings localCasSettings, AbsolutePath dataRootPath, bool isDistributed)
        {
            var namedCacheRoots = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, NamedCacheSettings> settings in localCasSettings.CacheSettingsByCacheName)
            {
                var rootPath = localCasSettings.GetCacheRootPathWithScenario(settings.Key);

                logger.Debug($"Writing content store config file at {rootPath}.");
                WriteContentStoreConfigFile(settings.Value.CacheSizeQuotaString, rootPath, fileSystem);

                if (!isDistributed)
                {
                    namedCacheRoots[settings.Key] = rootPath;
                }
                else
                {
                    // Arbitrary set to match ServiceConfiguration and LocalContentServer pattern
                    namedCacheRoots[localCasSettings.CasClientSettings.DefaultCacheName] = rootPath;
                }
            }

            if (!namedCacheRoots.Keys.Any(name => localCasSettings.CasClientSettings.DefaultCacheName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"Must have the default cache name {localCasSettings.CasClientSettings.DefaultCacheName} as one of the named cache roots.");
            }

            return new ServiceConfiguration(
                namedCacheRoots,
                dataRootPath,
                localCasSettings.ServiceSettings.MaxPipeListeners,
                localCasSettings.ServiceSettings.GracefulShutdownSeconds,
                (int)localCasSettings.ServiceSettings.GrpcPort,
                grpcPortFileName: localCasSettings.ServiceSettings.GrpcPortFileName,
                bufferSizeForGrpcCopies: localCasSettings.ServiceSettings.BufferSizeForGrpcCopies,
                gzipBarrierSizeForGrpcCopies: localCasSettings.ServiceSettings.GzipBarrierSizeForGrpcCopies);
        }

        private static void WriteContentStoreConfigFile(string cacheSizeQuotaString, AbsolutePath rootPath, IAbsFileSystem fileSystem)
        {
            fileSystem.CreateDirectory(rootPath);

            var maxSizeQuota = new MaxSizeQuota(cacheSizeQuotaString);
            var casConfig = new ContentStoreConfiguration(maxSizeQuota);

            casConfig.Write(fileSystem, rootPath).GetAwaiter().GetResult();
        }

        private static string GetRoot(AbsolutePath rootPath)
        {
            return Path.GetPathRoot(rootPath.Path);
        }
    }
}
