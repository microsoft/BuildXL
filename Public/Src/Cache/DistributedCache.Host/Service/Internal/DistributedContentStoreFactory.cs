// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Services;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using ContentStore.Grpc;
using static BuildXL.Utilities.ConfigurationHelper;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using BandwidthConfiguration = BuildXL.Cache.ContentStore.Distributed.BandwidthConfiguration;

namespace BuildXL.Cache.Host.Service.Internal
{
    public sealed class DistributedContentStoreFactory : IDistributedServicesSecrets
    {
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

        public LocalLocationStoreConfiguration ContentLocationStoreConfiguration { get; }

        public DistributedContentStoreFactory(DistributedCacheServiceArguments arguments)
        {
            _logger = arguments.Logger;

            _arguments = arguments;
            _distributedSettings = arguments.Configuration.DistributedContentSettings;

            _distributedSettings.DisableRedis();

            _keySpace = string.IsNullOrWhiteSpace(_arguments.Keyspace) ? "Default:" : _arguments.Keyspace;
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

            ContentLocationStoreConfiguration = CreateContentLocationStoreConfiguration();
            _distributedContentStoreSettings = CreateDistributedStoreSettings(_arguments, ContentLocationStoreConfiguration);

            // Tracing configuration before creating anything.
            if (arguments.TraceConfiguration)
            {
                TraceConfiguration();
            }

            _copier = new DistributedContentCopier(
                CreateDistributedContentCopierConfiguration(arguments),
                _fileSystem,
                fileCopier: _arguments.Copier!,
                copyRequester: _arguments.CopyRequester!,
                _arguments.Overrides.Clock,
                _logger
            );
        }

        private DistributedContentStoreServices CreateDistributedContentStoreServices(IContentStore preferredContentStore)
        {
            var primaryCacheRoot = OrderedResolvedCacheSettings[0].ResolvedCacheRootPath;

            var connectionPool = new GrpcConnectionMap(new ConnectionPoolConfiguration()
            {
                ConnectTimeout = _distributedSettings.ContentMetadataClientConnectionTimeout,
                GrpcDotNetOptions = _distributedSettings.ContentMetadataClientGrpcDotNetClientOptions ?? GrpcDotNetClientOptions.Default,
            },
            context: new OperationContext(_arguments.TracingContext.CreateNested(componentName: nameof(GrpcConnectionMap))));

            var serviceArguments = new DistributedContentStoreServicesArguments
            (
                ConnectionMap: connectionPool,
                DistributedContentSettings: _distributedSettings,
                ContentLocationStoreConfiguration: ContentLocationStoreConfiguration,
                Overrides: _arguments.Overrides,
                Secrets: this,
                PrimaryCacheRoot: primaryCacheRoot,
                FileSystem: _arguments.FileSystem,
                DistributedContentCopier: _copier,
                PreferredContentStore: preferredContentStore
            );

            return new DistributedContentStoreServices(serviceArguments);
        }

