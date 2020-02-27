// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using Microsoft.Practices.TransientFaultHandling;
using Microsoft.WindowsAzure.Storage.Auth;

namespace BuildXL.Cache.Host.Service.Internal
{
    public sealed class DistributedContentStoreFactory
    {
        private readonly RedisContentSecretNames _redisContentSecretNames;
        private readonly string _keySpace;
        private readonly IAbsFileSystem _fileSystem;
        private readonly DistributedCacheSecretRetriever _secretRetriever;
        private readonly ILogger _logger;

        private readonly DistributedContentSettings _distributedSettings;
        private readonly DistributedCacheServiceArguments _arguments;
        private readonly DistributedContentCopier<AbsolutePath> _copier;

        private readonly RedisMemoizationStoreFactory _redisMemoizationStoreFactory;
        private readonly DistributedContentStoreSettings _distributedContentStoreSettings;

        public IReadOnlyList<ResolvedNamedCacheSettings> OrderedResolvedCacheSettings => _orderedResolvedCacheSettings;
        private readonly List<ResolvedNamedCacheSettings> _orderedResolvedCacheSettings;

        public RedisMemoizationStoreConfiguration RedisContentLocationStoreConfiguration { get; }

        public DistributedContentStoreFactory(DistributedCacheServiceArguments arguments)
        {
            _logger = arguments.Logger;
            _arguments = arguments;
            _redisContentSecretNames = arguments.Configuration.DistributedContentSettings.GetRedisConnectionSecretNames(arguments.HostInfo.StampId);
            _distributedSettings = arguments.Configuration.DistributedContentSettings;
            _keySpace = string.IsNullOrWhiteSpace(_arguments.Keyspace) ? RedisContentLocationStoreFactory.DefaultKeySpace : _arguments.Keyspace;
            _fileSystem = new PassThroughFileSystem(_logger);
            _secretRetriever = new DistributedCacheSecretRetriever(arguments);
            var bandwidthCheckedCopier = new BandwidthCheckedCopier(_arguments.Copier, BandwidthChecker.Configuration.FromDistributedContentSettings(_distributedSettings), _logger);

            _orderedResolvedCacheSettings = ResolveCacheSettingsInPrecedenceOrder(arguments);
            Contract.Assert(_orderedResolvedCacheSettings.Count != 0);

            RedisContentLocationStoreConfiguration = CreateRedisConfiguration();
            _distributedContentStoreSettings = CreateDistributedStoreSettings(_arguments, RedisContentLocationStoreConfiguration);

            _copier = new DistributedContentCopier<AbsolutePath>(
                _distributedContentStoreSettings,
                _fileSystem,
                fileCopier: bandwidthCheckedCopier,
                fileExistenceChecker: _arguments.Copier,
                _arguments.CopyRequester,
                _arguments.PathTransformer,
                _arguments.Overrides.Clock
            );

            _redisMemoizationStoreFactory = CreateRedisCacheFactory();
        }

        private static List<ResolvedNamedCacheSettings> ResolveCacheSettingsInPrecedenceOrder(DistributedCacheServiceArguments arguments)
        {
            var localCasSettings = arguments.Configuration.LocalCasSettings;
            var cacheSettingsByName = localCasSettings.CacheSettingsByCacheName;

            Dictionary<string, string> cacheNamesByDrive =
                cacheSettingsByName.ToDictionary(x => Path.GetPathRoot(x.Value.CacheRootPath), x => x.Key, StringComparer.OrdinalIgnoreCase);

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
                        machineLocation: arguments.PathTransformer.GetLocalMachineLocation(resolvedCacheRootPath)));
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

