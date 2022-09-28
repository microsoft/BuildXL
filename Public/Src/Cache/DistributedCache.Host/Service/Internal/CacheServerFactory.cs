// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
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
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Service.OutOfProc;
using BuildXL.Utilities.ConfigurationHelpers;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using BuildXL.Cache.ContentStore.Distributed.Blobs;

namespace BuildXL.Cache.Host.Service.Internal
{
    /// <summary>
    /// Creates and configures cache server instances.
    /// </summary>
    /// <remarks>Marked as public because it is used externally.</remarks>
    public class CacheServerFactory
    {
        private static readonly Tracer _tracer = new Tracer(nameof(CacheServerFactory));
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

        /// <summary>
        /// Creates a cache server.
        /// </summary>
        /// <remarks>
        /// Currently it can be one of the following:
        /// * Launcher that will download configured bits and start them.
        /// * Out-of-proc launcher that will start the current bits in a separate process.
        /// * In-proc distributed cache service.
        /// * In-proc local cache service.
        /// </remarks>
        public async Task<StartupShutdownBase> CreateAsync(OperationContext operationContext)
        {
            var cacheConfig = _arguments.Configuration;

            if (IsLauncherEnabled(cacheConfig))
            {
                _tracer.Debug(operationContext, $"Creating a launcher.");
                return await CreateLauncherAsync(cacheConfig);
            }

            var distributedSettings = cacheConfig.DistributedContentSettings;

            if (IsOutOfProcCacheEnabled(cacheConfig))
            {
                _tracer.Debug(operationContext, $"Creating an out-of-proc cache service.");
                var outOfProcCache = await CacheServiceWrapper.CreateAsync(
                    _arguments,
                    DeploymentLauncherHost.Instance,
                    static (context, reason) => LifetimeManager.RequestTeardown(context, reason));

                if (outOfProcCache.Succeeded)
                {
                    return outOfProcCache.Value;
                }

                // Tracing and falling back to the in-proc cache
                _tracer.Error(operationContext, $"Failed to create out of proc cache: {outOfProcCache}. Using in-proc cache instead.");
            }

            _tracer.Debug(operationContext, "Creating an in-proc cache service.");
            cacheConfig.LocalCasSettings = cacheConfig.LocalCasSettings.FilterUnsupportedNamedCaches(_arguments.HostInfo.Capabilities, _logger);
            
            var isLocal = distributedSettings == null || !distributedSettings.IsDistributedContentEnabled;

            if (distributedSettings is not null)
            {
                LogManager.Update(distributedSettings.LogManager);
            }

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

        private bool IsLauncherEnabled(DistributedCacheServiceConfiguration cacheConfig) =>
            cacheConfig.DistributedContentSettings.LauncherSettings != null;

        private bool IsOutOfProcCacheEnabled(DistributedCacheServiceConfiguration cacheConfig) =>
            cacheConfig.DistributedContentSettings.RunCacheOutOfProc == true;

        private async Task<DeploymentLauncher> CreateLauncherAsync(DistributedCacheServiceConfiguration cacheConfig)
        {
            var launcherSettings = cacheConfig.DistributedContentSettings.LauncherSettings;
            Contract.Assert(launcherSettings is not null);

            var deploymentParams = launcherSettings.DeploymentParameters;
            deploymentParams.ApplyFromTelemetryProviderIfNeeded(_arguments.TelemetryFieldsProvider);
            deploymentParams.AuthorizationSecret ??= await _arguments.Host.GetPlainSecretAsync(deploymentParams.AuthorizationSecretName, _arguments.Cancellation);

            return new DeploymentLauncher(launcherSettings, _fileSystem);
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
                        cacheToReturn = CreateL3AsyncPublishingCache(distributedSettings, distributedCache);
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

#if MICROSOFT_INTERNAL
        private ICache CreateL3AsyncPublishingCache<T>(DistributedContentSettings distributedSettings, T localCache)
            where T : ICache, IContentStore, IStreamStore, IRepairStore, ICopyRequestHandler, IPushFileHandler
        {
            var publishingStores = new IPublishingStore[] {
                            new BuildCachePublishingStore(
                                    contentSource: localCache,
                                    _fileSystem,
                                    distributedSettings.PublishingConcurrencyLimit),
                            new AzureBlobStoragePublishingStore(localCache)
                        };

            return new PublishingCache<T>(
                local: localCache,
                publishingStores: publishingStores,
                Guid.NewGuid());
        }
#endif

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

            IColdStorage coldStorage = topLevelAndPrimaryStore.primaryDistributedStore.ColdStorage;

            if (distributedSettings.EnableMetadataStore || distributedSettings.EnableDistributedCache)
            {
                _logger.Always("Creating distributed server with content and metadata store");

                Func<AbsolutePath, ICache> cacheFactory = path =>
                {
                    if (distributedSettings.EnableDistributedCache)
                    {
                        var distributedCache = new DistributedOneLevelCache(topLevelAndPrimaryStore.topLevelStore,
                            factory.Services,
                            Guid.NewGuid(),
                            passContentToMemoization: true);

                        ICache cacheToReturn = distributedCache;

#if MICROSOFT_INTERNAL
                        if (distributedSettings.EnablePublishingCache)
                        {
                            cacheToReturn = CreateL3AsyncPublishingCache(distributedSettings, distributedCache);
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
                    capabilities: distributedSettings.EnablePublishingCache ? Capabilities.All : Capabilities.AllNonPublishing,
                    factory.GetAdditionalEndpoints(),
                    coldStorage);
            }
            else
            {
                _logger.Always("Creating distributed server with content store only");

                return new LocalContentServer(
                    _fileSystem,
                    _logger,
                    cacheConfig.LocalCasSettings.ServiceSettings.ScenarioName,
                    path => topLevelAndPrimaryStore.topLevelStore,
                    localServerConfiguration,
                    factory.GetAdditionalEndpoints(),
                    coldStorage);
            }
        }

        private IMemoizationStore CreateServerSideLocalMemoizationStore(AbsolutePath path, DistributedContentStoreFactory factory)
        {
            var distributedSettings = _arguments.Configuration.DistributedContentSettings;

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

            return new RocksDbMemoizationStore(SystemClock.Instance, config);
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

            // Need to disable the Grpc server when asp.net core gprc server is used.
            localContentServerConfiguration.DisableGrpcServer = distributedSettings.EnableAspNetCoreGrpc;

            localCasServiceSettings.UnusedSessionTimeoutMinutes.ApplyIfNotNull(value => localContentServerConfiguration.UnusedSessionTimeout = TimeSpan.FromMinutes(value));
            localCasServiceSettings.UnusedSessionHeartbeatTimeoutMinutes.ApplyIfNotNull(value => localContentServerConfiguration.UnusedSessionHeartbeatTimeout = TimeSpan.FromMinutes(value));
            localCasServiceSettings.GrpcCoreServerOptions.ApplyIfNotNull(value => localContentServerConfiguration.GrpcCoreServerOptions = value);
            localCasServiceSettings.GrpcEnvironmentOptions.ApplyIfNotNull(value => localContentServerConfiguration.GrpcEnvironmentOptions = value);
            localCasServiceSettings.DoNotShutdownSessionsInUse.ApplyIfNotNull(value => localContentServerConfiguration.DoNotShutdownSessionsInUse = value);

            (distributedSettings?.UseUnsafeByteStringConstruction).ApplyIfNotNull(
                value =>
            {
                GrpcExtensions.UnsafeByteStringOptimizations = value;
            });

            (distributedSettings?.ShutdownEvictionBeforeHibernation).ApplyIfNotNull(value => localContentServerConfiguration.ShutdownEvictionBeforeHibernation = value);

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
                localCasSettings.ServiceSettings.GracefulShutdownSeconds,
                (int)localCasSettings.ServiceSettings.GrpcPort,
                grpcPortFileName: localCasSettings.ServiceSettings.GrpcPortFileName,
                bufferSizeForGrpcCopies: localCasSettings.ServiceSettings.BufferSizeForGrpcCopies,
                proactivePushCountLimit: localCasSettings.ServiceSettings.MaxProactivePushRequestHandlers,
                logIncrementalStatsInterval: distributedSettings?.LogIncrementalStatsInterval,
                logMachineStatsInterval: distributedSettings?.LogMachineStatsInterval,
                logIncrementalStatsCounterNames: distributedSettings?.IncrementalStatisticsCounterNames,
                asyncSessionShutdownTimeout: distributedSettings?.AsyncSessionShutdownTimeout);

            distributedSettings?.TraceServiceGrpcOperations.ApplyIfNotNull(v => result.TraceGrpcOperation = v);
            return result;
        }

        private static void WriteContentStoreConfigFile(string cacheSizeQuotaString, AbsolutePath rootPath, IAbsFileSystem fileSystem)
        {
            fileSystem.CreateDirectory(rootPath);

            var maxSizeQuota = new MaxSizeQuota(cacheSizeQuotaString);
            var casConfig = new ContentStoreConfiguration(maxSizeQuota);

            casConfig.Write(fileSystem, rootPath);
        }
    }
}
