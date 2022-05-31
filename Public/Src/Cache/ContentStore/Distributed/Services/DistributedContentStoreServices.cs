// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Utilities.Tracing;
using ProtoBuf.Grpc;
using RocksDbSharp;
using static BuildXL.Utilities.ConfigurationHelper;

namespace BuildXL.Cache.ContentStore.Distributed.Services
{
    /// <nodoc />
    public record DistributedContentStoreServicesArguments(
        DistributedContentSettings DistributedContentSettings,
        GrpcConnectionPool ConnectionPool,
        RedisContentLocationStoreConfiguration RedisContentLocationStoreConfiguration,
        DistributedCacheServiceHostOverrides Overrides,
        IDistributedServicesSecrets Secrets,
        AbsolutePath PrimaryCacheRoot,
        IAbsFileSystem FileSystem,
        DistributedContentCopier DistributedContentCopier)
    {
        public IClock Clock => Overrides.Clock;
    }

    /// <nodoc />
    public interface IDistributedServicesSecrets
    {
        /// <nodoc />
        Secret GetRequiredSecret(string secretName);

        /// <nodoc />
        AzureBlobStorageCredentials[] GetStorageCredentials(IEnumerable<string> storageSecretNames);
    }

    /// <summary>
    /// Services used by for distributed content store
    /// </summary>
    public class DistributedContentStoreServices : ServicesCreatorBase
    {
        /// <nodoc />
        public DistributedContentStoreServicesArguments Arguments { get; }

        /// <nodoc />
        private DistributedContentSettings DistributedContentSettings => Arguments.DistributedContentSettings;

        /// <nodoc />
        private RedisContentLocationStoreConfiguration RedisContentLocationStoreConfiguration => Arguments.RedisContentLocationStoreConfiguration;

        /// <nodoc />
        public OptionalServiceDefinition<GlobalCacheServiceConfiguration> GlobalCacheServiceConfiguration { get; }

        /// <nodoc />
        public OptionalServiceDefinition<GlobalCacheService> GlobalCacheService { get; }

        /// <nodoc />
        public OptionalServiceDefinition<ColdStorage> ColdStorage { get; }

        /// <nodoc />
        public OptionalServiceDefinition<IRoleObserver> RoleObserver { get; }

        /// <nodoc />
        public IServiceDefinition<ContentLocationStoreFactory> ContentLocationStoreFactory { get; }

        /// <nodoc />
        public IServiceDefinition<ContentLocationStoreServices> ContentLocationStoreServices { get; }

        internal IServiceDefinition<RedisWriteAheadEventStorage> RedisWriteAheadEventStorage { get; }

        internal IServiceDefinition<ICheckpointRegistry> CacheServiceCheckpointRegistry { get; }

        internal IServiceDefinition<AzureBlobStorageCheckpointRegistry> CacheServiceBlobCheckpointRegistry { get;  }

        internal DistributedContentStoreServices(DistributedContentStoreServicesArguments arguments)
        {
            Arguments = arguments;

            bool isGlobalCacheServiceEnabled = DistributedContentSettings.IsMasterEligible
                && RedisContentLocationStoreConfiguration.AllContentMetadataStoreModeFlags.HasAnyFlag(ContentMetadataStoreModeFlags.Distributed);

            GlobalCacheServiceConfiguration = CreateOptional(
                () => isGlobalCacheServiceEnabled,
                () => CreateGlobalCacheServiceConfiguration());

            GlobalCacheService = CreateOptional(
                () => isGlobalCacheServiceEnabled,
                () => CreateGlobalCacheService());

            RoleObserver = CreateOptional<IRoleObserver>(
                () => isGlobalCacheServiceEnabled && GlobalCacheService.InstanceOrDefault() is ResilientGlobalCacheService,
                () => (ResilientGlobalCacheService)GlobalCacheService.InstanceOrDefault());

            ContentLocationStoreServices = Create(() => ContentLocationStoreFactory.Instance.Services);

            ColdStorage = CreateOptional(() => DistributedContentSettings.ColdStorageSettings != null, () =>
            {
                return new ColdStorage(Arguments.FileSystem, DistributedContentSettings.ColdStorageSettings, Arguments.DistributedContentCopier);
            });

            ContentLocationStoreFactory = Create(() =>
            {
                return new ContentLocationStoreFactory(
                    new ContentLocationStoreFactoryArguments()
                    {
                        Clock = Arguments.Clock,
                        Copier = Arguments.DistributedContentCopier,
                        ConnectionPool = Arguments.ConnectionPool,
                        Dependencies = new ContentLocationStoreServicesDependencies()
                        {
                            GlobalCacheService = GlobalCacheService.UnsafeGetServiceDefinition().AsOptional<IGlobalCacheService>(),
                            ColdStorage = ColdStorage,
                            RoleObserver = RoleObserver,
                            DistributedContentSettings = CreateOptional(() => true, () => DistributedContentSettings)
                        },
                    },
                    Arguments.RedisContentLocationStoreConfiguration);
            });

            RedisWriteAheadEventStorage = Create(() => CreateRedisWriteAheadEventStorage());

            CacheServiceCheckpointRegistry = Create(() => CreateCacheServiceBlobCheckpointRegistry());
        }

