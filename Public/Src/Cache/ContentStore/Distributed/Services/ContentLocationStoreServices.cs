// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.ServiceModel;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities;

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
        public RedisDatabaseFactory RedisDatabaseFactoryForRedisGlobalStore { get; }

        /// <nodoc />
        public RedisDatabaseFactory? RedisDatabaseFactoryForRedisGlobalStoreSecondary { get; }

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
        public IServiceDefinition<LocalLocationStore> LocalLocationStore { get; }

        public ContentLocationStoreServices(
            ContentLocationStoreFactoryArguments arguments,
            RedisContentLocationStoreConfiguration configuration)
        {
            Configuration = configuration;
            Arguments = arguments;

            var context = arguments.Copier.Context;

            RedisDatabaseFactoryForRedisGlobalStore = RedisDatabaseFactory.Create(
                context,
                new LiteralConnectionStringProvider(Configuration.RedisGlobalStoreConnectionString),
                Configuration.RedisConnectionMultiplexerConfiguration);

            if (Configuration.RedisGlobalStoreSecondaryConnectionString != null)
            {
                RedisDatabaseFactoryForRedisGlobalStoreSecondary = RedisDatabaseFactory.Create(
                    context,
                    new LiteralConnectionStringProvider(Configuration.RedisGlobalStoreSecondaryConnectionString),
                    Configuration.RedisConnectionMultiplexerConfiguration);
            }

            RedisGlobalStore = Create(() => CreateRedisGlobalStore());

            MasterElectionMechanism = Create(() => CreateMasterElectionMechanism());

            MasterClientAccessor = Create(() => CreateMasterClientAccessor());

            ClientGlobalCacheStore = CreateOptional(() => Configuration.MetadataStore != null, () => CreateClientGlobalCacheStore());

            GlobalCacheStore = Create(() => CreateGlobalCacheStore());

            LocalLocationStore = Create(() => CreateLocalLocationStore());
        }

        private ClientGlobalCacheStore CreateClientGlobalCacheStore()
        {
            return new ClientGlobalCacheStore(MasterClientAccessor.Instance, Configuration.MetadataStore!);
        }

        private LocalLocationStore CreateLocalLocationStore()
        {
            return new LocalLocationStore(
                                Clock,
                                RedisGlobalStore.Instance,
                                GlobalCacheStore.Instance,
                                Configuration,
                                Copier,
                                MasterElectionMechanism.Instance,
                                Dependencies?.ColdStorage.InstanceOrDefault());
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

            return new GrpcMasterClientFactory<IGlobalCacheService>(RedisGlobalStore.Instance, clientPool, MasterElectionMechanism.Instance);
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

        private RedisGlobalStore CreateRedisGlobalStore()
        {
            var redisDatabaseForGlobalStore = ConfigurableRedisDatabaseFactory.CreateDatabase(Configuration, RedisDatabaseFactoryForRedisGlobalStore, "primaryRedisDatabase");
            var secondaryRedisDatabaseForGlobalStore = ConfigurableRedisDatabaseFactory.CreateDatabase(
                Configuration,
                RedisDatabaseFactoryForRedisGlobalStoreSecondary,
                "secondaryRedisDatabase",
                optional: true);

            RedisDatabaseAdapter redisBlobDatabase;
            RedisDatabaseAdapter? secondaryRedisBlobDatabase;
            if (Configuration.UseSeparateConnectionForRedisBlobs)
            {
                // To prevent blob opoerations from blocking other operations, create a separate connections for them.
                redisBlobDatabase = ConfigurableRedisDatabaseFactory.CreateDatabase(Configuration, RedisDatabaseFactoryForRedisGlobalStore, "primaryRedisBlobDatabase");
                secondaryRedisBlobDatabase = ConfigurableRedisDatabaseFactory.CreateDatabase(
                    Configuration,
                    RedisDatabaseFactoryForRedisGlobalStoreSecondary,
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
