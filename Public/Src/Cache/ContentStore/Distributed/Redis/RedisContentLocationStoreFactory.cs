// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Instantiates a new ContentLocationStore backed by a Redis instance.
    /// </summary>
    public class RedisContentLocationStoreFactory : StartupShutdownBase, IContentLocationStoreFactory
    {
        /// <summary>
        /// Default value for keyspace used for partitioning Redis data
        /// </summary>
        public const string DefaultKeySpace = "Default:";

        /// <inheritdoc />
        public override bool AllowMultipleStartupAndShutdowns => true;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new ContentSessionTracer(nameof(RedisContentLocationStoreFactory));

        /// <summary>
        /// Salt to determine keyspace's current version
        /// </summary>
        public const string Salt = "V4";

        private readonly IConnectionStringProvider /*CanBeNull*/ _contentConnectionStringProvider;
        private readonly IConnectionStringProvider /*CanBeNull*/ _machineConnectionStringProvider;

        // https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Basics.md
        // Maintain the same connection multiplexer to reuse across sessions

        /// <nodoc />
        protected RedisDatabaseFactory /*CanBeNull*/ RedisDatabaseFactoryForContent;

        /// <nodoc />
        protected RedisDatabaseFactory /*CanBeNull*/ RedisDatabaseFactoryForMachineLocations;

        /// <nodoc />
        protected readonly IClock Clock;

        private readonly TimeSpan _contentHashBumpTime;
        private readonly IDistributedContentCopier _copier;

        /// <nodoc />
        protected readonly string KeySpace;

        /// <nodoc />
        protected readonly RedisContentLocationStoreConfiguration Configuration;

        /// <nodoc />
        protected RedisDatabaseFactory RedisDatabaseFactoryForRedisGlobalStore;

        /// <nodoc />
        protected RedisDatabaseFactory RedisDatabaseFactoryForRedisGlobalStoreSecondary;

        private readonly Lazy<LocalLocationStore> _lazyLocalLocationStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisContentLocationStoreFactory"/> class.
        /// </summary>
        public RedisContentLocationStoreFactory(
            /*CanBeNull*/IConnectionStringProvider contentConnectionStringProvider,
            /*CanBeNull*/IConnectionStringProvider machineLocationConnectionStringProvider,
            IClock clock,
            TimeSpan contentHashBumpTime,
            string keySpace,
            RedisContentLocationStoreConfiguration configuration,
            IDistributedContentCopier copier)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(keySpace));

            _contentConnectionStringProvider = contentConnectionStringProvider;
            _machineConnectionStringProvider = machineLocationConnectionStringProvider;
            Clock = clock;
            _contentHashBumpTime = contentHashBumpTime;
            _copier = copier;
            _lazyLocalLocationStore = new Lazy<LocalLocationStore>(() => CreateLocalLocationStore());
            KeySpace = keySpace + Salt;
            Configuration = configuration ?? RedisContentLocationStoreConfiguration.Default;

            if (Configuration.HasReadOrWriteMode(ContentLocationMode.Redis))
            {
                Contract.Assert(contentConnectionStringProvider != null, "When ReadFromRedis is on 'contentConnectionStringProvider' must not be null.");
                Contract.Assert(machineLocationConnectionStringProvider != null, "When ReadFromRedis is on 'machineLocationConnectionStringProvider' must not be null.");
            }
        }

        /// <inheritdoc />
        public Task<IContentLocationStore> CreateAsync(MachineLocation localMachineLocation, ILocalContentStore localContentStore)
        {
            IContentLocationStore contentLocationStore = null;

            if (Configuration.HasReadOrWriteMode(ContentLocationMode.Redis))
            {
                var redisDatabaseAdapter = CreateDatabase(RedisDatabaseFactoryForContent);
                var machineLocationRedisDatabaseAdapter = CreateDatabase(RedisDatabaseFactoryForMachineLocations);

                contentLocationStore = new RedisContentLocationStore(
                    redisDatabaseAdapter,
                    machineLocationRedisDatabaseAdapter,
                    Clock,
                    _contentHashBumpTime,
                    localMachineLocation.Data,
                    Configuration);
            }

            if (Configuration.HasReadOrWriteMode(ContentLocationMode.LocalLocationStore))
            {
                var localLocationStore = _lazyLocalLocationStore.Value;
                contentLocationStore = new TransitioningContentLocationStore(Configuration, (RedisContentLocationStore)contentLocationStore, localLocationStore, localMachineLocation, localContentStore);
            }

            return Task.FromResult(contentLocationStore);
        }

        private LocalLocationStore CreateLocalLocationStore()
        {
            Contract.Assert(RedisDatabaseFactoryForRedisGlobalStore != null);
            var redisDatabaseForGlobalStore = CreateDatabase(RedisDatabaseFactoryForRedisGlobalStore);
            var secondaryRedisDatabaseForGlobalStore = CreateDatabase(RedisDatabaseFactoryForRedisGlobalStoreSecondary, optional: true);
            IGlobalLocationStore globalStore = new RedisGlobalStore(Clock, Configuration, redisDatabaseForGlobalStore, secondaryRedisDatabaseForGlobalStore);
            var localLocationStore = new LocalLocationStore(Clock, globalStore, Configuration, _copier);
            return localLocationStore;
        }

        private RedisDatabaseAdapter CreateDatabase(RedisDatabaseFactory factory, bool optional = false)
        {
            if (factory != null)
            {
                return new RedisDatabaseAdapter(factory, KeySpace, redisConnectionErrorLimit: Configuration.RedisConnectionErrorLimit);
            }
            else
            {
                Contract.Assert(optional);
                return null;
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            Tracer.TraceStartupConfiguration(context, Configuration);

            if (Configuration.RedisGlobalStoreConnectionString != null)
            {
                RedisDatabaseFactoryForRedisGlobalStore = await RedisDatabaseFactory.CreateAsync(
                    context,
                    new LiteralConnectionStringProvider(Configuration.RedisGlobalStoreConnectionString));

                if (Configuration.RedisGlobalStoreSecondaryConnectionString != null)
                {
                    RedisDatabaseFactoryForRedisGlobalStoreSecondary = await RedisDatabaseFactory.CreateAsync(
                        context,
                        new LiteralConnectionStringProvider(Configuration.RedisGlobalStoreSecondaryConnectionString));
                }
            }
            else
            {
                // Local location store can only be used if connection string is provided for redis global store
                Contract.Assert(!Configuration.HasReadOrWriteMode(ContentLocationMode.LocalLocationStore));
            }

            // Instantiate factories for old redis only when we use old redis.
            if (Configuration.HasReadOrWriteMode(ContentLocationMode.Redis))
            {
                Contract.Assert(_contentConnectionStringProvider != null);
                Contract.Assert(_machineConnectionStringProvider != null);
                RedisDatabaseFactoryForContent = await RedisDatabaseFactory.CreateAsync(context, _contentConnectionStringProvider);
                RedisDatabaseFactoryForMachineLocations = await RedisDatabaseFactory.CreateAsync(context, _machineConnectionStringProvider);
            }

            return BoolResult.Success;
        }
    }
}