        private RedisMemoizationStoreFactory CreateRedisCacheFactory()
        {
            var contentHashBumpTime = TimeSpan.FromMinutes(_distributedSettings.ContentHashBumpTimeMinutes);

            // RedisContentSecretName and RedisMachineLocationsSecretName can be null. HostConnectionStringProvider won't fail in this case.
            IConnectionStringProvider contentConnectionStringProvider = TryCreateRedisConnectionStringProvider(_redisContentSecretNames.RedisContentSecretName);
            IConnectionStringProvider machineLocationsConnectionStringProvider = TryCreateRedisConnectionStringProvider(_redisContentSecretNames.RedisMachineLocationsSecretName);

            return new RedisMemoizationStoreFactory(
                contentConnectionStringProvider,
                machineLocationsConnectionStringProvider,
                _arguments.Overrides.Clock,
                contentHashBumpTime,
                _keySpace,
                configuration: RedisContentLocationStoreConfiguration,
                copier: _copier
            );
        }

        private RedisMemoizationStoreConfiguration CreateRedisConfiguration()
        {
            var primaryCacheRoot = OrderedResolvedCacheSettings[0].ResolvedCacheRootPath;

            var redisContentLocationStoreConfiguration = new RedisMemoizationStoreConfiguration
            {
                RedisBatchPageSize = _distributedSettings.RedisBatchPageSize,
                BlobExpiryTimeMinutes = _distributedSettings.BlobExpiryTimeMinutes,
                MaxBlobCapacity = _distributedSettings.MaxBlobCapacity,
                MaxBlobSize = _distributedSettings.MaxBlobSize,
                UseFullEvictionSort = _distributedSettings.UseFullEvictionSort,
                EvictionWindowSize = _distributedSettings.EvictionWindowSize,
                EvictionPoolSize = _distributedSettings.EvictionPoolSize,
                UpdateStaleLocalLastAccessTimes = _distributedSettings.UpdateStaleLocalLastAccessTimes,
                EvictionRemovalFraction = _distributedSettings.EvictionRemovalFraction,
                EvictionDiscardFraction = _distributedSettings.EvictionDiscardFraction,
                UseTieredDistributedEviction = _distributedSettings.UseTieredDistributedEviction,
                MemoizationExpiryTime = TimeSpan.FromMinutes(_distributedSettings.RedisMemoizationExpiryTimeMinutes),
                ProactiveCopyLocationsThreshold = _distributedSettings.ProactiveCopyLocationsThreshold,
                UseBinManager = _distributedSettings.UseBinManager || _distributedSettings.ProactiveCopyUsePreferredLocations,
                PreferredLocationsExpiryTime = TimeSpan.FromMinutes(_distributedSettings.PreferredLocationsExpiryTimeMinutes),
                PrimaryMachineLocation = OrderedResolvedCacheSettings[0].MachineLocation,
                AdditionalMachineLocations = OrderedResolvedCacheSettings.Skip(1).Select(r => r.MachineLocation).ToArray()
            };

            ApplyIfNotNull(_distributedSettings.RedisConnectionErrorLimit, v => redisContentLocationStoreConfiguration.RedisConnectionErrorLimit = v);

            ApplyIfNotNull(_distributedSettings.ReplicaCreditInMinutes, v => redisContentLocationStoreConfiguration.ContentLifetime = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MachineRisk, v => redisContentLocationStoreConfiguration.MachineRisk = v);
            ApplyIfNotNull(_distributedSettings.LocationEntryExpiryMinutes, v => redisContentLocationStoreConfiguration.LocationEntryExpiry = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MachineExpiryMinutes, v => redisContentLocationStoreConfiguration.MachineExpiry = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.TouchFrequencyMinutes, v => redisContentLocationStoreConfiguration.TouchFrequency = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.EvictionMinAgeMinutes, v => redisContentLocationStoreConfiguration.EvictionMinAge = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.RetryWindowSeconds, v => redisContentLocationStoreConfiguration.RetryWindow = TimeSpan.FromSeconds(v));

            redisContentLocationStoreConfiguration.ReputationTrackerConfiguration.Enabled = _distributedSettings.IsMachineReputationEnabled;

            if (_distributedSettings.IsContentLocationDatabaseEnabled)
            {
                var dbConfig = new RocksDbContentLocationDatabaseConfiguration(primaryCacheRoot / "LocationDb")
                {
                    StoreClusterState = _distributedSettings.StoreClusterStateInDatabase,
                    LogsKeepLongTerm = true,
                };

                if (_distributedSettings.ContentLocationDatabaseGcIntervalMinutes != null)
                {
                    dbConfig.GarbageCollectionInterval = TimeSpan.FromMinutes(_distributedSettings.ContentLocationDatabaseGcIntervalMinutes.Value);
                }

                if (_distributedSettings.EnableDistributedCache)
                {
                    dbConfig.MetadataGarbageCollectionEnabled = true;
                    dbConfig.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep = _distributedSettings.MaximumNumberOfMetadataEntriesToStore;
                }

                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseCacheEnabled, v => dbConfig.ContentCacheEnabled = v);
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseFlushDegreeOfParallelism, v => dbConfig.FlushDegreeOfParallelism = v);
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseFlushTransactionSize, v => dbConfig.FlushTransactionSize = v);
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseFlushSingleTransaction, v => dbConfig.FlushSingleTransaction = v);
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseFlushPreservePercentInMemory, v => dbConfig.FlushPreservePercentInMemory = v);
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseCacheMaximumUpdatesPerFlush, v => dbConfig.CacheMaximumUpdatesPerFlush = v);
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseCacheFlushingMaximumInterval, v => dbConfig.CacheFlushingMaximumInterval = v);

                ApplyIfNotNull(
                    _distributedSettings.FullRangeCompactionIntervalMinutes,
                    v => dbConfig.FullRangeCompactionInterval = TimeSpan.FromMinutes(v));
                ApplyIfNotNull(
                    _distributedSettings.FullRangeCompactionVariant,
                    v => {
                        if (Enum.TryParse<FullRangeCompactionVariant>(v, out var variant))
                        {
                            dbConfig.FullRangeCompactionVariant = variant;
                        }
                        else
                        {
                            throw new ArgumentException($"Failed to parse `{nameof(_distributedSettings.FullRangeCompactionVariant)}` setting with value `{_distributedSettings.FullRangeCompactionVariant}` into type `{nameof(FullRangeCompactionVariant)}`");
                        }
                    });
                ApplyIfNotNull(
                    _distributedSettings.FullRangeCompactionByteIncrementStep,
                    v => dbConfig.FullRangeCompactionByteIncrementStep = v);

                if (_distributedSettings.ContentLocationDatabaseLogsBackupEnabled)
                {
                    dbConfig.LogsBackupPath = primaryCacheRoot / "LocationDbLogs";
                }
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseLogsBackupRetentionMinutes, v => dbConfig.LogsRetention = TimeSpan.FromMinutes(v));

                redisContentLocationStoreConfiguration.Database = dbConfig;
                ApplySecretSettingsForLlsAsync(redisContentLocationStoreConfiguration, primaryCacheRoot, dbConfig).GetAwaiter().GetResult();
            }

            if (_distributedSettings.IsRedisGarbageCollectionEnabled)
            {
                redisContentLocationStoreConfiguration.GarbageCollectionConfiguration = new RedisGarbageCollectionConfiguration()
                {
                    MaximumEntryLastAccessTime = TimeSpan.FromMinutes(30)
                };
            }
            else
            {
                redisContentLocationStoreConfiguration.GarbageCollectionConfiguration = null;
            }

            _arguments.Overrides.Override(redisContentLocationStoreConfiguration);

            ConfigurationPrinter.TraceConfiguration(redisContentLocationStoreConfiguration, _logger);
            return redisContentLocationStoreConfiguration;
        }

        public async Task<IMemoizationStore> CreateMemoizationStoreAsync()
        {
            var cacheFactory = CreateRedisCacheFactory();
            await cacheFactory.StartupAsync(new Context(_logger)).ThrowIfFailure();
            return cacheFactory.CreateMemoizationStore(_logger);
        }

        public DistributedContentStore<AbsolutePath> CreateContentStore(ResolvedNamedCacheSettings resolvedSettings)
        {
            var contentStoreSettings = FromDistributedSettings(_distributedSettings);

            ConfigurationModel configurationModel = configurationModel 
                = new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota(resolvedSettings.Settings.CacheSizeQuotaString)));

            _logger.Debug("Creating a distributed content store");

            var contentStore =
                new DistributedContentStore<AbsolutePath>(
                    resolvedSettings.MachineLocation,
                    resolvedSettings.ResolvedCacheRootPath,
                    (announcer, evictionSettings, checkLocal, trimBulk) =>
                        ContentStoreFactory.CreateContentStore(_fileSystem, resolvedSettings.ResolvedCacheRootPath, announcer, distributedEvictionSettings: evictionSettings,
                            contentStoreSettings: contentStoreSettings, trimBulkAsync: trimBulk, configurationModel: configurationModel),
                    _redisMemoizationStoreFactory,
                    _distributedContentStoreSettings,
                    distributedCopier: _copier,
                    clock: _arguments.Overrides.Clock,
                    contentStoreSettings: contentStoreSettings);

            _logger.Debug("Created Distributed content store.");
            return contentStore;
        }

        private static DistributedContentStoreSettings CreateDistributedStoreSettings(
            DistributedCacheServiceArguments arguments,
            RedisContentLocationStoreConfiguration redisContentLocationStoreConfiguration)
        {
            var distributedSettings = arguments.Configuration.DistributedContentSettings;

            ContentAvailabilityGuarantee contentAvailabilityGuarantee;
            if (string.IsNullOrEmpty(distributedSettings.ContentAvailabilityGuarantee))
            {
                contentAvailabilityGuarantee = ContentAvailabilityGuarantee.FileRecordsExist;
            }
            else if (!Enum.TryParse(distributedSettings.ContentAvailabilityGuarantee, true, out contentAvailabilityGuarantee))
            {
                throw new ArgumentException($"Unable to parse {nameof(distributedSettings.ContentAvailabilityGuarantee)}: [{distributedSettings.ContentAvailabilityGuarantee}]");
            }

            PinConfiguration pinConfiguration = null;
            if (distributedSettings.IsPinBetterEnabled)
            {
                pinConfiguration = new PinConfiguration();
                ApplyIfNotNull(distributedSettings.PinRisk, v => pinConfiguration.PinRisk = v);
                ApplyIfNotNull(distributedSettings.MachineRisk, v => pinConfiguration.MachineRisk = v);
                ApplyIfNotNull(distributedSettings.FileRisk, v => pinConfiguration.FileRisk = v);
                ApplyIfNotNull(distributedSettings.MaxIOOperations, v => pinConfiguration.MaxIOOperations = v);

                pinConfiguration.IsPinCachingEnabled = distributedSettings.IsPinCachingEnabled;

                ApplyIfNotNull(distributedSettings.PinCacheReplicaCreditRetentionMinutes, v => pinConfiguration.PinCachePerReplicaRetentionCreditMinutes = v);
                ApplyIfNotNull(distributedSettings.PinCacheReplicaCreditRetentionDecay, v => pinConfiguration.PinCacheReplicaCreditRetentionFactor = v);
            }

            var contentHashBumpTime = TimeSpan.FromMinutes(distributedSettings.ContentHashBumpTimeMinutes);
            var lazyTouchContentHashBumpTime = distributedSettings.IsTouchEnabled ? (TimeSpan?)contentHashBumpTime : null;
            if (redisContentLocationStoreConfiguration.ReadMode == ContentLocationMode.LocalLocationStore)
            {
                // LocalLocationStore has its own internal notion of lazy touch/registration. We disable the lazy touch in distributed content store
                // because it can conflict with behavior of the local location store.
                lazyTouchContentHashBumpTime = null;
            }

            var distributedContentStoreSettings = new DistributedContentStoreSettings()
            {
                TrustedHashFileSizeBoundary = distributedSettings.TrustedHashFileSizeBoundary,
                ParallelHashingFileSizeBoundary = distributedSettings.ParallelHashingFileSizeBoundary,
                MaxConcurrentCopyOperations = distributedSettings.MaxConcurrentCopyOperations,
                PinConfiguration = pinConfiguration,
                RetryIntervalForCopies = distributedSettings.RetryIntervalForCopies,
                MaxRetryCount = distributedSettings.MaxRetryCount,
                TimeoutForProactiveCopies = TimeSpan.FromMinutes(distributedSettings.TimeoutForProactiveCopiesMinutes),
                ProactiveCopyMode = (ProactiveCopyMode)Enum.Parse(typeof(ProactiveCopyMode), distributedSettings.ProactiveCopyMode),
                PushProactiveCopies = distributedSettings.PushProactiveCopies,
                ProactiveCopyOnPut = distributedSettings.ProactiveCopyOnPut,
                ProactiveCopyOnPin = distributedSettings.ProactiveCopyOnPin,
                ProactiveCopyUsePreferredLocations = distributedSettings.ProactiveCopyUsePreferredLocations,
                MaxConcurrentProactiveCopyOperations = distributedSettings.MaxConcurrentProactiveCopyOperations,
                ProactiveCopyLocationsThreshold = distributedSettings.ProactiveCopyLocationsThreshold,
                ProactiveCopyRejectOldContent = distributedSettings.ProactiveCopyRejectOldContent,
                MaximumConcurrentPutFileOperations = distributedSettings.MaximumConcurrentPutFileOperations,
                ReplicaCreditInMinutes = distributedSettings.IsDistributedEvictionEnabled ? distributedSettings.ReplicaCreditInMinutes : null,
                EnableRepairHandling = distributedSettings.IsRepairHandlingEnabled,
                ContentHashBumpTime = lazyTouchContentHashBumpTime,
                LocationStoreBatchSize = distributedSettings.RedisBatchPageSize,
                ContentAvailabilityGuarantee = contentAvailabilityGuarantee,
                PrioritizeDesignatedLocationsOnCopies = distributedSettings.PrioritizeDesignatedLocationsOnCopies,
                RestrictedCopyReplicaCount = distributedSettings.RestrictedCopyReplicaCount,
                CopyAttemptsWithRestrictedReplicas = distributedSettings.CopyAttemptsWithRestrictedReplicas,
                AreBlobsSupported = redisContentLocationStoreConfiguration.AreBlobsSupported,
                MaxBlobSize = redisContentLocationStoreConfiguration.MaxBlobSize,
                DelayForProactiveReplication = TimeSpan.FromSeconds(distributedSettings.ProactiveReplicationDelaySeconds),
                ProactiveReplicationCopyLimit = distributedSettings.ProactiveReplicationCopyLimit,
                EnableProactiveReplication = distributedSettings.EnableProactiveReplication,
            };

            if (distributedSettings.EnableProactiveReplication && redisContentLocationStoreConfiguration.Checkpoint != null)
            {
                distributedContentStoreSettings.ProactiveReplicationInterval = redisContentLocationStoreConfiguration.Checkpoint.RestoreCheckpointInterval;
            }

            arguments.Overrides.Override(distributedContentStoreSettings);

            ConfigurationPrinter.TraceConfiguration(distributedContentStoreSettings, arguments.Logger);

            return distributedContentStoreSettings;
        }

        private DistributedContentStoreSettings Override(DistributedContentStoreSettings settings)
        {
            _arguments.Overrides.Override(settings);
            return settings;
        }

        private IConnectionStringProvider TryCreateRedisConnectionStringProvider(string secretName)
        {
            return string.IsNullOrEmpty(secretName)
                ? null
                : new HostConnectionStringProvider(_arguments.Host, secretName, _logger);
        }

        private static ContentStoreSettings FromDistributedSettings(DistributedContentSettings settings)
        {
            var result = new ContentStoreSettings()
            {
                CheckFiles = settings.CheckLocalFiles,
                UseNativeBlobEnumeration = settings.UseNativeBlobEnumeration,
                SelfCheckSettings = CreateSelfCheckSettings(settings),
                OverrideUnixFileAccessMode = settings.OverrideUnixFileAccessMode,
                UseRedundantPutFileShortcut = settings.UseRedundantPutFileShortcut,
                TraceFileSystemContentStoreDiagnosticMessages = settings.TraceFileSystemContentStoreDiagnosticMessages,

                SkipTouchAndLockAcquisitionWhenPinningFromHibernation = settings.UseFastHibernationPin,
            };

            ApplyIfNotNull(settings.SilentOperationDurationThreshold, v => result.SilentOperationDurationThreshold = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(settings.SilentOperationDurationThreshold, v => DefaultTracingConfiguration.DefaultSilentOperationDurationThreshold = TimeSpan.FromSeconds(v));
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

        private async Task ApplySecretSettingsForLlsAsync(
            RedisContentLocationStoreConfiguration configuration,
            AbsolutePath localCacheRoot,
            RocksDbContentLocationDatabaseConfiguration dbConfig)
        {
            (var secrets, var errors) = await _secretRetriever.TryRetrieveSecretsAsync();
            if (secrets == null)
            {
                _logger.Error($"Unable to configure Local Location Store. {errors}");
                return;
            }

            configuration.Checkpoint = new CheckpointConfiguration(localCacheRoot);

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
                _distributedSettings.SafeToLazilyUpdateMachineCountThreshold,
                value => configuration.SafeToLazilyUpdateMachineCountThreshold = value);

            configuration.EnableReconciliation = !_distributedSettings.Unsafe_DisableReconciliation;

            configuration.ReconciliationCycleFrequency = TimeSpan.FromMinutes(_distributedSettings.ReconciliationCycleFrequencyMinutes);
            configuration.ReconciliationMaxCycleSize = _distributedSettings.ReconciliationMaxCycleSize;

            ApplyIfNotNull(_distributedSettings.UseIncrementalCheckpointing, value => configuration.Checkpoint.UseIncrementalCheckpointing = value);
            ApplyIfNotNull(_distributedSettings.IncrementalCheckpointDegreeOfParallelism, value => configuration.Checkpoint.IncrementalCheckpointDegreeOfParallelism = value);

            configuration.RedisGlobalStoreConnectionString = ((PlainTextSecret)GetRequiredSecret(secrets, _distributedSettings.GlobalRedisSecretName)).Secret;
            if (_distributedSettings.SecondaryGlobalRedisSecretName != null)
            {
                configuration.RedisGlobalStoreSecondaryConnectionString = ((PlainTextSecret)GetRequiredSecret(
                    secrets,
                    _distributedSettings.SecondaryGlobalRedisSecretName)).Secret;
            }

            ApplyIfNotNull(
                _distributedSettings.ContentLocationReadMode,
                value => configuration.ReadMode = (ContentLocationMode)Enum.Parse(typeof(ContentLocationMode), value));
            ApplyIfNotNull(
                _distributedSettings.ContentLocationWriteMode,
                value => configuration.WriteMode = (ContentLocationMode)Enum.Parse(typeof(ContentLocationMode), value));
            ApplyIfNotNull(_distributedSettings.LocationEntryExpiryMinutes, value => configuration.LocationEntryExpiry = TimeSpan.FromMinutes(value));

            ApplyIfNotNull(_distributedSettings.RestoreCheckpointAgeThresholdMinutes, v => configuration.Checkpoint.RestoreCheckpointAgeThreshold = TimeSpan.FromMinutes(v));
            // Need to disable cleaning database on initialization when restore checkpoint age is set.
            ApplyIfNotNull(_distributedSettings.RestoreCheckpointAgeThresholdMinutes, v => dbConfig.CleanOnInitialize = false);

            var errorBuilder = new StringBuilder();
            var storageCredentials = GetStorageCredentials(secrets, errorBuilder);
            Contract.Assert(storageCredentials != null && storageCredentials.Length > 0);

            var blobStoreConfiguration = new BlobCentralStoreConfiguration(
                credentials: storageCredentials,
                containerName: "checkpoints",
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
                    PropagationDelay = TimeSpan.FromSeconds(
                                                                _distributedSettings.CentralStoragePropagationDelaySeconds),
                    PropagationIterations = _distributedSettings.CentralStoragePropagationIterations,
                    MaxSimultaneousCopies = _distributedSettings.CentralStorageMaxSimultaneousCopies
                };

                distributedCentralStoreConfiguration.TraceFileSystemContentStoreDiagnosticMessages = _distributedSettings.TraceFileSystemContentStoreDiagnosticMessages;

                ApplyIfNotNull(_distributedSettings.DistributedCentralStoragePeerToPeerCopyTimeoutSeconds, v => distributedCentralStoreConfiguration.PeerToPeerCopyTimeout = TimeSpan.FromSeconds(v));
                configuration.DistributedCentralStore = distributedCentralStoreConfiguration;
            }

            var eventStoreConfiguration = new EventHubContentLocationEventStoreConfiguration(
                eventHubName: "eventhub",
                eventHubConnectionString: ((PlainTextSecret)GetRequiredSecret(secrets, _distributedSettings.EventHubSecretName)).Secret,
                consumerGroupName: "$Default",
                epoch: _keySpace + _distributedSettings.EventHubEpoch);

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
        }

        private AzureBlobStorageCredentials[] GetStorageCredentials(Dictionary<string, Secret> secrets, StringBuilder errorBuilder)
        {
            var storageSecretNames = GetAzureStorageSecretNames(errorBuilder);
            // This would have failed earlier otherwise
            Contract.Assert(storageSecretNames != null);

            var credentials = new List<AzureBlobStorageCredentials>();
            foreach (var secretName in storageSecretNames)
            {
                var secret = GetRequiredSecret(secrets, secretName);

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

        private static void ApplyIfNotNull<T>(T value, Action<T> apply) where T : class
        {
            if (value != null)
            {
                apply(value);
            }
        }

        private static void ApplyIfNotNull<T>(T? value, Action<T> apply) where T : struct
        {
            if (value != null)
            {
                apply(value.Value);
            }
        }

        private static Secret GetRequiredSecret(Dictionary<string, Secret> secrets, string secretName)
        {
            if (!secrets.TryGetValue(secretName, out var value))
            {
                throw new KeyNotFoundException($"Missing secret: {secretName}");
            }

            return value;
        }

        private sealed class KeyVaultRetryPolicy : ITransientErrorDetectionStrategy
        {
            /// <inheritdoc />
            public bool IsTransient(Exception ex)
            {
                var message = ex.Message;

                if (message.Contains("The remote name could not be resolved"))
                {
                    // In some cases, KeyVault provider may fail with HttpRequestException with an inner exception like 'The remote name could not be resolved: 'login.windows.net'.
                    // Theoretically, this should be handled by the host, but to make error handling simple and consistent (this method throws one exception type) the handling is happening here.
                    // This is a recoverable error.
                    return true;
                }

                if (message.Contains("429"))
                {
                    // This is a throttling response which is recoverable as well.
                    return true;
                }

                return false;
            }
        }
    }
}
