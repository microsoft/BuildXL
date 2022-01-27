// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.Host.Configuration;

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
    }

    /// <summary>
    /// Services used by <see cref="ContentLocationStoreFactory"/>
    /// </summary>
    public class ContentLocationStoreServices : ServicesCreatorBase
    {
        /// <nodoc />
        public ContentLocationStoreServicesDependencies Dependencies => Arguments.Dependencies;

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
        public IServiceDefinition<ICheckpointRegistry> CheckpointRegistry { get; }

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
        }

        private ClientGlobalCacheStore CreateClientGlobalCacheStore()
        {
            return new ClientGlobalCacheStore(MasterClientAccessor.Instance, Configuration.MetadataStore!);
        }

        private LocalLocationStore CreateLocalLocationStore()
        {
            return new LocalLocationStore(
                                Clock,
                                GlobalCacheStore.Instance,
                                Configuration,
                                Copier,
                                MasterElectionMechanism.Instance,
                                ClusterStateManager.Instance,
                                CheckpointRegistry.Instance,
                                Dependencies?.ColdStorage.InstanceOrDefault());
        }

        private ICheckpointRegistry CreateCheckpointRegistry()
        {
            if (Configuration.AzureBlobStorageCheckpointRegistryConfiguration is not null)
            {
                var storageRegistry = new AzureBlobStorageCheckpointRegistry(Configuration.AzureBlobStorageCheckpointRegistryConfiguration, Configuration.PrimaryMachineLocation, Clock);

                if (Configuration.AzureBlobStorageCheckpointRegistryConfiguration.Standalone)
                {
                    return storageRegistry;
                }
                else
                {
                    return new TransitioningCheckpointRegistry(primary: storageRegistry, fallback: RedisGlobalStore.Instance);
                }
            }
            else
            {
                return RedisGlobalStore.Instance;
            }
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
            IMasterElectionMechanism createInner()
            {
                if (Configuration.AzureBlobStorageMasterElectionMechanismConfiguration is not null)
                {
                    var storageElectionMechanism = new AzureBlobStorageMasterElectionMechanism(Configuration.AzureBlobStorageMasterElectionMechanismConfiguration, Configuration.PrimaryMachineLocation, Clock);

                    return storageElectionMechanism;
                }
                else
                {
                    return RedisGlobalStore.Instance;
                }
            }

            var inner = createInner();
            if (Dependencies.RoleObserver.TryGetInstance(out var observer))
            {
                return new ObservableMasterElectionMechanism(inner, observer);
            }
            else
            {
                return inner;
            }
        }

        private ClusterStateManager CreateClusterStateManager()
        {
            IClusterStateStorage storage;
            if (Configuration.BlobClusterStateStorageConfiguration is not null)
            {
                var configuration = Configuration.BlobClusterStateStorageConfiguration;
                var secondaryStorage = new BlobClusterStateStorage(configuration, Clock);
                if (!configuration.Standalone)
                {
                    storage = new TransitionalClusterStateStorage(RedisGlobalStore.Instance, secondaryStorage);
                }
                else
                {
                    storage = secondaryStorage;
                }
            }
            else
            {
                storage = RedisGlobalStore.Instance;
            }

            return new ClusterStateManager(Configuration, storage, Clock);
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

            var globalStore = new RedisGlobalStore(Arguments.Clock, Configuration, redisDatabaseForGlobalStore, secondaryRedisDatabaseForGlobalStore, redisBlobDatabase, secondaryRedisBlobDatabase);
            return globalStore;
        }
    }
}
