// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
#if MICROSOFT_INTERNAL
using BuildXL.Cache.MemoizationStore.Vsts;
#endif
using BuildXL.Cache.MemoizationStore.Service;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using static BuildXL.Utilities.ConfigurationHelper;

namespace BuildXL.Cache.Host.Service.Internal
{
    /// <summary>
    /// Creates and configures cache server instances.
    /// </summary>
    /// <remarks>Marked as public because it is used externally.</remarks>
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
            if (TryCreateLauncherIfSpecified(cacheConfig, out var launcher))
            {
                return launcher;
            }

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
            var localServerConfiguration = CreateLocalServerConfiguration(cacheConfig.LocalCasSettings.ServiceSettings, serviceConfiguration, distributedSettings);

            // Initialization of the GrpcEnvironment is nasty business: we have a wrapper class around the internal
            // state. The internal state has a flag inside that marks whether it's been initialized or not. If we do
            // any Grpc activity, the internal state will be initialized, and all further attempts to change things
            // will throw. Since we may need to initialize a Grpc client before we do a Grpc server, this means we
            // need to call this early, even if it doesn't have anything to do with what's going on here.
            GrpcEnvironment.Initialize(_logger, localServerConfiguration.GrpcEnvironmentOptions, overwriteSafeOptions: true);

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

        private bool TryCreateLauncherIfSpecified(DistributedCacheServiceConfiguration cacheConfig, out DeploymentLauncher launcher)
        {
            var launcherSettings = cacheConfig.DistributedContentSettings.LauncherSettings;
            if (launcherSettings != null)
            {
                var deploymentParams = launcherSettings.DeploymentParameters;
                deploymentParams.Stamp ??= _arguments.TelemetryFieldsProvider?.Stamp;
                deploymentParams.Machine ??= Environment.MachineName;
                deploymentParams.MachineFunction ??= _arguments.TelemetryFieldsProvider?.APMachineFunction;
                deploymentParams.Ring ??= _arguments.TelemetryFieldsProvider?.Ring;

                deploymentParams.AuthorizationSecret ??= _arguments.Host.GetPlainSecretAsync(deploymentParams.AuthorizationSecretName, _arguments.Cancellation).GetAwaiter().GetResult();

                launcher = new DeploymentLauncher(
                    launcherSettings,
                    _fileSystem);
                return true;
            }
            else
            {
                launcher = null;
                return false;
            }
        }