        private void TraceConfiguration()
        {
            ConfigurationPrinter.TraceConfiguration(_distributedContentStoreSettings, _logger);
            ConfigurationPrinter.TraceConfiguration(ContentLocationStoreConfiguration, _logger);
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

        private LocalLocationStoreConfiguration CreateContentLocationStoreConfiguration()
        {
            var primaryCacheRoot = OrderedResolvedCacheSettings[0].ResolvedCacheRootPath;

            var result = new LocalLocationStoreConfiguration
            {
                LogReconciliationHashes = _distributedSettings.LogReconciliationHashes,
                UseFullEvictionSort = _distributedSettings.UseFullEvictionSort,
                EvictionWindowSize = _distributedSettings.EvictionWindowSize,
                EvictionPoolSize = _distributedSettings.EvictionPoolSize,
                EvictionRemovalFraction = _distributedSettings.EvictionRemovalFraction,
                EvictionDiscardFraction = _distributedSettings.EvictionDiscardFraction,
                UseTieredDistributedEviction = _distributedSettings.UseTieredDistributedEviction,
                ProactiveCopyLocationsThreshold = _distributedSettings.ProactiveCopyLocationsThreshold,
                UseBinManager = _distributedSettings.UseBinManager || _distributedSettings.ProactiveCopyUsePreferredLocations,
                PreferredLocationsExpiryTime = TimeSpan.FromMinutes(_distributedSettings.PreferredLocationsExpiryTimeMinutes),
                PrimaryMachineLocation = OrderedResolvedCacheSettings[0].MachineLocation,
            };

            ApplyIfNotNull(_distributedSettings.ReplicaCreditInMinutes, v => result.ContentLifetime = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MachineRisk, v => result.MachineRisk = v);
            ApplyIfNotNull(_distributedSettings.LocationEntryExpiryMinutes, v => result.LocationEntryExpiry = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.TouchFrequencyMinutes, v => result.TouchFrequency = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.ReconcileCacheLifetimeMinutes, v => result.ReconcileCacheLifetime = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MaxProcessingDelayToReconcileMinutes, v => result.MaxProcessingDelayToReconcile = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.EvictionMinAgeMinutes, v => result.EvictionMinAge = TimeSpan.FromMinutes(v));

            ApplyIfNotNull(_distributedSettings.LocationStoreSettings, v => result.Settings = v);

            var dbConfig = RocksDbContentLocationDatabaseConfiguration.FromDistributedContentSettings(
                _distributedSettings,
                primaryCacheRoot / "LocationDb");
            result.Database = dbConfig;
            ApplySecretSettingsForLls(result, primaryCacheRoot, dbConfig);

            var clientContentMetadataStoreConfiguration = new ClientContentMetadataStoreConfiguration();
            ApplyIfNotNull(_distributedSettings.ContentMetadataClientOperationTimeout, v => clientContentMetadataStoreConfiguration.OperationTimeout = v);
            ApplyIfNotNull(_distributedSettings.ContentMetadataClientRetryMinimumWaitTime, v => clientContentMetadataStoreConfiguration.RetryMinimumWaitTime = v);
            ApplyIfNotNull(_distributedSettings.ContentMetadataClientRetryMaximumWaitTime, v => clientContentMetadataStoreConfiguration.RetryMaximumWaitTime = v);
            ApplyIfNotNull(_distributedSettings.ContentMetadataClientRetryDelta, v => clientContentMetadataStoreConfiguration.RetryDelta = v);

            result.MetadataStore = clientContentMetadataStoreConfiguration;

            _arguments.Overrides.Override(result);

            return result;
        }

        private static IEnumerable<IGrpcServiceEndpoint> CreateMetadataServices(DistributedContentStoreServices services)
        {
            if (services.GlobalCacheService.TryGetInstance(out var service))
            {
                yield return new ProtobufNetGrpcServiceEndpoint<IGlobalCacheService, GlobalCacheService>(nameof(GlobalCacheService), service);
            }
        }

        private static IGrpcServiceEndpoint[] GetAdditionalEndpoints(DistributedContentStoreServices services)
        {
            return CreateMetadataServices(services).ToArray();
        }

        public record CreateStoreResult
        {
            public required IContentStore TopLevelStore { get; init; }
            public required DistributedContentStore PrimaryDistributedStore { get; init; }
            public required DistributedContentStoreServices Services { get; init; }
            public required IGrpcServiceEndpoint[] AdditionalGrpcEndpoints { get; init; }
        }

        public CreateStoreResult CreateStore()
        {
            var multiplex = CreateMultiplexedStore();
            var services = CreateDistributedContentStoreServices(multiplex.PreferredContentStore);

            // NOTE: This relies on the assumption that when creating a distributed server,
            // there is only one call to create a cache so we simply create the cache here and ignore path
            // below in factory delegates since the logic for creating path based caches is included in the
            // call to CreateTopLevelStore
            var topLevelAndPrimaryStore = CreateTopLevelStore(multiplex, services);

            return new CreateStoreResult
            {
                TopLevelStore = topLevelAndPrimaryStore.topLevelStore,
                PrimaryDistributedStore = topLevelAndPrimaryStore.primaryDistributedStore,
                Services = services,
                AdditionalGrpcEndpoints = GetAdditionalEndpoints(services)
            };
        }

        private (IContentStore topLevelStore, DistributedContentStore primaryDistributedStore) CreateTopLevelStore(MultiplexedContentStore innerContentStore, DistributedContentStoreServices services)
        {
            (IContentStore topLevelStore, DistributedContentStore primaryDistributedStore) result = default;

            var distributedStore =
                CreateDistributedContentStore(OrderedResolvedCacheSettings[0], innerContentStore, services);

            foreach (IContentStore contentStore in innerContentStore.DrivesWithContentStore.Values)
            {
                if (contentStore is FileSystemContentStore fscs)
                {
                    // We attach the DistributedContentStore here because we need to create the FSCS before the DistributedContentStore
                    fscs.Store.AttachDistributedLocationStore(distributedStore);
                }
            }

            result.topLevelStore = distributedStore;
            result.primaryDistributedStore = distributedStore;

            return result;
        }

        private DistributedContentStore CreateDistributedContentStore(
            ResolvedNamedCacheSettings resolvedSettings,
            IContentStore innerContentStore,
            DistributedContentStoreServices services)
        {
            _logger.Debug("Creating a distributed content store");

            var contentStore =
                new DistributedContentStore(
                    resolvedSettings.MachineLocation,
                    resolvedSettings.ResolvedCacheRootPath,
                    innerContentStore,
                    services.ContentLocationStoreFactory.Instance,
                    _distributedContentStoreSettings,
                    distributedCopier: _copier,
                    clock: _arguments.Overrides.Clock);

            _logger.Debug("Created Distributed content store.");
            return contentStore;
        }

        private IContentStore CreateFileSystemContentStore(
            ResolvedNamedCacheSettings resolvedCacheSettings)
        {
            var contentStoreSettings = FromDistributedSettings(_distributedSettings);

            ConfigurationModel configurationModel
                = new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota(resolvedCacheSettings.Settings.CacheSizeQuotaString)));