        private GlobalCacheServiceConfiguration CreateGlobalCacheServiceConfiguration()
        {
            return new GlobalCacheServiceConfiguration()
            {
                EnableBackgroundRestoreCheckpoint = DistributedContentSettings.GlobalCacheBackgroundRestore,
                MaxOperationConcurrency = DistributedContentSettings.MetadataStoreMaxOperationConcurrency,
                MaxOperationQueueLength = DistributedContentSettings.MetadataStoreMaxOperationQueueLength,
                CheckpointMaxAge = DistributedContentSettings.ContentMetadataCheckpointMaxAge?.Value,
                MaxEventParallelism = RedisContentLocationStoreConfiguration.EventStore.MaxEventProcessingConcurrency,
                MasterLeaseStaleThreshold = DateTimeUtilities.Multiply(RedisContentLocationStoreConfiguration.Checkpoint.MasterLeaseExpiryTime, 0.5),
                PersistentEventStorage = new BlobEventStorageConfiguration()
                {
                    Credentials = Arguments.Secrets.GetStorageCredentials(new[] { DistributedContentSettings.ContentMetadataBlobSecretName }).First(),
                    FolderName = "events" + DistributedContentSettings.KeySpacePrefix,
                    ContainerName = DistributedContentSettings.ContentMetadataLogBlobContainerName,
                },
                CentralStorage = RedisContentLocationStoreConfiguration.CentralStore with
                {
                    ContainerName = DistributedContentSettings.ContentMetadataCentralStorageContainerName
                },
                EventStream = new ContentMetadataEventStreamConfiguration()
                {
                    BatchWriteAheadWrites = DistributedContentSettings.ContentMetadataBatchVolatileWrites,
                    ShutdownTimeout = DistributedContentSettings.ContentMetadataShutdownTimeout,
                    LogBlockRefreshInterval = DistributedContentSettings.ContentMetadataPersistInterval
                },
                Checkpoint = RedisContentLocationStoreConfiguration.Checkpoint with
                {
                    WorkingDirectory = Arguments.PrimaryCacheRoot / "cmschkpt"
                },
            };
        }

        internal RedisWriteAheadEventStorage CreateRedisWriteAheadEventStorage()
        {
            Contract.Assert(!DistributedContentSettings.PreventRedisUsage, "Attempt to use Redis when it is disabled");

            var configuration = GlobalCacheServiceConfiguration.GetRequiredInstance();
            var clock = Arguments.Clock;

            var redisVolatileEventStorageConfiguration = new RedisVolatileEventStorageConfiguration()
            {
                ConnectionString = (Arguments.Secrets.GetRequiredSecret(DistributedContentSettings.ContentMetadataRedisSecretName) as PlainTextSecret).Secret,
                KeyPrefix = DistributedContentSettings.RedisWriteAheadKeyPrefix,
                MaximumKeyLifetime = DistributedContentSettings.ContentMetadataRedisMaximumKeyLifetime,
            };

            return new RedisWriteAheadEventStorage(
                    redisVolatileEventStorageConfiguration,
                    new ConfigurableRedisDatabaseFactory(RedisContentLocationStoreConfiguration),
                    clock);
        }

        internal AzureBlobStorageCheckpointRegistry CreateCacheServiceBlobCheckpointRegistry()
        {
            var clock = Arguments.Clock;
            
            var storageRegistryConfiguration = new AzureBlobStorageCheckpointRegistryConfiguration()
            {
                Credentials = Arguments.Secrets.GetStorageCredentials(new[] { DistributedContentSettings.ContentMetadataBlobSecretName }).First(),
                FolderName = "checkpointRegistry" + DistributedContentSettings.KeySpacePrefix,
                ContainerName = DistributedContentSettings.ContentMetadataBlobCheckpointRegistryContainerName,
                KeySpacePrefix = DistributedContentSettings.KeySpacePrefix,
            };

            ApplyIfNotNull(DistributedContentSettings.BlobCheckpointRegistryFanout, v => storageRegistryConfiguration.CheckpointContentFanOut = v);

            var storageRegistry = new AzureBlobStorageCheckpointRegistry(
                storageRegistryConfiguration,
                RedisContentLocationStoreConfiguration.PrimaryMachineLocation,
                clock);
            storageRegistry.WorkaroundTracer = new Tracer("ContentMetadataAzureBlobStorageCheckpointRegistry");

            return storageRegistry;
        }

