// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Service;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using static BuildXL.Cache.Host.Service.Internal.ConfigurationHelper;

namespace BuildXL.Cache.Host.Service.Internal
{
    /// <summary>
    /// Creates and configures cache server instances.
    /// </summary>
    /// <remarks>Marked as public because it is used externally.<remarks/>
    public class CacheServerFactory
    {
        private readonly IAbsFileSystem _fileSystem;
        private readonly ILogger _logger;
        private readonly DistributedCacheServiceArguments _arguments;

        public CacheServerFactory(DistributedCacheServiceArguments arguments)
        {
            _arguments = arguments;
            _logger = arguments.Logger;

            // Enable POSIX delete to ensure that files are removed even when there are open handles
            PassThroughFileSystem.EnablePosixDelete();
            _fileSystem = new PassThroughFileSystem(_logger);
        }

        public StartupShutdownBase Create()
        {
            var cacheConfig = _arguments.Configuration;
            cacheConfig.LocalCasSettings = cacheConfig.LocalCasSettings.FilterUnsupportedNamedCaches(_arguments.HostInfo.Capabilities, _logger);

            var distributedSettings = cacheConfig.DistributedContentSettings;
            var isLocal = distributedSettings == null || !distributedSettings.IsDistributedContentEnabled;

            var serviceConfiguration = CreateServiceConfiguration(
                _logger,
                _fileSystem,
                cacheConfig.LocalCasSettings,
                distributedSettings,
                new AbsolutePath(_arguments.DataRootPath),
                isDistributed: !isLocal);
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

        private StartupShutdownBase CreateLocalServer(LocalServerConfiguration localServerConfiguration, DistributedContentSettings distributedSettings = null)
        {
            Func<AbsolutePath, IContentStore> contentStoreFactory = path => ContentStoreFactory.CreateContentStore(_fileSystem, path, evictionAnnouncer: null, distributedEvictionSettings: default, contentStoreSettings: default, trimBulkAsync: null);

            if (distributedSettings?.EnableMetadataStore == true)
            {
                var factory = CreateDistributedContentStoreFactory();

                Func<AbsolutePath, ICache> cacheFactory = path =>
                {
                    return new OneLevelCache(
                        contentStoreFunc: () => contentStoreFactory(path),
                        memoizationStoreFunc: () => CreateServerSideLocalMemoizationStore(path, factory),
                        Guid.NewGuid(),
                        passContentToMemoization: true);
                };

                return new LocalCacheServer(
                    _fileSystem,
                    _logger,
                    _arguments.Configuration.LocalCasSettings.ServiceSettings.ScenarioName,
                    cacheFactory,
                    localServerConfiguration);
            }
            else
            {
                return new LocalContentServer(
                    _fileSystem,
                    _logger,
                    _arguments.Configuration.LocalCasSettings.ServiceSettings.ScenarioName,
                    contentStoreFactory,
                    localServerConfiguration);
            }
        }

        private DistributedContentStoreFactory CreateDistributedContentStoreFactory()
        {
            var cacheConfig = _arguments.Configuration;
            var hostInfo = _arguments.HostInfo;
            _logger.Debug($"Creating on stamp id {hostInfo.StampId} with scenario {cacheConfig.LocalCasSettings.ServiceSettings.ScenarioName ?? string.Empty}");

            return new DistributedContentStoreFactory(_arguments);
        }

        private StartupShutdownBase CreateDistributedServer(LocalServerConfiguration localServerConfiguration, DistributedContentSettings distributedSettings)
        {
            var cacheConfig = _arguments.Configuration;
            var factory = CreateDistributedContentStoreFactory();

            Func<AbsolutePath, MultiplexedContentStore> contentStoreFactory = path =>
            {
                var drivesWithContentStore = new Dictionary<string, IContentStore>(StringComparer.OrdinalIgnoreCase);

                foreach (var resolvedCacheSettings in factory.OrderedResolvedCacheSettings)
                {
                    _logger.Debug($"Using [{resolvedCacheSettings.Settings.CacheRootPath}]'s settings: {resolvedCacheSettings.Settings}");

                    drivesWithContentStore[resolvedCacheSettings.Drive] = factory.CreateContentStore(resolvedCacheSettings);
                }

                return new MultiplexedContentStore(drivesWithContentStore, cacheConfig.LocalCasSettings.PreferredCacheDrive);
            };

            if (distributedSettings.EnableMetadataStore || distributedSettings.EnableDistributedCache)
            {
                Func<AbsolutePath, ICache> cacheFactory = path =>
                {
                    if (distributedSettings.EnableDistributedCache)
                    {
                        var contentStore = contentStoreFactory(path);
                        return new DistributedOneLevelCache(contentStore,
                            (DistributedContentStore<AbsolutePath>)contentStore.PreferredContentStore,
                            Guid.NewGuid(),
                            passContentToMemoization: true);
                    }
                    else
                    {
                        return new OneLevelCache(
                            contentStoreFunc: () => contentStoreFactory(path),
                            memoizationStoreFunc: () => CreateServerSideLocalMemoizationStore(path, factory),
                            Guid.NewGuid(),
                            passContentToMemoization: true);
                    }
                };

                // NOTE(jubayard): When generating the service configuration, we create a single named cache root in
                // the distributed case. This means that the factories will be called exactly once, so we will have
                // a single MultiplexedContentStore and MemoizationStore. The latter will be located in the last cache
                // root listed as per production configuration, which currently (8/27/2019) points to the SSD drives.
                return new LocalCacheServer(
                    _fileSystem,
                    _logger,
                    _arguments.Configuration.LocalCasSettings.ServiceSettings.ScenarioName,
                    cacheFactory,
                    localServerConfiguration);
            }
            else
            {
                return new LocalContentServer(
                    _fileSystem,
                    _logger,
                    cacheConfig.LocalCasSettings.ServiceSettings.ScenarioName,
                    contentStoreFactory,
                    localServerConfiguration);
            }
        }

        private IMemoizationStore CreateServerSideLocalMemoizationStore(AbsolutePath path, DistributedContentStoreFactory factory)
        {
            var distributedSettings = _arguments.Configuration.DistributedContentSettings;

            if (distributedSettings.UseRedisMetadataStore)
            {
                return factory.CreateMemoizationStoreAsync().GetAwaiter().GetResult();
            }
            else
            {
                var config = new RocksDbMemoizationStoreConfiguration()
                {
                    Database = new RocksDbContentLocationDatabaseConfiguration(path / "RocksDbMemoizationStore")
                    {
                        MetadataGarbageCollectionEnabled = true,
                        OnFailureDeleteExistingStoreAndRetry = true,
                        LogsKeepLongTerm = true,
                    },
                };

                config.Database.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep = distributedSettings.MaximumNumberOfMetadataEntriesToStore;

                return new RocksDbMemoizationStore(_logger, SystemClock.Instance, config);
            }
        }

        private static LocalServerConfiguration CreateLocalServerConfiguration(LocalCasServiceSettings localCasServiceSettings, ServiceConfiguration serviceConfiguration)
        {
            serviceConfiguration.GrpcPort = localCasServiceSettings.GrpcPort;
            serviceConfiguration.BufferSizeForGrpcCopies = localCasServiceSettings.BufferSizeForGrpcCopies;
            serviceConfiguration.GzipBarrierSizeForGrpcCopies = localCasServiceSettings.GzipBarrierSizeForGrpcCopies;
            serviceConfiguration.ProactivePushCountLimit = localCasServiceSettings.MaxProactivePushRequestHandlers;

            var localContentServerConfiguration = new LocalServerConfiguration(serviceConfiguration);
            ApplyIfNotNull(localCasServiceSettings.UnusedSessionTimeoutMinutes, value => localContentServerConfiguration.UnusedSessionTimeout = TimeSpan.FromMinutes(value));
            ApplyIfNotNull(localCasServiceSettings.UnusedSessionHeartbeatTimeoutMinutes, value => localContentServerConfiguration.UnusedSessionHeartbeatTimeout = TimeSpan.FromMinutes(value));
            ApplyIfNotNull(localCasServiceSettings.GrpcThreadPoolSize, value => localContentServerConfiguration.GrpcThreadPoolSize = value);

            return localContentServerConfiguration;
        }

        private static ServiceConfiguration CreateServiceConfiguration(
            ILogger logger,
            IAbsFileSystem fileSystem,
            LocalCasSettings localCasSettings,
            DistributedContentSettings distributedSettings,
            AbsolutePath dataRootPath,
            bool isDistributed)
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
                gzipBarrierSizeForGrpcCopies: localCasSettings.ServiceSettings.GzipBarrierSizeForGrpcCopies,
                proactivePushCountLimit: localCasSettings.ServiceSettings.MaxProactivePushRequestHandlers,
                logIncrementalStatsInterval: distributedSettings?.LogIncrementalStatsInterval,
                logMachineStatsInterval: distributedSettings?.LogMachineStatsInterval);
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
