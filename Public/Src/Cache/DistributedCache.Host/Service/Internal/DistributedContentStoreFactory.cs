// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling;
using ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Utilities.Tracing;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Distributed.Services;

using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using BandwidthConfiguration = BuildXL.Cache.ContentStore.Distributed.BandwidthConfiguration;
using static BuildXL.Utilities.ConfigurationHelper;

namespace BuildXL.Cache.Host.Service.Internal
{
    public sealed class DistributedContentStoreFactory : IDistributedServicesSecrets
    {
        /// <summary>
        /// Salt to determine keyspace's current version
        /// </summary>
        public const string RedisKeySpaceSalt = "V4";

        private readonly string _keySpace;
        private readonly IAbsFileSystem _fileSystem;
        private readonly DistributedCacheSecretRetriever _secretRetriever;
        private readonly ILogger _logger;

        private readonly DistributedContentSettings _distributedSettings;
        private readonly DistributedCacheServiceArguments _arguments;
        private readonly DistributedContentCopier _copier;

        /// <summary>
        /// This uses a Lazy because not having it breaks the local service use-case (i.e. running ContentStoreApp
        /// with distributed service)
        /// </summary>
        private readonly DistributedContentStoreSettings _distributedContentStoreSettings;

        public IReadOnlyList<ResolvedNamedCacheSettings> OrderedResolvedCacheSettings => _orderedResolvedCacheSettings;
        private readonly List<ResolvedNamedCacheSettings> _orderedResolvedCacheSettings;
        private readonly RetrievedSecrets _secrets;

        public RedisContentLocationStoreConfiguration RedisContentLocationStoreConfiguration { get; }

        public DistributedContentStoreServices Services { get; }

        public DistributedContentStoreFactory(DistributedCacheServiceArguments arguments)
        {
            _logger = arguments.Logger;
            _arguments = arguments;
            _distributedSettings = arguments.Configuration.DistributedContentSettings;

            if (_distributedSettings.PreventRedisUsage)
            {
                _distributedSettings.DisableRedis();
            }

            _keySpace = string.IsNullOrWhiteSpace(_arguments.Keyspace) ? RedisContentLocationStoreConstants.DefaultKeySpace : _arguments.Keyspace;
            _fileSystem = arguments.FileSystem;
            _secretRetriever = new DistributedCacheSecretRetriever(arguments);

            var secretsResult = _secretRetriever.TryRetrieveSecretsAsync().GetAwaiter().GetResult();
            if (!secretsResult.Succeeded)
            {
                _logger.Error($"Unable to retrieve secrets. {secretsResult}");
                _secrets = new RetrievedSecrets(new Dictionary<string, Secret>());
            }

            _secrets = secretsResult.Value;

            _orderedResolvedCacheSettings = ResolveCacheSettingsInPrecedenceOrder(arguments);
            Contract.Assert(_orderedResolvedCacheSettings.Count != 0);

            RedisContentLocationStoreConfiguration = CreateRedisConfiguration();
            _distributedContentStoreSettings = CreateDistributedStoreSettings(_arguments, RedisContentLocationStoreConfiguration);

            // Tracing configuration before creating anything.
            TraceConfiguration();

            _copier = new DistributedContentCopier(
                _distributedContentStoreSettings,
                _fileSystem,
                fileCopier: _arguments.Copier,
                copyRequester: _arguments.CopyRequester,
                _arguments.Overrides.Clock,
                _logger
            );

            var primaryCacheRoot = OrderedResolvedCacheSettings[0].ResolvedCacheRootPath;

            var connectionPool = new GrpcConnectionPool(new ConnectionPoolConfiguration()
            {
                DefaultPort = (int)_arguments.Configuration.LocalCasSettings.ServiceSettings.GrpcPort,
                ConnectTimeout = _distributedSettings.ContentMetadataClientConnectionTimeout,
            });

            var serviceArguments = new DistributedContentStoreServicesArguments
            (
                ConnectionPool: connectionPool,
                DistributedContentSettings: _distributedSettings,
                RedisContentLocationStoreConfiguration: RedisContentLocationStoreConfiguration,
                Overrides: arguments.Overrides,
                Secrets: this,
                PrimaryCacheRoot: primaryCacheRoot,
                FileSystem: arguments.FileSystem,
                DistributedContentCopier: _copier
            );

            Services = new DistributedContentStoreServices(serviceArguments);
        }

        private void TraceConfiguration()
        {
            ConfigurationPrinter.TraceConfiguration(_distributedContentStoreSettings, _logger);
            ConfigurationPrinter.TraceConfiguration(RedisContentLocationStoreConfiguration, _logger);
            ConfigurationPrinter.TraceConfiguration(_arguments.LoggingSettings, _logger);
            ConfigurationPrinter.TraceConfiguration(_arguments.Configuration.LocalCasSettings, _logger);
        }

        internal static List<ResolvedNamedCacheSettings> ResolveCacheSettingsInPrecedenceOrder(DistributedCacheServiceArguments arguments)
        {
            var localCasSettings = arguments.Configuration.LocalCasSettings;
            var cacheSettingsByName = localCasSettings.CacheSettingsByCacheName;

            Dictionary<string, string> cacheNamesByDrive =
                cacheSettingsByName.ToDictionary(x => new AbsolutePath(x.Value.CacheRootPath).GetPathRoot(), x => x.Key, StringComparer.OrdinalIgnoreCase);

            var result = new List<ResolvedNamedCacheSettings>();

            void addCacheByName(string cacheName)
            {
                var cacheSettings = cacheSettingsByName[cacheName];
                var resolvedCacheRootPath = localCasSettings.GetCacheRootPathWithScenario(cacheName);
                result.Add(
                    new ResolvedNamedCacheSettings(
                        name: cacheName,
                        settings: cacheSettings,
                        resolvedCacheRootPath: resolvedCacheRootPath,
                        machineLocation: arguments.Copier.GetLocalMachineLocation(resolvedCacheRootPath)));
            }

            // Add caches specified in drive preference order
            foreach (var drive in localCasSettings.DrivePreferenceOrder)
            {
                if (cacheNamesByDrive.TryGetValue(drive, out var cacheName))
                {
                    addCacheByName(cacheName);
                    cacheNamesByDrive.Remove(drive);
                }
            }

            // Add remaining caches
            foreach (var cacheName in cacheNamesByDrive.Values)
            {
                addCacheByName(cacheName);
            }

            return result;
        }