        private StartupShutdownBase CreateLocalServer(LocalServerConfiguration localServerConfiguration, DistributedContentSettings distributedSettings = null)
        {
            var resolvedCacheSettings = DistributedContentStoreFactory.ResolveCacheSettingsInPrecedenceOrder(_arguments);

            Func<AbsolutePath, IContentStore> contentStoreFactory = path => DistributedContentStoreFactory.CreateLocalContentStore(
                distributedSettings,
                _arguments,
                resolvedCacheSettings.Where(s => s.ResolvedCacheRootPath == path || s.ResolvedCacheRootPath.Path.StartsWith(path.Path, StringComparison.OrdinalIgnoreCase)).Single());

            if (distributedSettings?.EnableMetadataStore == true)
            {
                _logger.Always("Creating local server with content and metadata store");

                var factory = CreateDistributedContentStoreFactory();

                Func<AbsolutePath, ICache> cacheFactory = path =>
                {
                    var distributedCache = new OneLevelCache(
                        contentStoreFunc: () => contentStoreFactory(path),
                        memoizationStoreFunc: () => CreateServerSideLocalMemoizationStore(path, factory),
                        Guid.NewGuid(),
                        passContentToMemoization: true);

                    ICache cacheToReturn = distributedCache;
#if MICROSOFT_INTERNAL
                    if (distributedSettings.EnablePublishingCache)
                    {
                        cacheToReturn = new PublishingCache<OneLevelCache>(
                            local: distributedCache,
                            remote: new BuildCachePublishingStore(contentSource: distributedCache, _fileSystem, distributedSettings.PublishingConcurrencyLimit),
                            Guid.NewGuid());
                    }
#endif

                    return cacheToReturn;
                };

                return new LocalCacheServer(
                    _fileSystem,
                    _logger,
                    _arguments.Configuration.LocalCasSettings.ServiceSettings.ScenarioName,
                    cacheFactory,
                    localServerConfiguration,
                    capabilities: distributedSettings.EnablePublishingCache ? Capabilities.All : Capabilities.AllNonPublishing);
            }
            else
            {
                _logger.Always("Creating local server with content store only");

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

            // NOTE: This relies on the assumption that when creating a distributed server,
            // there is only one call to create a cache so we simply create the cache here and ignore path
            // below in factory delegates since the logic for creating path based caches is included in the
            // call to CreateTopLevelStore
            var topLevelAndPrimaryStore = factory.CreateTopLevelStore();

            if (distributedSettings.EnableMetadataStore || distributedSettings.EnableDistributedCache)
            {
                _logger.Always("Creating distributed server with content and metadata store");

                Func<AbsolutePath, ICache> cacheFactory = path =>
                {
                    if (distributedSettings.EnableDistributedCache)
                    {
                        var distributedCache = new DistributedOneLevelCache(topLevelAndPrimaryStore.topLevelStore,
                            topLevelAndPrimaryStore.primaryDistributedStore,
                            Guid.NewGuid(),
                            passContentToMemoization: true);

                        ICache cacheToReturn = distributedCache;
#if MICROSOFT_INTERNAL
                        if (distributedSettings.EnablePublishingCache)
                        {
                            cacheToReturn = new PublishingCache<DistributedOneLevelCache>(
                                local: distributedCache,
                                remote: new BuildCachePublishingStore(contentSource: distributedCache, _fileSystem, distributedSettings.PublishingConcurrencyLimit),
                                Guid.NewGuid());
                        }
#endif

                        return cacheToReturn;
                    }
                    else
                    {
                        return new OneLevelCache(
                            contentStoreFunc: () => topLevelAndPrimaryStore.topLevelStore,
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
                    localServerConfiguration,
                    capabilities: distributedSettings.EnablePublishingCache ? Capabilities.All : Capabilities.AllNonPublishing);
            }
            else
            {
                _logger.Always("Creating distributed server with content store only");

                return new LocalContentServer(
                    _fileSystem,
                    _logger,
                    cacheConfig.LocalCasSettings.ServiceSettings.ScenarioName,
                    path => topLevelAndPrimaryStore.topLevelStore,
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
            else if (distributedSettings.UseRoxisMetadataStore)
            {
                var config = new RoxisMemoizationDatabaseConfiguration();
                ApplyIfNotNull(distributedSettings.RoxisMetadataStoreHost, v => config.MetadataClientConfiguration.GrpcHost = v);
                ApplyIfNotNull(distributedSettings.RoxisMetadataStorePort, v => config.MetadataClientConfiguration.GrpcPort = v);

                return config.CreateStore(_logger, SystemClock.Instance);
            }
            else
            {
                var config = new RocksDbMemoizationStoreConfiguration()
                {
                    Database = RocksDbContentLocationDatabaseConfiguration.FromDistributedContentSettings(
                        distributedSettings,
                        databasePath: path / "RocksDbMemoizationStore",
                        logsBackupPath: null,
                        logsKeepLongTerm: true),
                };

                config.Database.CleanOnInitialize = false;
                config.Database.OnFailureDeleteExistingStoreAndRetry = true;
                config.Database.MetadataGarbageCollectionEnabled = true;

                return new RocksDbMemoizationStore(_logger, SystemClock.Instance, config);
            }
        }

        private static LocalServerConfiguration CreateLocalServerConfiguration(
            LocalCasServiceSettings localCasServiceSettings,
            ServiceConfiguration serviceConfiguration,
            DistributedContentSettings distributedSettings)
        {
            serviceConfiguration.GrpcPort = localCasServiceSettings.GrpcPort;
            serviceConfiguration.BufferSizeForGrpcCopies = localCasServiceSettings.BufferSizeForGrpcCopies;
            serviceConfiguration.ProactivePushCountLimit = localCasServiceSettings.MaxProactivePushRequestHandlers;
            serviceConfiguration.CopyRequestHandlingCountLimit = localCasServiceSettings.MaxCopyFromHandlers;

            var localContentServerConfiguration = new LocalServerConfiguration(serviceConfiguration);
            
            ApplyIfNotNull(localCasServiceSettings.UnusedSessionTimeoutMinutes, value => localContentServerConfiguration.UnusedSessionTimeout = TimeSpan.FromMinutes(value));
            ApplyIfNotNull(localCasServiceSettings.UnusedSessionHeartbeatTimeoutMinutes, value => localContentServerConfiguration.UnusedSessionHeartbeatTimeout = TimeSpan.FromMinutes(value));
            ApplyIfNotNull(localCasServiceSettings.GrpcCoreServerOptions, value => localContentServerConfiguration.GrpcCoreServerOptions = value);
            ApplyIfNotNull(localCasServiceSettings.GrpcEnvironmentOptions, value => localContentServerConfiguration.GrpcEnvironmentOptions = value);
            ApplyIfNotNull(localCasServiceSettings.DoNotShutdownSessionsInUse, value => localContentServerConfiguration.DoNotShutdownSessionsInUse = value);

            ApplyIfNotNull(distributedSettings?.UseUnsafeByteStringConstruction, value =>
            {
                GrpcExtensions.UnsafeByteStringOptimizations = value;
            });

            ApplyIfNotNull(distributedSettings?.ShutdownEvictionBeforeHibernation, value => localContentServerConfiguration.ShutdownEvictionBeforeHibernation = value);

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

            var result = new ServiceConfiguration(
                namedCacheRoots,
                dataRootPath,
                localCasSettings.ServiceSettings.MaxPipeListeners,
                localCasSettings.ServiceSettings.GracefulShutdownSeconds,
                (int)localCasSettings.ServiceSettings.GrpcPort,
                grpcPortFileName: localCasSettings.ServiceSettings.GrpcPortFileName,
                bufferSizeForGrpcCopies: localCasSettings.ServiceSettings.BufferSizeForGrpcCopies,
                proactivePushCountLimit: localCasSettings.ServiceSettings.MaxProactivePushRequestHandlers,
                logIncrementalStatsInterval: distributedSettings?.LogIncrementalStatsInterval,
                logMachineStatsInterval: distributedSettings?.LogMachineStatsInterval,
                logIncrementalStatsCounterNames: distributedSettings?.IncrementalStatisticsCounterNames);

            ApplyIfNotNull(distributedSettings?.TraceServiceGrpcOperations, v => result.TraceGrpcOperation = v);
            return result;
        }

        private static void WriteContentStoreConfigFile(string cacheSizeQuotaString, AbsolutePath rootPath, IAbsFileSystem fileSystem)
        {
            fileSystem.CreateDirectory(rootPath);

            var maxSizeQuota = new MaxSizeQuota(cacheSizeQuotaString);
            var casConfig = new ContentStoreConfiguration(maxSizeQuota);

            casConfig.Write(fileSystem, rootPath).GetAwaiter().GetResult();
        }
    }
}
