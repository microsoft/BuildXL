// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
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
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
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

        internal string MachineName { get; set; } = Environment.MachineName;

        public DistributedContentStoreFactory(
            DistributedCacheServiceArguments arguments,
            RedisContentSecretNames redisContentSecretNames,
            DistributedCacheSecretRetriever secretRetriever)
        {
            _logger = arguments.Logger;
            _arguments = arguments;
            _redisContentSecretNames = redisContentSecretNames;
            _distributedSettings = arguments.Configuration.DistributedContentSettings;
            _keySpace = string.IsNullOrWhiteSpace(_arguments.Keyspace) ? RedisContentLocationStoreFactory.DefaultKeySpace : _arguments.Keyspace;
            _fileSystem = new PassThroughFileSystem(_logger);
            _secretRetriever = secretRetriever;
        }

        public RedisMemoizationStoreFactory CreateRedisCacheFactory(AbsolutePath localCacheRoot, out RedisContentLocationStoreConfiguration config)
        {
            var redisContentLocationStoreConfiguration = new RedisMemoizationStoreConfiguration
            {
                RedisBatchPageSize = _distributedSettings.RedisBatchPageSize,
                BlobExpiryTimeMinutes = _distributedSettings.BlobExpiryTimeMinutes,
                MaxBlobCapacity = _distributedSettings.MaxBlobCapacity,
                MaxBlobSize = _distributedSettings.MaxBlobSize,
                EvictionWindowSize = _distributedSettings.EvictionWindowSize,
                EvictionPoolSize = _distributedSettings.EvictionPoolSize,
                EvictionRemovalFraction = _distributedSettings.EvictionRemovalFraction,
                MemoizationExpiryTime = TimeSpan.FromMinutes(_distributedSettings.RedisMemoizationExpiryTimeMinutes)
            };

            ApplyIfNotNull(_distributedSettings.ReplicaCreditInMinutes, v => redisContentLocationStoreConfiguration.ContentLifetime = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MachineRisk, v => redisContentLocationStoreConfiguration.MachineRisk = v);
            ApplyIfNotNull(_distributedSettings.LocationEntryExpiryMinutes, v => redisContentLocationStoreConfiguration.LocationEntryExpiry = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.RestoreCheckpointAgeThresholdMinutes, v => redisContentLocationStoreConfiguration.Checkpoint.RestoreCheckpointAgeThreshold = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(_distributedSettings.MachineExpiryMinutes, v => redisContentLocationStoreConfiguration.MachineExpiry = TimeSpan.FromMinutes(v));

            redisContentLocationStoreConfiguration.ReputationTrackerConfiguration.Enabled = _distributedSettings.IsMachineReputationEnabled;

            if (_distributedSettings.IsContentLocationDatabaseEnabled)
            {
                var dbConfig = new RocksDbContentLocationDatabaseConfiguration(localCacheRoot / "LocationDb")
                {
                    StoreClusterState = _distributedSettings.StoreClusterStateInDatabase
                };

                if (_distributedSettings.ContentLocationDatabaseGcIntervalMinutes != null)
                {
                    dbConfig.GarbageCollectionInterval = TimeSpan.FromMinutes(_distributedSettings.ContentLocationDatabaseGcIntervalMinutes.Value);
                }

                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseCacheEnabled, v => dbConfig.ContentCacheEnabled = v);
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseFlushDegreeOfParallelism, v => dbConfig.FlushDegreeOfParallelism = v);
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseFlushSingleTransaction, v => dbConfig.FlushSingleTransaction = v);
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseFlushPreservePercentInMemory, v => dbConfig.FlushPreservePercentInMemory = v);
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseCacheMaximumUpdatesPerFlush, v => dbConfig.CacheMaximumUpdatesPerFlush = v);
                ApplyIfNotNull(_distributedSettings.ContentLocationDatabaseCacheFlushingMaximumInterval, v => dbConfig.CacheFlushingMaximumInterval = v);

                ApplyIfNotNull(
                    _distributedSettings.FullRangeCompactionIntervalMinutes,
                    v => dbConfig.FullRangeCompactionInterval = TimeSpan.FromMinutes(v));

                redisContentLocationStoreConfiguration.Database = dbConfig;
                ApplySecretSettingsForLlsAsync(redisContentLocationStoreConfiguration, localCacheRoot).GetAwaiter().GetResult();
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

            var contentHashBumpTime = TimeSpan.FromMinutes(_distributedSettings.ContentHashBumpTimeMinutes);

            // RedisContentSecretName and RedisMachineLocationsSecretName can be null. HostConnectionStringProvider won't fail in this case.
            IConnectionStringProvider contentConnectionStringProvider = TryCreateRedisConnectionStringProvider(_redisContentSecretNames.RedisContentSecretName);
            IConnectionStringProvider machineLocationsConnectionStringProvider = TryCreateRedisConnectionStringProvider(_redisContentSecretNames.RedisMachineLocationsSecretName);

            config = redisContentLocationStoreConfiguration;

            return new RedisMemoizationStoreFactory(
                contentConnectionStringProvider,
                machineLocationsConnectionStringProvider,
                SystemClock.Instance,
                contentHashBumpTime,
                _keySpace,
                configuration: redisContentLocationStoreConfiguration
                );
        }

        public async Task<IMemoizationStore> CreateMemoizationStoreAsync(AbsolutePath localCacheRoot)
        {
            var cacheFactory = CreateRedisCacheFactory(localCacheRoot, out var _);
            await cacheFactory.StartupAsync(new Context(_logger)).ThrowIfFailure();
            return cacheFactory.CreateMemoizationStore(_logger);
        }

        public IContentStore CreateContentStore(AbsolutePath localCacheRoot)
        {
            var redisContentLocationStoreFactory = CreateRedisCacheFactory(localCacheRoot, out var redisContentLocationStoreConfiguration);

            var localMachineLocation = _arguments.PathTransformer.GetLocalMachineLocation(localCacheRoot);

            ReadOnlyDistributedContentSession<AbsolutePath>.ContentAvailabilityGuarantee contentAvailabilityGuarantee;
            if (string.IsNullOrEmpty(_distributedSettings.ContentAvailabilityGuarantee))
            {
                contentAvailabilityGuarantee =
                    ReadOnlyDistributedContentSession<AbsolutePath>.ContentAvailabilityGuarantee
                        .FileRecordsExist;
            }
            else if (!Enum.TryParse(_distributedSettings.ContentAvailabilityGuarantee, true, out contentAvailabilityGuarantee))
            {
                throw new ArgumentException($"Unable to parse {nameof(_distributedSettings.ContentAvailabilityGuarantee)}: [{_distributedSettings.ContentAvailabilityGuarantee}]");
            }

            PinConfiguration pinConfiguration = null;
            if (_distributedSettings.IsPinBetterEnabled)
            {
                pinConfiguration = new PinConfiguration();
                if (_distributedSettings.PinRisk.HasValue) { pinConfiguration.PinRisk = _distributedSettings.PinRisk.Value; }
                if (_distributedSettings.MachineRisk.HasValue) { pinConfiguration.MachineRisk = _distributedSettings.MachineRisk.Value; }
                if (_distributedSettings.FileRisk.HasValue) { pinConfiguration.FileRisk = _distributedSettings.FileRisk.Value; }
                if (_distributedSettings.MaxIOOperations.HasValue) { pinConfiguration.MaxIOOperations = _distributedSettings.MaxIOOperations.Value; }

                pinConfiguration.UsePinCache = _distributedSettings.IsPinCachingEnabled;

                if (_distributedSettings.PinCacheReplicaCreditRetentionMinutes.HasValue) { pinConfiguration.PinCacheReplicaCreditRetentionMinutes = _distributedSettings.PinCacheReplicaCreditRetentionMinutes.Value; }
                if (_distributedSettings.PinCacheReplicaCreditRetentionDecay.HasValue) { pinConfiguration.PinCacheReplicaCreditRetentionDecay = _distributedSettings.PinCacheReplicaCreditRetentionDecay.Value; }
            }

            var contentHashBumpTime = TimeSpan.FromMinutes(_distributedSettings.ContentHashBumpTimeMinutes);
            var lazyTouchContentHashBumpTime = _distributedSettings.IsTouchEnabled ? (TimeSpan?)contentHashBumpTime : null;
            if (redisContentLocationStoreConfiguration.ReadMode == ContentLocationMode.LocalLocationStore)
            {
                // LocalLocationStore has its own internal notion of lazy touch/registration. We disable the lazy touch in distributed content store
                // because it can conflict with behavior of the local location store.
                lazyTouchContentHashBumpTime = null;
            }

            var contentStoreSettings = FromDistributedSettings(_distributedSettings);

            ConfigurationModel configurationModel = null;
            if (_arguments.Configuration.LocalCasSettings.CacheSettingsByCacheName.TryGetValue(_arguments.Configuration.LocalCasSettings.CasClientSettings.DefaultCacheName, out var namedCacheSettings))
            {
                configurationModel = new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota(namedCacheSettings.CacheSizeQuotaString)));
            }

            var bandwidthCheckedCopier = new BandwidthCheckedCopier(_arguments.Copier, BandwidthChecker.Configuration.FromDistributedContentSettings(_distributedSettings), _logger);

            _logger.Debug("Creating a distributed content store for Autopilot");
            var contentStore =
                new DistributedContentStore<AbsolutePath>(
                    localMachineLocation,
                    (announcer, evictionSettings, checkLocal, trimBulk) =>
                        ContentStoreFactory.CreateContentStore(_fileSystem, localCacheRoot, announcer, distributedEvictionSettings: evictionSettings,
                            contentStoreSettings: contentStoreSettings, trimBulkAsync: trimBulk, configurationModel: configurationModel),
                    redisContentLocationStoreFactory,
                    _arguments.Copier,
                    bandwidthCheckedCopier,
                    _arguments.PathTransformer,
                    _arguments.CopyRequester,
                    contentAvailabilityGuarantee,
                    localCacheRoot,
                    _fileSystem,
                    _distributedSettings.RedisBatchPageSize,
                    new DistributedContentStoreSettings()
                    {
                        UseTrustedHash = _distributedSettings.UseTrustedHash,
                        CleanRandomFilesAtRoot = _distributedSettings.CleanRandomFilesAtRoot,
                        TrustedHashFileSizeBoundary = _distributedSettings.TrustedHashFileSizeBoundary,
                        ParallelHashingFileSizeBoundary = _distributedSettings.ParallelHashingFileSizeBoundary,
                        MaxConcurrentCopyOperations = _distributedSettings.MaxConcurrentCopyOperations,
                        PinConfiguration = pinConfiguration,
                        EmptyFileHashShortcutEnabled = _distributedSettings.EmptyFileHashShortcutEnabled,
                        RetryIntervalForCopies = _distributedSettings.RetryIntervalForCopies,
                        MaxRetryCount = _distributedSettings.MaxRetryCount,
                        TimeoutForProactiveCopies = TimeSpan.FromMinutes(_distributedSettings.TimeoutForProactiveCopiesMinutes),
                        ProactiveCopyMode = (ProactiveCopyMode)Enum.Parse(typeof(ProactiveCopyMode), _distributedSettings.ProactiveCopyMode),
                        MaxConcurrentProactiveCopyOperations = _distributedSettings.MaxConcurrentProactiveCopyOperations,
                        ProactiveCopyLocationsThreshold = _distributedSettings.ProactiveCopyLocationsThreshold,
                        MaximumConcurrentPutFileOperations = _distributedSettings.MaximumConcurrentPutFileOperations,
                    },
                    replicaCreditInMinutes: _distributedSettings.IsDistributedEvictionEnabled ? _distributedSettings.ReplicaCreditInMinutes : null,
                    enableRepairHandling: _distributedSettings.IsRepairHandlingEnabled,
                    contentHashBumpTime: lazyTouchContentHashBumpTime,
                    contentStoreSettings: contentStoreSettings);
            _logger.Debug("Created Distributed content store.");
            return contentStore;
        }

        private IConnectionStringProvider TryCreateRedisConnectionStringProvider(string secretName)
        {
            return string.IsNullOrEmpty(secretName)
                ? null
                : new HostConnectionStringProvider(_arguments.Host, secretName, _logger);
        }

        private static ContentStoreSettings FromDistributedSettings(DistributedContentSettings settings)
        {
            return new ContentStoreSettings()
                   {
                       UseEmptyFileHashShortcut = settings.EmptyFileHashShortcutEnabled,
                       CheckFiles = settings.CheckLocalFiles,
                       UseNativeBlobEnumeration = settings.UseNativeBlobEnumeration,
                       SelfCheckSettings = CreateSelfCheckSettings(settings),
                       OverrideUnixFileAccessMode = settings.OverrideUnixFileAccessMode,
                       UseRedundantPutFileShortcut = settings.UseRedundantPutFileShortcut,
                       TraceFileSystemContentStoreDiagnosticMessages = settings.TraceFileSystemContentStoreDiagnosticMessages,
                   };
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

        private async Task ApplySecretSettingsForLlsAsync(RedisContentLocationStoreConfiguration configuration, AbsolutePath localCacheRoot)
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

            configuration.RedisGlobalStoreConnectionString = ((PlainTextSecret) GetRequiredSecret(secrets, _distributedSettings.GlobalRedisSecretName)).Secret;

            if (_distributedSettings.SecondaryGlobalRedisSecretName != null)
            {
                configuration.RedisGlobalStoreSecondaryConnectionString = ((PlainTextSecret) GetRequiredSecret(
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
                configuration.DistributedCentralStore = new DistributedCentralStoreConfiguration(localCacheRoot)
                {
                    MaxRetentionGb = _distributedSettings.MaxCentralStorageRetentionGb,
                    PropagationDelay = TimeSpan.FromSeconds(
                                                                _distributedSettings.CentralStoragePropagationDelaySeconds),
                    PropagationIterations = _distributedSettings.CentralStoragePropagationIterations,
                    MaxSimultaneousCopies = _distributedSettings.CentralStorageMaxSimultaneousCopies
                };
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

                    credentials.Add(CreateAzureBlobCredentialsFromSasToken(secretName, updatingSasToken));
                }
                else
                {
                    var plainTextSecret = secret as PlainTextSecret;
                    Contract.Assert(!(plainTextSecret is null));

                    credentials.Add(new AzureBlobStorageCredentials(plainTextSecret.Secret));
                }
            }

            return credentials.ToArray();
        }

        private AzureBlobStorageCredentials CreateAzureBlobCredentialsFromSasToken(string secretName, UpdatingSasToken updatingSasToken)
        {
            var storageCredentials = new StorageCredentials(sasToken: updatingSasToken.Token.Token);
            updatingSasToken.TokenUpdated += (_, sasToken) =>
            {
                _logger.Debug($"Updating SAS token for Azure Storage secret {secretName}");
                storageCredentials.UpdateSASToken(sasToken.Token);
            };

            // The account name should never actually be updated, so its OK to take it from the initial token
            var azureCredentials = new AzureBlobStorageCredentials(storageCredentials, updatingSasToken.Token.StorageAccount);
            return azureCredentials;
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