        private RedisContentLocationStoreConfiguration CreateRedisConfiguration()
        {
            var primaryCacheRoot = OrderedResolvedCacheSettings[0].ResolvedCacheRootPath;

            var redisConfig = new RedisContentLocationStoreConfiguration
            {
                PreventRedisUsage = _distributedSettings.PreventRedisUsage,
                Keyspace = _keySpace + RedisKeySpaceSalt,
                LogReconciliationHashes = _distributedSettings.LogReconciliationHashes,
                RedisBatchPageSize = _distributedSettings.RedisBatchPageSize,
                BlobExpiryTime = TimeSpan.FromMinutes(_distributedSettings.BlobExpiryTimeMinutes),
                MaxBlobCapacity = _distributedSettings.MaxBlobCapacity,
                MaxBlobSize = _distributedSettings.MaxBlobSize,
                UseFullEvictionSort = _distributedSettings.UseFullEvictionSort,
                EvictionWindowSize = _distributedSettings.EvictionWindowSize,
                EvictionPoolSize = _distributedSettings.EvictionPoolSize,
                UpdateStaleLocalLastAccessTimes = _distributedSettings.UpdateStaleLocalLastAccessTimes,
                EvictionRemovalFraction = _distributedSettings.EvictionRemovalFraction,
                EvictionDiscardFraction = _distributedSettings.EvictionDiscardFraction,
                UseTieredDistributedEviction = _distributedSettings.UseTieredDistributedEviction,
                Memoization =
                {
                    ExpiryTime = TimeSpan.FromMinutes(_distributedSettings.RedisMemoizationExpiryTimeMinutes),
                },
                ProactiveCopyLocationsThreshold = _distributedSettings.ProactiveCopyLocationsThreshold,
                UseBinManager = _distributedSettings.UseBinManager || _distributedSettings.ProactiveCopyUsePreferredLocations,
                PreferredLocationsExpiryTime = TimeSpan.FromMinutes(_distributedSettings.PreferredLocationsExpiryTimeMinutes),
                PrimaryMachineLocation = OrderedResolvedCacheSettings[0].MachineLocation,
                MachineListPrioritizeDesignatedLocations = _distributedSettings.PrioritizeDesignatedLocationsOnCopies,
                MachineListDeprioritizeMaster = _distributedSettings.DeprioritizeMasterOnCopies,
                TouchContentHashLists = _distributedSettings.TouchContentHashLists,
                UseMemoizationContentMetadataStore = _distributedSettings.UseMemoizationContentMetadataStore,
            };

            ApplyIfNotNull(_distributedSettings.BlobContentMetadataStoreModeOverride, v => redisConfig.BlobContentMetadataStoreModeOverride = v.Value);
            ApplyIfNotNull(_distributedSettings.LocationContentMetadataStoreModeOverride, v => redisConfig.LocationContentMetadataStoreModeOverride = v.Value);
            ApplyIfNotNull(_distributedSettings.MemoizationContentMetadataStoreModeOverride, v => redisConfig.MemoizationContentMetadataStoreModeOverride = v.Value);

            ApplyIfNotNull(_distributedSettings.BlobOperationLimitCount, v => redisConfig.BlobOperationLimitCount = v);
            ApplyIfNotNull(_distributedSettings.BlobOperationLimitSpanSeconds, v => redisConfig.BlobOperationLimitSpan = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(_distributedSettings.UseSeparateConnectionForRedisBlobs, v => redisConfig.UseSeparateConnectionForRedisBlobs = v);

            // Stop passing additional stores when fully transitioned to unified mode
            // since all drives appear under the same machine id
            if (_distributedSettings.GetMultiplexMode() != MultiplexMode.Unified)
            {
                redisConfig.AdditionalMachineLocations = OrderedResolvedCacheSettings.Skip(1).Select(r => r.MachineLocation).ToArray();
            }

            ApplyIfNotNull(_distributedSettings.ThrottledEvictionIntervalMinutes, v => redisConfig.ThrottledEvictionInterval = TimeSpan.FromMinutes(v));

            // Redis-related configuration.
            ApplyIfNotNull(_distributedSettings.RedisConnectionErrorLimit, v => redisConfig.RedisConnectionErrorLimit = v);
            ApplyIfNotNull(_distributedSettings.RedisReconnectionLimitBeforeServiceRestart, v => redisConfig.RedisReconnectionLimitBeforeServiceRestart = v);
            ApplyIfNotNull(_distributedSettings.DefaultRedisOperationTimeoutInSeconds, v => redisConfig.OperationTimeout = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(_distributedSettings.MinRedisReconnectInterval, v => redisConfig.MinRedisReconnectInterval = v);
            ApplyIfNotNull(_distributedSettings.CancelBatchWhenMultiplexerIsClosed, v => redisConfig.CancelBatchWhenMultiplexerIsClosed = v);

            if (_distributedSettings.RedisExponentialBackoffRetryCount != null)
            {
                var exponentialBackoffConfiguration = new ExponentialBackoffConfiguration(
                    _distributedSettings.RedisExponentialBackoffRetryCount.Value,
                    minBackoff: IfNotNull(_distributedSettings.RedisExponentialBackoffMinIntervalInSeconds, v => TimeSpan.FromSeconds(v)),
                    maxBackoff: IfNotNull(_distributedSettings.RedisExponentialBackoffMaxIntervalInSeconds, v => TimeSpan.FromSeconds(v)),
                    deltaBackoff: IfNotNull(_distributedSettings.RedisExponentialBackoffDeltaIntervalInSeconds, v => TimeSpan.FromSeconds(v))
                    );
                redisConfig.ExponentialBackoffConfiguration = exponentialBackoffConfiguration;
            }

            ApplyIfNotNull(_distributedSettings.ReplicaCreditInMinutes, v => redisConfig.ContentLifetime = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MachineRisk, v => redisConfig.MachineRisk = v);
            ApplyIfNotNull(_distributedSettings.LocationEntryExpiryMinutes, v => redisConfig.LocationEntryExpiry = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MachineStateRecomputeIntervalMinutes, v => redisConfig.MachineStateRecomputeInterval = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MachineActiveToClosedIntervalMinutes, v => redisConfig.MachineActiveToClosedInterval = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MachineActiveToExpiredIntervalMinutes, v => redisConfig.MachineActiveToExpiredInterval = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.TouchFrequencyMinutes, v => redisConfig.TouchFrequency = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.ReconcileCacheLifetimeMinutes, v => redisConfig.ReconcileCacheLifetime = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MaxProcessingDelayToReconcileMinutes, v => redisConfig.MaxProcessingDelayToReconcile = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.EvictionMinAgeMinutes, v => redisConfig.EvictionMinAge = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.RetryWindowSeconds, v => redisConfig.RetryWindow = TimeSpan.FromSeconds(v));

            ApplyIfNotNull(_distributedSettings.Unsafe_MasterThroughputCheckMode, v => redisConfig.MasterThroughputCheckMode = v);
            ApplyIfNotNull(_distributedSettings.Unsafe_EventHubCursorPosition, v => redisConfig.EventHubCursorPosition = v);
            ApplyIfNotNull(_distributedSettings.RedisGetBlobTimeoutMilliseconds, v => redisConfig.BlobTimeout = TimeSpan.FromMilliseconds(v));
            ApplyIfNotNull(_distributedSettings.RedisGetCheckpointStateTimeoutInSeconds, v => redisConfig.ClusterRedisOperationTimeout = TimeSpan.FromSeconds(v));

            ApplyIfNotNull(_distributedSettings.ShouldFilterInactiveMachinesInLocalLocationStore, v => redisConfig.ShouldFilterInactiveMachinesInLocalLocationStore = v);

            redisConfig.ReputationTrackerConfiguration.Enabled = _distributedSettings.IsMachineReputationEnabled;

            if (_distributedSettings.IsContentLocationDatabaseEnabled)
            {
                var dbConfig = RocksDbContentLocationDatabaseConfiguration.FromDistributedContentSettings(
                    _distributedSettings,
                    primaryCacheRoot / "LocationDb",
                    primaryCacheRoot / "LocationDbLogs",
                    logsKeepLongTerm: true
                    );
                redisConfig.Database = dbConfig;
                ApplySecretSettingsForLls(redisConfig, primaryCacheRoot, dbConfig);
            }

            if (redisConfig.AllContentMetadataStoreModeFlags.HasAnyFlag(ContentMetadataStoreModeFlags.Distributed))
            {
                redisConfig.MetadataStore = new ClientContentMetadataStoreConfiguration()
                {
                    AreBlobsSupported = _distributedSettings.ContentMetadataBlobsEnabled && redisConfig.AreBlobsSupported,
                    OperationTimeout = _distributedSettings.ContentMetadataClientOperationTimeout,
                };
            }

            _arguments.Overrides.Override(redisConfig);

            return redisConfig;
        }

        public IEnumerable<IGrpcServiceEndpoint> CreateMetadataServices()
        {
            if (Services.GlobalCacheService.TryGetInstance(out var service))
            {
                yield return new ProtobufNetGrpcServiceEndpoint<IGlobalCacheService, GlobalCacheService>(nameof(GlobalCacheService), service);
            }
        }

        public IGrpcServiceEndpoint[] GetAdditionalEndpoints()
        {
            return CreateMetadataServices().ToArray();
        }

        public (IContentStore topLevelStore, DistributedContentStore primaryDistributedStore) CreateTopLevelStore()
        {
            (IContentStore topLevelStore, DistributedContentStore primaryDistributedStore) result = default;

            var coldStorage = Services.ColdStorage.InstanceOrDefault();

            if (_distributedSettings.GetMultiplexMode() == MultiplexMode.Legacy)
            {
                var multiplexedStore =
                    CreateMultiplexedStore(settings =>
                        CreateDistributedContentStore(settings, coldStorage, dls =>
                            CreateFileSystemContentStore(settings, dls)));
                result.topLevelStore = multiplexedStore;
                result.primaryDistributedStore = (DistributedContentStore)multiplexedStore.PreferredContentStore;
            }
            else
            {
                var distributedStore =
                    CreateDistributedContentStore(OrderedResolvedCacheSettings[0], coldStorage, dls =>
                        CreateMultiplexedStore(settings =>
                            CreateFileSystemContentStore(settings, dls, coldStorage)));
                result.topLevelStore = distributedStore;
                result.primaryDistributedStore = distributedStore;
            }

            return result;
        }

        public DistributedContentStore CreateDistributedContentStore(
            ResolvedNamedCacheSettings resolvedSettings,
            ColdStorage coldStorage,
            Func<IDistributedLocationStore, IContentStore> innerStoreFactory)
        {
            _logger.Debug("Creating a distributed content store");

            var contentStore =
                new DistributedContentStore(
                    resolvedSettings.MachineLocation,
                    resolvedSettings.ResolvedCacheRootPath,
                    distributedStore => innerStoreFactory(distributedStore),
                    Services.ContentLocationStoreFactory.Instance,
                    _distributedContentStoreSettings,
                    distributedCopier: _copier,
                    coldStorage: coldStorage,
                    clock: _arguments.Overrides.Clock);

            _logger.Debug("Created Distributed content store.");
            return contentStore;
        }

        public IContentStore CreateFileSystemContentStore(ResolvedNamedCacheSettings resolvedCacheSettings, IDistributedLocationStore distributedStore, ColdStorage coldStorage = null)
        {
            var contentStoreSettings = FromDistributedSettings(_distributedSettings);

            ConfigurationModel configurationModel
                = new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota(resolvedCacheSettings.Settings.CacheSizeQuotaString)));

            return ContentStoreFactory.CreateContentStore(_fileSystem, resolvedCacheSettings.ResolvedCacheRootPath,
                        contentStoreSettings: contentStoreSettings, distributedStore: distributedStore, configurationModel: configurationModel, coldStorage);
        }