        private GlobalCacheService CreateGlobalCacheService()
        {
            var primaryCacheRoot = Arguments.PrimaryCacheRoot;
            var configuration = GlobalCacheServiceConfiguration.GetRequiredInstance();
            var clock = Arguments.Clock;
            CentralStreamStorage centralStreamStorage = configuration.CentralStorage.CreateCentralStorage();

            var dbConfig = new RocksDbContentMetadataDatabaseConfiguration(primaryCacheRoot / "cms")
            {
                // Setting to false, until we have persistence for the db
                CleanOnInitialize = false,
                StoreExpandedContent = DistributedContentSettings.ContentMetadataOptimizeWrites
            };

            ApplyIfNotNull(DistributedContentSettings.LocationEntryExpiryMinutes, v => dbConfig.ContentRotationInterval = TimeSpan.FromMinutes(v));
            ApplyEnumIfNotNull<Compression>(DistributedContentSettings.ContentLocationDatabaseCompression, v => dbConfig.Compression = v);
            dbConfig.BlobRotationInterval = TimeSpan.FromMinutes(DistributedContentSettings.BlobExpiryTimeMinutes);
            dbConfig.MetadataRotationInterval = DistributedContentSettings.ContentMetadataServerMetadataRotationInterval;

            var store = new RocksDbContentMetadataStore(
                clock,
                new RocksDbContentMetadataStoreConfiguration()
                {
                    DisableRegisterLocation = DistributedContentSettings.ContentMetadataDisableDatabaseRegisterLocation,
                    MaxBlobCapacity = DistributedContentSettings.MaxBlobCapacity,
                    Database = dbConfig,
                });

            if (!DistributedContentSettings.ContentMetadataEnableResilience)
            {
                return new GlobalCacheService(store);
            }
            else
            {
                IWriteAheadEventStorage volatileEventStorage;
                if (DistributedContentSettings.UseBlobVolatileStorage)
                {
                    var volatileConfig = configuration.PersistentEventStorage with
                    {
                        Credentials = DistributedContentSettings.GlobalCacheWriteAheadBlobSecretName != null
                            ? Arguments.Secrets.GetStorageCredentials(new[] { DistributedContentSettings.GlobalCacheWriteAheadBlobSecretName })[0]
                            : configuration.PersistentEventStorage.Credentials,
                        ContainerName = "volatileeventstorage"
                    };

                    volatileEventStorage = new BlobWriteAheadEventStorage(volatileConfig);
                }
                else
                {
                    volatileEventStorage = RedisWriteAheadEventStorage.Instance;
                }

                CentralStorage centralStorage = centralStreamStorage;

                var blobCheckpointRegistry = CacheServiceCheckpointRegistry.Instance as AzureBlobStorageCheckpointRegistry;

                if (RedisContentLocationStoreConfiguration.DistributedCentralStore != null)
                {
                    var metadataConfig = RedisContentLocationStoreConfiguration.DistributedCentralStore with
                    {
                        CacheRoot = configuration.Checkpoint.WorkingDirectory
                    };

                    if (DistributedContentSettings.CheckpointDistributionMode.Value == CheckpointDistributionModes.Proxy
                        && blobCheckpointRegistry != null)
                    {
                        var dcs = new DistributedCentralStorage(
                            metadataConfig,
                            blobCheckpointRegistry,
                            Arguments.DistributedContentCopier,
                            fallbackStorage: centralStorage,
                            clock);

                        centralStorage = dcs;
                    }
                    else
                    {
                        var cachingCentralStorage = new CachingCentralStorage(
                            metadataConfig,
                            centralStorage,
                            Arguments.DistributedContentCopier.FileSystem);

                        centralStorage = cachingCentralStorage;
                    }
                }

                var persistentEventStorage = Arguments.Overrides.Override(new BlobWriteBehindEventStorage(configuration.PersistentEventStorage));

                var checkpointManager = new CheckpointManager(
                    store.Database,
                    CacheServiceCheckpointRegistry.Instance,
                    centralStorage,
                    configuration.Checkpoint,
                    new CounterCollection<ContentLocationStoreCounters>(),
                    checkpointObserver: RedisContentLocationStoreConfiguration.DistributedCentralStore?.TrackCheckpointConsumers == true ? blobCheckpointRegistry : null);

                // This is done to ensure logging in Kusto is shown under a separate component. The need for this comes
                // from the fact that CheckpointManager per-se is used in our Kusto dashboards and monitoring queries to
                // mean "LLS' checkpoint"
                checkpointManager.WorkaroundTracer = new Tracer($"MetadataServiceCheckpointManager");

                var eventStream = new ContentMetadataEventStream(
                    configuration.EventStream,
                    Arguments.Overrides.Override(volatileEventStorage),
                    persistentEventStorage);

                var service = new ResilientGlobalCacheService(
                    configuration,
                    checkpointManager,
                    store,
                    eventStream,
                    centralStreamStorage,
                    clock);

                return service;
            }
        }
    }
}