            // We add the distributedStore later because we need the FSCS to build the DistributedStore
            return ContentStoreFactory.CreateContentStore(_fileSystem, resolvedCacheSettings.ResolvedCacheRootPath,
                        contentStoreSettings: contentStoreSettings, configurationModel: configurationModel);
        }

        private MultiplexedContentStore CreateMultiplexedStore()
        {
            var contentStores = new Dictionary<string, IContentStore>(StringComparer.OrdinalIgnoreCase);
            foreach (var settings in OrderedResolvedCacheSettings)
            {
                _logger.Debug($"Using [{settings.Settings.CacheRootPath}]'s settings: {settings.Settings}");
                contentStores[settings.Drive] = CreateFileSystemContentStore(settings);
            }

            return new MultiplexedContentStore(contentStores, OrderedResolvedCacheSettings[0].Drive);
        }

        private static DistributedContentCopier.Configuration CreateDistributedContentCopierConfiguration(DistributedCacheServiceArguments arguments)
        {
            var distributedSettings = arguments.Configuration.DistributedContentSettings;

            return new DistributedContentCopier.Configuration
            {
                TrustedHashFileSizeBoundary = distributedSettings.TrustedHashFileSizeBoundary,
                ParallelHashingFileSizeBoundary = distributedSettings.ParallelHashingFileSizeBoundary,
                RetryIntervalForCopies = distributedSettings.RetryIntervalForCopies,
                BandwidthConfigurations = FromDistributedSettings(distributedSettings.BandwidthConfigurations),
                MaxRetryCount = distributedSettings.MaxRetryCount,
                RestrictedCopyReplicaCount = distributedSettings.RestrictedCopyReplicaCount,
                CopyAttemptsWithRestrictedReplicas = distributedSettings.CopyAttemptsWithRestrictedReplicas,
                CopyScheduler = CopySchedulerConfiguration.FromDistributedContentSettings(distributedSettings),
            };
        }