        public MultiplexedContentStore CreateMultiplexedStore(Func<ResolvedNamedCacheSettings, IContentStore> createContentStore)
        {
            var cacheConfig = _arguments.Configuration;
            var distributedSettings = cacheConfig.DistributedContentSettings;
            var drivesWithContentStore = new Dictionary<string, IContentStore>(StringComparer.OrdinalIgnoreCase);

            foreach (var resolvedCacheSettings in OrderedResolvedCacheSettings)
            {
                _logger.Debug($"Using [{resolvedCacheSettings.Settings.CacheRootPath}]'s settings: {resolvedCacheSettings.Settings}");

                drivesWithContentStore[resolvedCacheSettings.Drive] = createContentStore(resolvedCacheSettings);
            }

            if (string.IsNullOrEmpty(cacheConfig.LocalCasSettings.PreferredCacheDrive))
            {
                var knownDrives = string.Join(",", OrderedResolvedCacheSettings.Select(cacheSetting => cacheSetting.Drive));
                throw new ArgumentException($"Preferred cache drive is missing, which can indicate an invalid configuration. Known drives={knownDrives}");
            }

            return new MultiplexedContentStore(drivesWithContentStore, cacheConfig.LocalCasSettings.PreferredCacheDrive,
                // None legacy mode attempts operation on all stores
                tryAllSessions: distributedSettings.GetMultiplexMode() != MultiplexMode.Legacy);
        }

