// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Tracing;

#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.Services
{
    /// <summary>
    /// Dependencies of <see cref="ContentLocationStoreServices"/>
    /// </summary>
    public record ContentLocationStoreServicesDependencies
    {
        /// <nodoc />
        public OptionalServiceDefinition<IGlobalCacheService> GlobalCacheService { get; init; }

        /// <nodoc />
        public OptionalServiceDefinition<ColdStorage> ColdStorage { get; init; }

        /// <nodoc />
        public OptionalServiceDefinition<IRoleObserver> RoleObserver { get; init; }

        /// <nodoc />
        public OptionalServiceDefinition<DistributedContentSettings> DistributedContentSettings { get; init; }

        /// <nodoc />
        public OptionalServiceDefinition<CheckpointManager> GlobalCacheCheckpointManager { get; init; }

        /// <nodoc />
        public OptionalServiceDefinition<RocksDbContentMetadataStore> GlobalCacheDatabaseStore { get; init; }
    }

    /// <summary>
    /// Services used by <see cref="ContentLocationStoreFactory"/>
    /// </summary>
    public class ContentLocationStoreServices : ServicesCreatorBase
    {
        /// <nodoc />
        public ContentLocationStoreServicesDependencies Dependencies => Arguments.Dependencies;

        /// <nodoc />
        public DistributedContentSettings? DistributedContentSettings => Dependencies.DistributedContentSettings.InstanceOrDefault();

        /// <nodoc />
        public RedisContentLocationStoreConfiguration Configuration { get; }

        /// <nodoc />
        public ContentLocationStoreFactoryArguments Arguments { get; }

        /// <nodoc />
        protected IClock Clock => Arguments.Clock;

        /// <nodoc />
        protected DistributedContentCopier Copier => Arguments.Copier;

        /// <nodoc />
        protected string KeySpace => Configuration.Keyspace;

        /// <nodoc />
        public OptionalServiceDefinition<ClientGlobalCacheStore> ClientGlobalCacheStore { get; }

        /// <nodoc />
        public IServiceDefinition<IClientAccessor<IGlobalCacheService>> MasterClientAccessor { get; }

        /// <nodoc />
        public IServiceDefinition<RedisGlobalStore> RedisGlobalStore { get; }

        /// <nodoc />
        public IServiceDefinition<IMasterElectionMechanism> MasterElectionMechanism { get; }

        /// <nodoc />
        public IServiceDefinition<IGlobalCacheStore> GlobalCacheStore { get; }

        /// <nodoc />
        public IServiceDefinition<ClusterStateManager> ClusterStateManager { get; }

        /// <nodoc />
        public IServiceDefinition<LocalLocationStore> LocalLocationStore { get; }

        /// <nodoc />
        /// <nodoc />
        public IServiceDefinition<CentralStreamStorage> CentralStorage { get; }

        /// <nodoc />
        public IServiceDefinition<CheckpointManager> CheckpointManager { get; }

        /// <nodoc />
        public IServiceDefinition<AzureBlobStorageCheckpointRegistry> CheckpointRegistry { get; }

        /// <nodoc />
        public OptionalServiceDefinition<BlobContentLocationRegistry> BlobContentLocationRegistry { get; }

        public ContentLocationStoreServices(
            ContentLocationStoreFactoryArguments arguments,
            RedisContentLocationStoreConfiguration configuration)
        {
            Configuration = configuration;
            Arguments = arguments;

            var context = arguments.Copier.Context;

            RedisGlobalStore = Create(() => CreateRedisGlobalStore(context));

            MasterElectionMechanism = Create(() => CreateMasterElectionMechanism());

            MasterClientAccessor = Create(() => CreateMasterClientAccessor());

            ClientGlobalCacheStore = CreateOptional(() => Configuration.MetadataStore != null, () => CreateClientGlobalCacheStore());

            GlobalCacheStore = Create(() => CreateGlobalCacheStore());

            LocalLocationStore = Create(() => CreateLocalLocationStore());

            ClusterStateManager = Create(() => CreateClusterStateManager());

            CheckpointRegistry = Create(() => CreateCheckpointRegistry());

            CheckpointManager = Create(() => CreateCheckpointManager());

            CentralStorage = Create(() => CreateCentralStorage());

            BlobContentLocationRegistry = CreateOptional(
                () => Dependencies.DistributedContentSettings.InstanceOrDefault()?.LocationStoreSettings?.EnableBlobContentLocationRegistry == true
                && !Configuration.DistributedContentConsumerOnly,
                () => CreateBlobContentLocationRegistry());
        }

        private BlobContentLocationRegistry CreateBlobContentLocationRegistry()
        {
            var baseConfiguration = Arguments.Dependencies.DistributedContentSettings.GetRequiredInstance().LocationStoreSettings.BlobContentLocationRegistrySettings ?? new();
            var database = (RocksDbContentMetadataDatabase)Dependencies.GlobalCacheCheckpointManager.GetRequiredInstance().Database;
            return new BlobContentLocationRegistry(
                baseConfiguration with
                {
                    Credentials = Configuration.AzureBlobStorageCheckpointRegistryConfiguration!.Credentials,
                },
                ClusterStateManager.Instance,
                Configuration.PrimaryMachineLocation,
                database,
                localContentStore: null, // Set to null initially. Will be populated when content location store is initialized similar to LLS.
                clock: Arguments.Clock);
        }

        private ClientGlobalCacheStore CreateClientGlobalCacheStore()
        {
            return new ClientGlobalCacheStore(MasterClientAccessor.Instance, Configuration.MetadataStore!);
        }

        private CheckpointManager CreateCheckpointManager()
        {
            Contract.Assert(Configuration.IsValidForLls());

            if (DistributedContentSettings?.UseGlobalCacheDatabaseInLocalLocationStore == true
                // Only replace LLS DB/CheckpointManager on worker machines
                && !Dependencies.GlobalCacheService.IsAvailable
                && DistributedContentSettings?.IsMasterEligible == false
                && Dependencies.GlobalCacheCheckpointManager.TryGetInstance(out var checkpointManager))
            {
                return checkpointManager;
            }

            CentralStorage centralStorage = CentralStorage.Instance;
            if (Configuration.DistributedCentralStore != null)
            {
                centralStorage = new DistributedCentralStorage(
                    Configuration.DistributedCentralStore,
                    new DistributedCentralStorageLocationStoreAdapter(() => LocalLocationStore.Instance),
                    Copier,
                    fallbackStorage: centralStorage,
                    clock: Clock);
            }

            var clusterStateManager = ClusterStateManager.Instance;
            var database = ContentLocationDatabase.Create(
                Clock,
                Configuration.Database!,
                () => clusterStateManager.ClusterState.InactiveMachineList);

            return new CheckpointManager(
                database,
                CheckpointRegistry.Instance,
                centralStorage,
                Configuration.Checkpoint,
                new CounterCollection<ContentLocationStoreCounters>());
        }

        private LocalLocationStore CreateLocalLocationStore()
        {
            return new LocalLocationStore(
                Clock,
                GlobalCacheStore.Instance,
                Configuration,
                CheckpointManager.Instance,
                MasterElectionMechanism.Instance,
                ClusterStateManager.Instance,
                Dependencies?.ColdStorage.InstanceOrDefault());
        }

        private CentralStreamStorage CreateCentralStorage()
        {
            return Configuration.CentralStore!.CreateCentralStorage();
        }

        private AzureBlobStorageCheckpointRegistry CreateCheckpointRegistry()
        {
            Contract.RequiresNotNull(Configuration.AzureBlobStorageCheckpointRegistryConfiguration);
            return new AzureBlobStorageCheckpointRegistry(Configuration.AzureBlobStorageCheckpointRegistryConfiguration, Configuration.PrimaryMachineLocation, Clock);
        }

        private IGlobalCacheStore CreateGlobalCacheStore()
        {
            if (Configuration.AllContentMetadataStoreModeFlags.HasAnyFlag(ContentMetadataStoreModeFlags.Distributed)
                && ClientGlobalCacheStore.TryGetInstance(out var distributedStore))
            {
                if (!Configuration.AllContentMetadataStoreModeFlags.HasAnyFlag(ContentMetadataStoreModeFlags.Redis))
                {
                    return distributedStore;
                }

                return new TransitioningGlobalCacheStore(Configuration, RedisGlobalStore.Instance, distributedStore);
            }
            else
            {
                return RedisGlobalStore.Instance;
            }
        }

        private IClientAccessor<IGlobalCacheService> CreateMasterClientAccessor()
        {
            var localClient = Dependencies.GlobalCacheService.TryGetInstance(out var localService)
                                ? new LocalClient<IGlobalCacheService>(Configuration.PrimaryMachineLocation, localService)
                                : null;
            var clientPool = new GrpcClientPool<IGlobalCacheService>(Arguments.ConnectionPool, localClient);

            return new GrpcMasterClientFactory<IGlobalCacheService>(clientPool, MasterElectionMechanism.Instance);
        }

        private IMasterElectionMechanism CreateMasterElectionMechanism()
        {
            Contract.AssertNotNull(Configuration.AzureBlobStorageMasterElectionMechanismConfiguration);
            var masterElectionMechanism = new AzureBlobStorageMasterElectionMechanism(
                Configuration.AzureBlobStorageMasterElectionMechanismConfiguration,
                Configuration.PrimaryMachineLocation,
                Clock);

            if (Dependencies.RoleObserver.TryGetInstance(out var observer)
                || Configuration.ObservableMasterElectionMechanismConfiguration.IsBackgroundEnabled)
            {
                return new ObservableMasterElectionMechanism(
                    Configuration.ObservableMasterElectionMechanismConfiguration,
                    masterElectionMechanism,
                    Clock,
                    observer);
            }
            else
            {
                return masterElectionMechanism;
            }
        }

        private ClusterStateManager CreateClusterStateManager()
        {
            Contract.Assert(Configuration.BlobClusterStateStorageConfiguration is not null);
            return new ClusterStateManager(Configuration, new BlobClusterStateStorage(Configuration.BlobClusterStateStorageConfiguration, Clock), Clock);
        }

        private RedisGlobalStore CreateRedisGlobalStore(Context context)
        {
            Contract.Assert(!Configuration.PreventRedisUsage, "Attempt to use Redis when it is disabled");

            var redisDatabaseFactoryForRedisGlobalStore = RedisDatabaseFactory.Create(
                context,
                new LiteralConnectionStringProvider(Configuration.RedisGlobalStoreConnectionString),
                Configuration.RedisConnectionMultiplexerConfiguration);

            RedisDatabaseFactory? redisDatabaseFactoryForRedisGlobalStoreSecondary = null;
            if (Configuration.RedisGlobalStoreSecondaryConnectionString != null)
            {
                redisDatabaseFactoryForRedisGlobalStoreSecondary = RedisDatabaseFactory.Create(
                    context,
                    new LiteralConnectionStringProvider(Configuration.RedisGlobalStoreSecondaryConnectionString),
                    Configuration.RedisConnectionMultiplexerConfiguration);
            }

            var redisDatabaseForGlobalStore = ConfigurableRedisDatabaseFactory.CreateDatabase(Configuration, redisDatabaseFactoryForRedisGlobalStore, "primaryRedisDatabase");
            var secondaryRedisDatabaseForGlobalStore = ConfigurableRedisDatabaseFactory.CreateDatabase(
                Configuration,
                redisDatabaseFactoryForRedisGlobalStoreSecondary,
                "secondaryRedisDatabase",
                optional: true);

            RedisDatabaseAdapter redisBlobDatabase;
            RedisDatabaseAdapter? secondaryRedisBlobDatabase;
            if (Configuration.UseSeparateConnectionForRedisBlobs)
            {
                // To prevent blob opoerations from blocking other operations, create a separate connections for them.
                redisBlobDatabase = ConfigurableRedisDatabaseFactory.CreateDatabase(Configuration, redisDatabaseFactoryForRedisGlobalStore, "primaryRedisBlobDatabase");
                secondaryRedisBlobDatabase = ConfigurableRedisDatabaseFactory.CreateDatabase(
                    Configuration,
                    redisDatabaseFactoryForRedisGlobalStoreSecondary,
                    "secondaryRedisBlobDatabase",
                    optional: true);
            }
            else
            {
                redisBlobDatabase = redisDatabaseForGlobalStore;
                secondaryRedisBlobDatabase = secondaryRedisDatabaseForGlobalStore;
            }

            var globalStore = new RedisGlobalStore(Arguments.Clock, Configuration, redisDatabaseForGlobalStore, secondaryRedisDatabaseForGlobalStore, redisBlobDatabase, secondaryRedisBlobDatabase, MasterElectionMechanism.Instance);
            return globalStore;
        }
    }
}