        private static DistributedContentStoreSettings CreateDistributedStoreSettings(
            DistributedCacheServiceArguments arguments,
            LocalLocationStoreConfiguration contentLocationStoreConfiguration)
        {
            var distributedSettings = arguments.Configuration.DistributedContentSettings;

            PinConfiguration pinConfiguration = new PinConfiguration();
            ApplyIfNotNull(distributedSettings.PinMinUnverifiedCount, v => pinConfiguration.PinMinUnverifiedCount = v);
            ApplyIfNotNull(distributedSettings.UseLocalLocationsOnlyOnUnverifiedPin, v => pinConfiguration.UseLocalLocationsOnlyOnUnverifiedPin = v);
            ApplyIfNotNull(distributedSettings.StartCopyWhenPinMinUnverifiedCountThreshold, v => pinConfiguration.AsyncCopyOnPinThreshold = v);
            ApplyIfNotNull(distributedSettings.AsyncCopyOnPinThreshold, v => pinConfiguration.AsyncCopyOnPinThreshold = v);
            ApplyIfNotNull(distributedSettings.MaxIOOperations, v => pinConfiguration.MaxIOOperations = v);

            var distributedContentStoreSettings = new DistributedContentStoreSettings()
            {

                PinConfiguration = pinConfiguration,
                ProactiveCopyMode = (ProactiveCopyMode)Enum.Parse(typeof(ProactiveCopyMode), distributedSettings.ProactiveCopyMode),
                PushProactiveCopies = distributedSettings.PushProactiveCopies,
                ProactiveCopyOnPut = distributedSettings.ProactiveCopyOnPut,
                ProactiveCopyOnPin = distributedSettings.ProactiveCopyOnPin,
                ProactiveCopyUsePreferredLocations = distributedSettings.ProactiveCopyUsePreferredLocations,
                ProactiveCopyLocationsThreshold = distributedSettings.ProactiveCopyLocationsThreshold,
                ProactiveCopyRejectOldContent = distributedSettings.ProactiveCopyRejectOldContent,
                PeriodicCopyTracingInterval = TimeSpan.FromMinutes(distributedSettings.PeriodicCopyTracingIntervalMinutes),
                DelayForProactiveReplication = TimeSpan.FromSeconds(distributedSettings.ProactiveReplicationDelaySeconds),
                ProactiveReplicationCopyLimit = distributedSettings.ProactiveReplicationCopyLimit,
                EnableProactiveReplication = distributedSettings.EnableProactiveReplication,
                ProactiveCopyGetBulkBatchSize = distributedSettings.ProactiveCopyGetBulkBatchSize,
                ProactiveCopyGetBulkInterval = TimeSpan.FromSeconds(distributedSettings.ProactiveCopyGetBulkIntervalSeconds),
                ProactiveCopyMaxRetries = distributedSettings.ProactiveCopyMaxRetries,
                RespectSkipRegisterContentHint = distributedSettings.RegisterHintHandling.Value == RegisterHintHandling.SkipAndRegisterAssociatedContent
                    && distributedSettings.EnableDistributedCache // Only distributed cache supports registering associated content
            };

            ApplyIfNotNull(distributedSettings.ProactiveCopyInRingMachineLocationsExpiryCache, v => distributedContentStoreSettings.ProactiveCopyInRingMachineLocationsExpiryCache = v);
            ApplyIfNotNull(distributedSettings.RegisterContentEagerlyOnPut, v => distributedContentStoreSettings.RegisterEagerlyOnPut = v);
            ApplyIfNotNull(distributedSettings.GrpcCopyCompressionSizeThreshold, v => distributedContentStoreSettings.GrpcCopyCompressionSizeThreshold = v);
            ApplyEnumIfNotNull<CopyCompression>(distributedSettings.GrpcCopyCompressionAlgorithm, v => distributedContentStoreSettings.GrpcCopyCompressionAlgorithm = v);

            if (distributedSettings.EnableProactiveReplication && contentLocationStoreConfiguration.Checkpoint != null)
            {
                distributedContentStoreSettings.ProactiveReplicationInterval = contentLocationStoreConfiguration.Checkpoint.RestoreCheckpointInterval;

                ApplyIfNotNull(
                    distributedSettings.ProactiveReplicationIntervalMinutes,
                    value => distributedContentStoreSettings.ProactiveReplicationInterval = TimeSpan.FromMinutes(value));
            }

            ApplyIfNotNull(distributedSettings.MaximumConcurrentPutAndPlaceFileOperations, v => distributedContentStoreSettings.MaximumConcurrentPutAndPlaceFileOperations = v);
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
            ResolvedNamedCacheSettings resolvedSettings)
        {
            settings ??= DistributedContentSettings.CreateDisabled();
            var contentStoreSettings = FromDistributedSettings(settings);

            ConfigurationModel configurationModel
                = new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota(resolvedSettings.Settings.CacheSizeQuotaString)));

            var localStore = ContentStoreFactory.CreateContentStore(arguments.FileSystem, resolvedSettings.ResolvedCacheRootPath,
                            contentStoreSettings: contentStoreSettings, configurationModel: configurationModel);

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

            ApplyIfNotNull(settings.RemoveAuditRuleInheritance, v => result.RemoveAuditRuleInheritance = v);

            ApplyIfNotNull(settings.SilentOperationDurationThreshold, v => result.SilentOperationDurationThreshold = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(settings.SilentOperationDurationThreshold, v => DefaultTracingConfiguration.DefaultSilentOperationDurationThreshold = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(settings.DefaultPendingOperationTracingIntervalInMinutes, v => DefaultTracingConfiguration.DefaultPendingOperationTracingInterval = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(settings.ReserveSpaceTimeoutInMinutes, v => result.ReserveTimeout = TimeSpan.FromMinutes(v));

            ApplyIfNotNull(settings.UseHierarchicalTraceIds, v => Context.UseHierarchicalIds = v);
            ApplyIfNotNull(settings.RetryCountForFileHashing, v => result.RetryCountForFileHashing = v);
            ApplyIfNotNull(settings.RetryDelayForFileHashing, v => result.RetryDelayForFileHashing = v);

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
            LocalLocationStoreConfiguration configuration,
            AbsolutePath localCacheRoot,
            RocksDbContentLocationDatabaseConfiguration dbConfig)
        {
            if (_secrets.Secrets.Count == 0)
            {
                return;
            }

            configuration.Checkpoint = new CheckpointConfiguration(
                localCacheRoot,
                configuration.PrimaryMachineLocation);

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

            ApplyIfNotNull(_distributedSettings.DistributedContentConsumerOnly, value =>
            {
                configuration.DistributedContentConsumerOnly = value;
            });
            ApplyIfNotNull(_distributedSettings.IncrementalCheckpointDegreeOfParallelism, value => configuration.Checkpoint.IncrementalCheckpointDegreeOfParallelism = value);

            ApplyIfNotNull(_distributedSettings.MetadataEntryStorageThreshold, value => configuration.MetadataStoreMemoization.StorageMetadataEntrySizeThreshold = value);

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
                    InlineCheckpointProactiveCopies = _distributedSettings.InlineCheckpointProactiveCopies,
                    UsePrimaryCasInDcs = _distributedSettings.UsePrimaryCasInDcs,
                };

                if (_distributedSettings.UseSelfCheckSettingsForDistributedCentralStorage)
                {
                    distributedCentralStoreConfiguration.SelfCheckSettings = CreateSelfCheckSettings(_distributedSettings);
                }

                distributedCentralStoreConfiguration.TraceFileSystemContentStoreDiagnosticMessages = _distributedSettings.TraceFileSystemContentStoreDiagnosticMessages;

                ApplyIfNotNull(_distributedSettings.DistributedCentralStoragePeerToPeerCopyTimeoutSeconds, v => distributedCentralStoreConfiguration.PeerToPeerCopyTimeout = TimeSpan.FromSeconds(v));

                configuration.DistributedCentralStore = distributedCentralStoreConfiguration;
            }

            string epoch = _keySpace + _distributedSettings.EventHubEpoch;

            ContentLocationEventStoreConfiguration eventStoreConfiguration = _distributedSettings.DisableContentLocationEvents
                ? new NullContentLocationEventStoreConfiguration()
                : new EventHubContentLocationEventStoreConfiguration(
                    eventHubName: _distributedSettings.EventHubName,
                    eventHubSecret: !string.IsNullOrEmpty(_distributedSettings.EventHubConnectionString)
                        ? new PlainTextSecret(_distributedSettings.EventHubConnectionString)
                        : GetRequiredSecret(_distributedSettings.EventHubSecretName),
                    consumerGroupName: _distributedSettings.EventHubConsumerGroupName,
                    epoch: epoch);

            dbConfig.Epoch = eventStoreConfiguration.Epoch;

            configuration.EventStore = eventStoreConfiguration;
            ApplyIfNotNull(_distributedSettings.MaxEventProcessingConcurrency, value => eventStoreConfiguration.MaxEventProcessingConcurrency = value);

            ApplyIfNotNull(_distributedSettings.EventBatchSize, value => eventStoreConfiguration.EventBatchSize = value);
            ApplyIfNotNull(_distributedSettings.EventHubFlushShutdownTimeout, value => eventStoreConfiguration.FlushShutdownTimeout = value);
            ApplyIfNotNull(_distributedSettings.EventProcessingMaxQueueSize, value => eventStoreConfiguration.EventProcessingMaxQueueSize = value);
            ApplyIfNotNull(_distributedSettings.EventHubUseSpanBasedSerialization, value => eventStoreConfiguration.UseSpanBasedSerialization = value);
            ApplyIfNotNull(_distributedSettings.EventHubSelfCheckSerialization, value => eventStoreConfiguration.SelfCheckSerialization = value);

            var azureBlobStorageCheckpointRegistryConfiguration = new AzureBlobStorageCheckpointRegistryConfiguration()
            {
                Storage = new AzureBlobStorageCheckpointRegistryConfiguration.StorageSettings(
                    storageCredentials[0],
                    ContainerName: _arguments.HostInfo.AppendRingSpecifierIfNeeded("checkpoints", _distributedSettings.UseRingIsolation),
                    FolderName: "checkpointRegistry"),
                KeySpacePrefix = epoch,
            };

            ApplyIfNotNull(_distributedSettings.BlobCheckpointRegistryGarbageCollectionTimeout, v => azureBlobStorageCheckpointRegistryConfiguration.GarbageCollectionTimeout = v);
            ApplyIfNotNull(_distributedSettings.BlobCheckpointRegistryRegisterCheckpointTimeout, v => azureBlobStorageCheckpointRegistryConfiguration.RegisterCheckpointTimeout = v);
            ApplyIfNotNull(_distributedSettings.BlobCheckpointRegistryGetCheckpointStateTimeout, v => azureBlobStorageCheckpointRegistryConfiguration.CheckpointStateTimeout = v);
            ApplyIfNotNull(_distributedSettings.BlobCheckpointRegistryLatestFileMaxAge, v => azureBlobStorageCheckpointRegistryConfiguration.LatestFileMaxAge = v);
            ApplyIfNotNull(_distributedSettings.BlobCheckpointRegistryRetryPolicy, v => azureBlobStorageCheckpointRegistryConfiguration.BlobFolderStorageConfiguration.RetryPolicy = v);

            ApplyIfNotNull(_distributedSettings.BlobCheckpointRegistryCheckpointLimit, v => azureBlobStorageCheckpointRegistryConfiguration.CheckpointLimit = v);
            azureBlobStorageCheckpointRegistryConfiguration.NewEpochEventStartCursorDelay = eventStoreConfiguration.NewEpochEventStartCursorDelay;

            configuration.AzureBlobStorageCheckpointRegistryConfiguration = azureBlobStorageCheckpointRegistryConfiguration;

            var azureBlobStorageMasterElectionMechanismConfiguration = new AzureBlobStorageMasterElectionMechanismConfiguration()
            {
                Storage = new AzureBlobStorageMasterElectionMechanismConfiguration.StorageSettings(
                    Credentials: storageCredentials[0],
                    ContainerName: _arguments.HostInfo.AppendRingSpecifierIfNeeded("checkpoints", _distributedSettings.UseRingIsolation),
                    FolderName: $"{epoch}/masterElection"),
            };

            azureBlobStorageMasterElectionMechanismConfiguration.IsMasterEligible = _distributedSettings.IsMasterEligible && !(_distributedSettings.DistributedContentConsumerOnly ?? false);

            ApplyIfNotNull(_distributedSettings.BlobMasterElectionFileName, v => azureBlobStorageMasterElectionMechanismConfiguration.FileName = v);
            ApplyIfNotNull(_distributedSettings.BlobMasterElectionLeaseExpiryTime, v => azureBlobStorageMasterElectionMechanismConfiguration.LeaseExpiryTime = v);
            ApplyIfNotNull(_distributedSettings.BlobMasterElectionReleaseLeaseOnShutdown, v => azureBlobStorageMasterElectionMechanismConfiguration.ReleaseLeaseOnShutdown = v);
            ApplyIfNotNull(_distributedSettings.BlobMasterElectionStorageInteractionTimeout, v => azureBlobStorageMasterElectionMechanismConfiguration.BlobFolderStorageConfiguration.StorageInteractionTimeout = v);
            ApplyIfNotNull(_distributedSettings.BlobMasterElectionRetryPolicy, v => azureBlobStorageMasterElectionMechanismConfiguration.BlobFolderStorageConfiguration.RetryPolicy = v);

            configuration.AzureBlobStorageMasterElectionMechanismConfiguration = azureBlobStorageMasterElectionMechanismConfiguration;
            configuration.ObservableMasterElectionMechanismConfiguration.GetRoleInterval = configuration.Checkpoint.HeartbeatInterval;
            configuration.ObservableMasterElectionMechanismConfiguration.GetRoleOnStartup = true;

            var blobClusterStateStorageConfiguration = new BlobClusterStateStorageConfiguration()
            {
                Storage = new BlobClusterStateStorageConfiguration.StorageSettings(
                    Credentials: storageCredentials[0],
                    ContainerName: _arguments.HostInfo.AppendRingSpecifierIfNeeded("checkpoints", _distributedSettings.UseRingIsolation),
                    FolderName: $"{epoch}/clusterState"),
            };
            ApplyIfNotNull(_distributedSettings.BlobClusterStateStorageFileName, v => blobClusterStateStorageConfiguration.FileName = v);
            ApplyIfNotNull(_distributedSettings.BlobClusterStateStorageStorageInteractionTimeout, v => blobClusterStateStorageConfiguration.BlobFolderStorageConfiguration.StorageInteractionTimeout = v);
            ApplyIfNotNull(_distributedSettings.BlobClusterStateStorageRetryPolicy, v => blobClusterStateStorageConfiguration.BlobFolderStorageConfiguration.RetryPolicy = v);

            var gcCfg = new ClusterStateRecomputeConfiguration();
            ApplyIfNotNull(_distributedSettings.MachineActiveToClosedIntervalMinutes, v => gcCfg.ActiveToClosed = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MachineClosedToDeadExpiredIntervalMinutes, v => gcCfg.ClosedToExpired = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MachineDeadExpiredToDeadUnavailableIntervalMinutes, v => gcCfg.ExpiredToUnavailable = TimeSpan.FromMinutes(v));

            blobClusterStateStorageConfiguration.RecomputeConfiguration = gcCfg;
            configuration.BlobClusterStateStorageConfiguration = blobClusterStateStorageConfiguration;
        }

        private SecretBasedAzureStorageCredentials[] GetStorageCredentials(StringBuilder errorBuilder)
        {
            IEnumerable<string> storageSecretNames = GetAzureStorageSecretNames(errorBuilder);
            // This would have failed earlier otherwise
            Contract.Assert(storageSecretNames != null);

            return GetStorageCredentials(storageSecretNames);
        }

        public SecretBasedAzureStorageCredentials[] GetStorageCredentials(IEnumerable<string> storageSecretNames)
        {
            var credentials = new List<SecretBasedAzureStorageCredentials>();
            foreach (var secretName in storageSecretNames)
            {
                var secret = GetRequiredSecret(secretName);

                if (_distributedSettings.AzureBlobStorageUseSasTokens)
                {
                    var updatingSasToken = secret as UpdatingSasToken;
                    Contract.Assert(!(updatingSasToken is null));

                    credentials.Add(new SecretBasedAzureStorageCredentials(updatingSasToken));
                }
                else
                {
                    var plainTextSecret = secret as PlainTextSecret;
                    Contract.Assert(!(plainTextSecret is null));

                    credentials.Add(new SecretBasedAzureStorageCredentials(plainTextSecret));
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