        private static DistributedContentStoreSettings CreateDistributedStoreSettings(
            DistributedCacheServiceArguments arguments,
            RedisContentLocationStoreConfiguration redisContentLocationStoreConfiguration)
        {
            var distributedSettings = arguments.Configuration.DistributedContentSettings;

            PinConfiguration pinConfiguration = new PinConfiguration();
            ApplyIfNotNull(distributedSettings.PinMinUnverifiedCount, v => pinConfiguration.PinMinUnverifiedCount = v);
            ApplyIfNotNull(distributedSettings.StartCopyWhenPinMinUnverifiedCountThreshold, v => pinConfiguration.AsyncCopyOnPinThreshold = v);
            ApplyIfNotNull(distributedSettings.AsyncCopyOnPinThreshold, v => pinConfiguration.AsyncCopyOnPinThreshold = v);
            ApplyIfNotNull(distributedSettings.MaxIOOperations, v => pinConfiguration.MaxIOOperations = v);

            var distributedContentStoreSettings = new DistributedContentStoreSettings()
            {
                TrustedHashFileSizeBoundary = distributedSettings.TrustedHashFileSizeBoundary,
                ParallelHashingFileSizeBoundary = distributedSettings.ParallelHashingFileSizeBoundary,
                PinConfiguration = pinConfiguration,
                RetryIntervalForCopies = distributedSettings.RetryIntervalForCopies,
                BandwidthConfigurations = FromDistributedSettings(distributedSettings.BandwidthConfigurations),
                MaxRetryCount = distributedSettings.MaxRetryCount,
                ProactiveCopyMode = (ProactiveCopyMode)Enum.Parse(typeof(ProactiveCopyMode), distributedSettings.ProactiveCopyMode),
                PushProactiveCopies = distributedSettings.PushProactiveCopies,
                ProactiveCopyOnPut = distributedSettings.ProactiveCopyOnPut,
                ProactiveCopyOnPin = distributedSettings.ProactiveCopyOnPin,
                ProactiveCopyUsePreferredLocations = distributedSettings.ProactiveCopyUsePreferredLocations,
                ProactiveCopyLocationsThreshold = distributedSettings.ProactiveCopyLocationsThreshold,
                ProactiveCopyRejectOldContent = distributedSettings.ProactiveCopyRejectOldContent,
                EnableRepairHandling = distributedSettings.IsRepairHandlingEnabled,
                LocationStoreBatchSize = distributedSettings.RedisBatchPageSize,
                RestrictedCopyReplicaCount = distributedSettings.RestrictedCopyReplicaCount,
                CopyAttemptsWithRestrictedReplicas = distributedSettings.CopyAttemptsWithRestrictedReplicas,
                PeriodicCopyTracingInterval = TimeSpan.FromMinutes(distributedSettings.PeriodicCopyTracingIntervalMinutes),
                AreBlobsSupported = redisContentLocationStoreConfiguration.AreBlobsSupported,
                MaxBlobSize = redisContentLocationStoreConfiguration.MaxBlobSize,
                DelayForProactiveReplication = TimeSpan.FromSeconds(distributedSettings.ProactiveReplicationDelaySeconds),
                ProactiveReplicationCopyLimit = distributedSettings.ProactiveReplicationCopyLimit,
                EnableProactiveReplication = distributedSettings.EnableProactiveReplication,
                TraceProactiveCopy = distributedSettings.TraceProactiveCopy,
                ProactiveCopyGetBulkBatchSize = distributedSettings.ProactiveCopyGetBulkBatchSize,
                ProactiveCopyGetBulkInterval = TimeSpan.FromSeconds(distributedSettings.ProactiveCopyGetBulkIntervalSeconds),
                ProactiveCopyMaxRetries = distributedSettings.ProactiveCopyMaxRetries,
            };
            ApplyIfNotNull(distributedSettings.GrpcCopyCompressionSizeThreshold, v => distributedContentStoreSettings.GrpcCopyCompressionSizeThreshold = v);
            ApplyEnumIfNotNull<CopyCompression>(distributedSettings.GrpcCopyCompressionAlgorithm, v => distributedContentStoreSettings.GrpcCopyCompressionAlgorithm = v);
            ApplyIfNotNull(distributedSettings.UseInRingMachinesForCopies, v => distributedContentStoreSettings.UseInRingMachinesForCopies = v);
            ApplyIfNotNull(distributedSettings.UseInRingMachinesForCopies, v => distributedContentStoreSettings.StoreBuildIdInCache = v);

            if (distributedSettings.EnableProactiveReplication && redisContentLocationStoreConfiguration.Checkpoint != null)
            {
                distributedContentStoreSettings.ProactiveReplicationInterval = redisContentLocationStoreConfiguration.Checkpoint.RestoreCheckpointInterval;

                ApplyIfNotNull(
                    distributedSettings.ProactiveReplicationIntervalMinutes,
                    value => distributedContentStoreSettings.ProactiveReplicationInterval = TimeSpan.FromMinutes(value));
            }

            ApplyIfNotNull(distributedSettings.MaximumConcurrentPutAndPlaceFileOperations, v => distributedContentStoreSettings.MaximumConcurrentPutAndPlaceFileOperations = v);

            distributedContentStoreSettings.CopyScheduler = CopySchedulerConfiguration.FromDistributedContentSettings(distributedSettings);
            arguments.Overrides.Override(distributedContentStoreSettings);

            return distributedContentStoreSettings;
        }

        private static IReadOnlyList<BandwidthConfiguration> FromDistributedSettings(Configuration.BandwidthConfiguration[] settings)
        {
            return settings?.Select(bc => fromDistributedConfiguration(bc)).ToArray() ?? new BandwidthConfiguration[0];

            static BandwidthConfiguration fromDistributedConfiguration(Configuration.BandwidthConfiguration configuration)
            {
                var result = new BandwidthConfiguration()
                {
                    Interval = TimeSpan.FromSeconds(configuration.IntervalInSeconds),
                    RequiredBytes = configuration.RequiredBytes,
                    FailFastIfServerIsBusy = configuration.FailFastIfServerIsBusy,
                };

                ApplyIfNotNull(configuration.ConnectionTimeoutInSeconds, v => result.ConnectionTimeout = TimeSpan.FromSeconds(v));
                ApplyIfNotNull(configuration.InvalidateOnTimeoutError, v => result.InvalidateOnTimeoutError = v);

                return result;
            }
        }

        internal static IContentStore CreateLocalContentStore(
            DistributedContentSettings settings,
            DistributedCacheServiceArguments arguments,
            ResolvedNamedCacheSettings resolvedSettings,
            IDistributedLocationStore distributedStore = null)
        {
            settings ??= DistributedContentSettings.CreateDisabled();
            var contentStoreSettings = FromDistributedSettings(settings);

            ConfigurationModel configurationModel
                = new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota(resolvedSettings.Settings.CacheSizeQuotaString)));

            var localStore = ContentStoreFactory.CreateContentStore(arguments.FileSystem, resolvedSettings.ResolvedCacheRootPath,
                            contentStoreSettings: contentStoreSettings, distributedStore: distributedStore, configurationModel: configurationModel);

            if (settings.BackingGrpcPort != null)
            {
                var serviceClientRpcConfiguration = new ServiceClientRpcConfiguration(settings.BackingGrpcPort.Value)
                {
                    GrpcCoreClientOptions = settings.GrpcCopyClientGrpcCoreClientOptions,
                };

                var retryCount = arguments.Configuration.LocalCasSettings.CasClientSettings.RetryIntervalSecondsOnFailServiceCalls;
                var retryIntervalInSeconds = arguments.Configuration.LocalCasSettings.CasClientSettings.RetryCountOnFailServiceCalls;
                var serviceClientConfiguration =
                    new ServiceClientContentStoreConfiguration(resolvedSettings.Name, serviceClientRpcConfiguration, settings.BackingScenario)
                    {
                        RetryCount = retryCount,
                        RetryIntervalSeconds = retryIntervalInSeconds,
                        GrpcEnvironmentOptions = arguments.Configuration.LocalCasSettings.ServiceSettings.GrpcEnvironmentOptions
                    };

                var backingStore = new ServiceClientContentStore(
                    arguments.Logger,
                    arguments.FileSystem,
                    serviceClientConfiguration);

                return new MultiLevelContentStore(localStore, backingStore);
            }

            return localStore;
        }

        private static ContentStoreSettings FromDistributedSettings(DistributedContentSettings settings)
        {
            var result = new ContentStoreSettings()
            {
                CheckFiles = settings.CheckLocalFiles,
                SelfCheckSettings = CreateSelfCheckSettings(settings),
                OverrideUnixFileAccessMode = settings.OverrideUnixFileAccessMode,
                UseRedundantPutFileShortcut = settings.UseRedundantPutFileShortcut,
                TraceFileSystemContentStoreDiagnosticMessages = settings.TraceFileSystemContentStoreDiagnosticMessages,

                SkipTouchAndLockAcquisitionWhenPinningFromHibernation = settings.UseFastHibernationPin,
            };

            ApplyIfNotNull(settings.SilentOperationDurationThreshold, v => result.SilentOperationDurationThreshold = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(settings.SilentOperationDurationThreshold, v => DefaultTracingConfiguration.DefaultSilentOperationDurationThreshold = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(settings.DefaultPendingOperationTracingIntervalInMinutes, v => DefaultTracingConfiguration.DefaultPendingOperationTracingInterval = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(settings.ReserveSpaceTimeoutInMinutes, v => result.ReserveTimeout = TimeSpan.FromMinutes(v));

            ApplyIfNotNull(settings.UseAsynchronousFileStreamOptionByDefault, v => FileSystemDefaults.UseAsynchronousFileStreamOptionByDefault = v);

            ApplyIfNotNull(settings.UseHierarchicalTraceIds, v => Context.UseHierarchicalIds = v);

            return result;
        }

        private static SelfCheckSettings CreateSelfCheckSettings(DistributedContentSettings settings)
        {
            var selfCheckSettings = new SelfCheckSettings()
            {
                Epoch = settings.SelfCheckEpoch,
                StartSelfCheckInStartup = settings.StartSelfCheckAtStartup,
                Frequency = TimeSpan.FromMinutes(settings.SelfCheckFrequencyInMinutes),
            };

            ApplyIfNotNull(settings.SelfCheckProgressReportingIntervalInMinutes, minutes => selfCheckSettings.ProgressReportingInterval = TimeSpan.FromMinutes(minutes));
            ApplyIfNotNull(settings.SelfCheckDelayInMilliseconds, milliseconds => selfCheckSettings.HashAnalysisDelay = TimeSpan.FromMilliseconds(milliseconds));
            ApplyIfNotNull(settings.SelfCheckDefaultHddDelayInMilliseconds, milliseconds => selfCheckSettings.DefaultHddHashAnalysisDelay = TimeSpan.FromMilliseconds(milliseconds));

            return selfCheckSettings;
        }

        private void ApplySecretSettingsForLls(
            RedisContentLocationStoreConfiguration configuration,
            AbsolutePath localCacheRoot,
            RocksDbContentLocationDatabaseConfiguration dbConfig)
        {
            if (_secrets.Secrets.Count == 0)
            {
                return;
            }

            configuration.Checkpoint = new CheckpointConfiguration(localCacheRoot, configuration.PrimaryMachineLocation);

            if (_distributedSettings.IsMasterEligible)
            {
                // Use master selection by setting role to null
                configuration.Checkpoint.Role = null;
            }
            else
            {
                // Not master eligible. Set role to worker.
                configuration.Checkpoint.Role = Role.Worker;
            }

            // It is important to set the current role of the service, to have non-null Role column
            // in all the tracing messages emitted to Kusto.
            GlobalInfoStorage.SetServiceRole(configuration.Checkpoint.Role?.ToString() ?? "MasterEligible");

            var checkpointConfiguration = configuration.Checkpoint;

            ApplyIfNotNull(_distributedSettings.MirrorClusterState, value => configuration.MirrorClusterState = value);
            ApplyIfNotNull(
                _distributedSettings.HeartbeatIntervalMinutes,
                value => checkpointConfiguration.HeartbeatInterval = TimeSpan.FromMinutes(value));
            ApplyIfNotNull(
                _distributedSettings.CreateCheckpointIntervalMinutes,
                value => checkpointConfiguration.CreateCheckpointInterval = TimeSpan.FromMinutes(value));
            ApplyIfNotNull(
                _distributedSettings.RestoreCheckpointIntervalMinutes,
                value => checkpointConfiguration.RestoreCheckpointInterval = TimeSpan.FromMinutes(value));
            ApplyIfNotNull(
                _distributedSettings.RestoreCheckpointTimeoutMinutes,
                value => checkpointConfiguration.RestoreCheckpointTimeout = TimeSpan.FromMinutes(value));

            ApplyIfNotNull(
                _distributedSettings.UpdateClusterStateIntervalSeconds,
                value => checkpointConfiguration.UpdateClusterStateInterval = TimeSpan.FromSeconds(value));

            ApplyIfNotNull(_distributedSettings.PacemakerEnabled, v => checkpointConfiguration.PacemakerEnabled = v);
            ApplyIfNotNull(_distributedSettings.PacemakerNumberOfBuckets, v => checkpointConfiguration.PacemakerNumberOfBuckets = v);
            ApplyIfNotNull(_distributedSettings.PacemakerUseRandomIdentifier, v => checkpointConfiguration.PacemakerUseRandomIdentifier = v);

            ApplyIfNotNull(
                _distributedSettings.SafeToLazilyUpdateMachineCountThreshold,
                value => configuration.SafeToLazilyUpdateMachineCountThreshold = value);

            ApplyEnumIfNotNull<ReconciliationMode>(_distributedSettings.ReconcileMode, v => configuration.ReconcileMode = v);
            ApplyIfNotNull(_distributedSettings.ReconcileHashesLogLimit, v => configuration.ReconcileHashesLogLimit = v);

            configuration.ReconciliationCycleFrequency = TimeSpan.FromMinutes(_distributedSettings.ReconciliationCycleFrequencyMinutes);
            configuration.ReconciliationMaxCycleSize = _distributedSettings.ReconciliationMaxCycleSize;
            configuration.ReconciliationMaxRemoveHashesCycleSize = _distributedSettings.ReconciliationMaxRemoveHashesCycleSize;
            configuration.ReconciliationMaxRemoveHashesAddPercentage = _distributedSettings.ReconciliationMaxRemoveHashesAddPercentage;

            configuration.ReconciliationAddLimit = _distributedSettings.ReconciliationAddLimit;
            configuration.ReconciliationRemoveLimit = _distributedSettings.ReconciliationRemoveLimit;
            configuration.ContentMetadataStoreMode = _distributedSettings.ContentMetadataStoreMode;

            ApplyIfNotNull(_distributedSettings.DistributedContentConsumerOnly, value =>
            {
                configuration.DistributedContentConsumerOnly = value;
                if (value)
                {
                    // If consumer only, override default to disable updating cluster state
                    checkpointConfiguration.UpdateClusterStateInterval ??= Timeout.InfiniteTimeSpan;
                }
            });
            ApplyIfNotNull(_distributedSettings.IncrementalCheckpointDegreeOfParallelism, value => configuration.Checkpoint.IncrementalCheckpointDegreeOfParallelism = value);

            ApplyIfNotNull(_distributedSettings.RedisMemoizationDatabaseOperationTimeoutInSeconds, value => configuration.Memoization.OperationTimeout = TimeSpan.FromSeconds(value));
            ApplyIfNotNull(_distributedSettings.RedisMemoizationSlowOperationCancellationTimeoutInSeconds, value => configuration.Memoization.SlowOperationCancellationTimeout = TimeSpan.FromSeconds(value));

            if (!string.IsNullOrEmpty(_distributedSettings.GlobalRedisSecretName))
            {
                configuration.RedisGlobalStoreConnectionString = ((PlainTextSecret)GetRequiredSecret(_distributedSettings.GlobalRedisSecretName)).Secret;
            }
            
            if (!string.IsNullOrEmpty(_distributedSettings.SecondaryGlobalRedisSecretName))
            {
                configuration.RedisGlobalStoreSecondaryConnectionString = ((PlainTextSecret)GetRequiredSecret(
                    _distributedSettings.SecondaryGlobalRedisSecretName)).Secret;
            }

            configuration.RedisConnectionMultiplexerConfiguration = RedisConnectionMultiplexerConfiguration.FromDistributedContentSettings(_distributedSettings);

            ApplyIfNotNull(_distributedSettings.LocationEntryExpiryMinutes, value => configuration.LocationEntryExpiry = TimeSpan.FromMinutes(value));

            ApplyIfNotNull(_distributedSettings.RestoreCheckpointAgeThresholdMinutes, v => configuration.Checkpoint.RestoreCheckpointAgeThreshold = TimeSpan.FromMinutes(v));
            // Need to disable cleaning database on initialization when restore checkpoint age is set.
            ApplyIfNotNull(_distributedSettings.RestoreCheckpointAgeThresholdMinutes, v => dbConfig.CleanOnInitialize = false);

            var errorBuilder = new StringBuilder();
            var storageCredentials = GetStorageCredentials(errorBuilder);
            Contract.Assert(storageCredentials != null && storageCredentials.Length > 0);

            var blobStoreConfiguration = new BlobCentralStoreConfiguration(
                credentials: storageCredentials,
                containerName: _arguments.HostInfo.AppendRingSpecifierIfNeeded("checkpoints", _distributedSettings.UseRingIsolation),
                checkpointsKey: "checkpoints-eventhub");

            ApplyIfNotNull(
                _distributedSettings.CentralStorageOperationTimeoutInMinutes,
                value => blobStoreConfiguration.OperationTimeout = TimeSpan.FromMinutes(value));
            configuration.CentralStore = blobStoreConfiguration;

            if (_distributedSettings.UseDistributedCentralStorage)
            {
                var distributedCentralStoreConfiguration = new DistributedCentralStoreConfiguration(localCacheRoot)
                {
                    MaxRetentionGb = _distributedSettings.MaxCentralStorageRetentionGb,
                    PropagationDelay = TimeSpan.FromSeconds(_distributedSettings.CentralStoragePropagationDelaySeconds),
                    PropagationIterations = _distributedSettings.CentralStoragePropagationIterations,
                    MaxSimultaneousCopies = _distributedSettings.CentralStorageMaxSimultaneousCopies,
                    ProactiveCopyCheckpointFiles = _distributedSettings.ProactiveCopyCheckpointFiles,
                    InlineCheckpointProactiveCopies = _distributedSettings.InlineCheckpointProactiveCopies
                };

                if (_distributedSettings.UseSelfCheckSettingsForDistributedCentralStorage)
                {
                    distributedCentralStoreConfiguration.SelfCheckSettings = CreateSelfCheckSettings(_distributedSettings);
                }

                distributedCentralStoreConfiguration.TraceFileSystemContentStoreDiagnosticMessages = _distributedSettings.TraceFileSystemContentStoreDiagnosticMessages;

                ApplyIfNotNull(_distributedSettings.DistributedCentralStoragePeerToPeerCopyTimeoutSeconds, v => distributedCentralStoreConfiguration.PeerToPeerCopyTimeout = TimeSpan.FromSeconds(v));

                configuration.DistributedCentralStore = distributedCentralStoreConfiguration;
            }

            EventHubContentLocationEventStoreConfiguration eventStoreConfiguration;
            string epoch = _keySpace + _distributedSettings.EventHubEpoch;
            bool ignoreEpoch = _distributedSettings.Unsafe_IgnoreEpoch ?? false;

            eventStoreConfiguration = new EventHubContentLocationEventStoreConfiguration(
                eventHubName: _distributedSettings.EventHubName,
                eventHubConnectionString: _distributedSettings.EventHubConnectionString ?? ((PlainTextSecret)GetRequiredSecret(_distributedSettings.EventHubSecretName)).Secret,
                consumerGroupName: _distributedSettings.EventHubConsumerGroupName,
                epoch: epoch,
                ignoreEpoch: ignoreEpoch);

            dbConfig.Epoch = eventStoreConfiguration.Epoch;

            configuration.EventStore = eventStoreConfiguration;
            ApplyIfNotNull(
                _distributedSettings.MaxEventProcessingConcurrency,
                value => eventStoreConfiguration.MaxEventProcessingConcurrency = value);

            ApplyIfNotNull(
                _distributedSettings.EventBatchSize,
                value => eventStoreConfiguration.EventBatchSize = value);

            ApplyIfNotNull(
                _distributedSettings.EventProcessingMaxQueueSize,
                value => eventStoreConfiguration.EventProcessingMaxQueueSize = value);

            if (_distributedSettings.UseBlobCheckpointRegistry)
            {
                var azureBlobStorageCheckpointRegistryConfiguration = new AzureBlobStorageCheckpointRegistryConfiguration()
                {
                    Credentials = storageCredentials[0],
                    ContainerName = _arguments.HostInfo.AppendRingSpecifierIfNeeded("checkpoints", _distributedSettings.UseRingIsolation),
                    FolderName = "checkpointRegistry",
                    KeySpacePrefix = epoch,
                };

                ApplyIfNotNull(_distributedSettings.BlobCheckpointRegistryGarbageCollectionTimeout, v => azureBlobStorageCheckpointRegistryConfiguration.GarbageCollectionTimeout = v);
                ApplyIfNotNull(_distributedSettings.BlobCheckpointRegistryRegisterCheckpointTimeout, v => azureBlobStorageCheckpointRegistryConfiguration.RegisterCheckpointTimeout = v);
                ApplyIfNotNull(_distributedSettings.BlobCheckpointRegistryGetCheckpointStateTimeout, v => azureBlobStorageCheckpointRegistryConfiguration.CheckpointStateTimeout = v);
                ApplyIfNotNull(_distributedSettings.BlobCheckpointRegistryStandalone, v => azureBlobStorageCheckpointRegistryConfiguration.Standalone = v);

                ApplyIfNotNull(_distributedSettings.BlobCheckpointRegistryCheckpointLimit, v => azureBlobStorageCheckpointRegistryConfiguration.CheckpointLimit = v);
                azureBlobStorageCheckpointRegistryConfiguration.NewEpochEventStartCursorDelay = eventStoreConfiguration.NewEpochEventStartCursorDelay;

                configuration.AzureBlobStorageCheckpointRegistryConfiguration = azureBlobStorageCheckpointRegistryConfiguration;
            }

            if (_distributedSettings.UseBlobMasterElection)
            {
                var azureBlobStorageMasterElectionMechanismConfiguration = new AzureBlobStorageMasterElectionMechanismConfiguration()
                {
                    Credentials = storageCredentials[0],
                    ContainerName = _arguments.HostInfo.AppendRingSpecifierIfNeeded("checkpoints", _distributedSettings.UseRingIsolation),
                    FolderName = $"{epoch}/masterElection",
                };

                azureBlobStorageMasterElectionMechanismConfiguration.IsMasterEligible = _distributedSettings.IsMasterEligible && !(_distributedSettings.DistributedContentConsumerOnly ?? false);

                ApplyIfNotNull(_distributedSettings.BlobMasterElectionFileName, v => azureBlobStorageMasterElectionMechanismConfiguration.FileName = v);
                ApplyIfNotNull(_distributedSettings.BlobMasterElectionLeaseExpiryTime, v => azureBlobStorageMasterElectionMechanismConfiguration.LeaseExpiryTime = v);
                ApplyIfNotNull(_distributedSettings.BlobMasterElectionStorageInteractionTimeout, v => azureBlobStorageMasterElectionMechanismConfiguration.StorageInteractionTimeout = v);

                configuration.AzureBlobStorageMasterElectionMechanismConfiguration = azureBlobStorageMasterElectionMechanismConfiguration;
            }

            if (_distributedSettings.UseBlobClusterStateStorage)
            {
                var blobClusterStateStorageConfiguration = new BlobClusterStateStorageConfiguration()
                {
                    Credentials = storageCredentials[0],
                    ContainerName = _arguments.HostInfo.AppendRingSpecifierIfNeeded("checkpoints", _distributedSettings.UseRingIsolation),
                    FolderName = $"{epoch}/clusterState",
                };

                ApplyIfNotNull(_distributedSettings.BlobClusterStateStorageFileName, v => blobClusterStateStorageConfiguration.FileName = v);
                ApplyIfNotNull(_distributedSettings.BlobClusterStateStorageStorageInteractionTimeout, v => blobClusterStateStorageConfiguration.StorageInteractionTimeout = v);
                ApplyIfNotNull(_distributedSettings.BlobClusterStateStorageStandalone, v => blobClusterStateStorageConfiguration.Standalone = v);

                var gcCfg = new ClusterStateRecomputeConfiguration();
                ApplyIfNotNull(_distributedSettings.MachineStateRecomputeIntervalMinutes, v => gcCfg.RecomputeFrequency = TimeSpan.FromMinutes(v));
                ApplyIfNotNull(_distributedSettings.MachineActiveToClosedIntervalMinutes, v => gcCfg.ActiveToClosedInterval = TimeSpan.FromMinutes(v));
                ApplyIfNotNull(_distributedSettings.MachineActiveToExpiredIntervalMinutes, v => gcCfg.ActiveToDeadExpiredInterval = TimeSpan.FromMinutes(v));
                ApplyIfNotNull(_distributedSettings.MachineActiveToExpiredIntervalMinutes, v => gcCfg.ClosedToDeadExpiredInterval = TimeSpan.FromMinutes(v));

                blobClusterStateStorageConfiguration.RecomputeConfiguration = gcCfg;
                configuration.BlobClusterStateStorageConfiguration = blobClusterStateStorageConfiguration;
            }
        }

        private AzureBlobStorageCredentials[] GetStorageCredentials(StringBuilder errorBuilder)
        {
            IEnumerable<string> storageSecretNames = GetAzureStorageSecretNames(errorBuilder);
            // This would have failed earlier otherwise
            Contract.Assert(storageSecretNames != null);

            return GetStorageCredentials(storageSecretNames);
        }

        public AzureBlobStorageCredentials[] GetStorageCredentials(IEnumerable<string> storageSecretNames)
        {
            var credentials = new List<AzureBlobStorageCredentials>();
            foreach (var secretName in storageSecretNames)
            {
                var secret = GetRequiredSecret(secretName);

                if (_distributedSettings.AzureBlobStorageUseSasTokens)
                {
                    var updatingSasToken = secret as UpdatingSasToken;
                    Contract.Assert(!(updatingSasToken is null));

                    credentials.Add(new AzureBlobStorageCredentials(updatingSasToken));
                }
                else
                {
                    var plainTextSecret = secret as PlainTextSecret;
                    Contract.Assert(!(plainTextSecret is null));

                    credentials.Add(new AzureBlobStorageCredentials(plainTextSecret));
                }
            }

            return credentials.ToArray();
        }

        private List<string> GetAzureStorageSecretNames(StringBuilder errorBuilder)
        {
            var secretNames = new List<string>();
            if (_distributedSettings.AzureStorageSecretName != null && !string.IsNullOrEmpty(_distributedSettings.AzureStorageSecretName))
            {
                secretNames.Add(_distributedSettings.AzureStorageSecretName);
            }

            if (_distributedSettings.AzureStorageSecretNames != null && !_distributedSettings.AzureStorageSecretNames.Any(string.IsNullOrEmpty))
            {
                secretNames.AddRange(_distributedSettings.AzureStorageSecretNames);
            }

            if (secretNames.Count > 0)
            {
                return secretNames;
            }

            errorBuilder.Append(
                $"Unable to configure Azure Storage. {nameof(DistributedContentSettings.AzureStorageSecretName)} or {nameof(DistributedContentSettings.AzureStorageSecretNames)} configuration options should be provided. ");
            return null;

        }

        public Secret GetRequiredSecret(string secretName)
        {
            Contract.Requires(!string.IsNullOrEmpty(secretName), "Attempt to retrieve invalid secret");
            if (!_secrets.Secrets.TryGetValue(secretName, out var value))
            {
                throw new KeyNotFoundException($"Missing secret: {secretName}");
            }

            return value;
        }
    }
}
