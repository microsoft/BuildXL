// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Creates <see cref="IContentLocationStore"/> instance backed by Local Location Store.
    /// </summary>
    public class ContentLocationStoreFactory : StartupShutdownBase, IContentLocationStoreFactory
    {
        /// <summary>
        /// Default value for keyspace used for partitioning Redis data
        /// </summary>
        public const string DefaultKeySpace = "Default:";

        /// <inheritdoc />
        public override bool AllowMultipleStartupAndShutdowns => true;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new ContentSessionTracer(nameof(ContentLocationStoreFactory));

        // https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Basics.md
        // Maintain the same connection multiplexer to reuse across sessions

        /// <nodoc />
        protected readonly IClock Clock;

        /// <nodoc />
        protected readonly IDistributedContentCopier Copier;

        /// <nodoc />
        protected string KeySpace => Configuration.Keyspace;

        /// <nodoc />
        protected readonly RedisContentLocationStoreConfiguration Configuration;

        /// <nodoc />
        protected RedisDatabaseFactory? RedisDatabaseFactoryForRedisGlobalStore;

        /// <nodoc />
        protected RedisDatabaseFactory? RedisDatabaseFactoryForRedisGlobalStoreSecondary;

        private readonly Lazy<LocalLocationStore> _lazyLocalLocationStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentLocationStoreFactory"/> class.
        /// </summary>
        public ContentLocationStoreFactory(
            IClock clock,
            RedisContentLocationStoreConfiguration configuration,
            IDistributedContentCopier copier)
        {
            Contract.Requires(configuration != null);
            Contract.Requires(!string.IsNullOrEmpty(configuration.RedisGlobalStoreConnectionString));
            Contract.Requires(!string.IsNullOrWhiteSpace(configuration.Keyspace));
            Contract.Requires(copier != null);

            Clock = clock;
            Copier = copier;
            _lazyLocalLocationStore = new Lazy<LocalLocationStore>(() => CreateLocalLocationStore());
            Configuration = configuration;
        }

        /// <inheritdoc />
        public Task<IContentLocationStore> CreateAsync(MachineLocation localMachineLocation, ILocalContentStore? localContentStore)
        {
            IContentLocationStore contentLocationStore = new TransitioningContentLocationStore(
                Configuration,
                _lazyLocalLocationStore.Value,
                localMachineLocation,
                localContentStore);

            return Task.FromResult(contentLocationStore);
        }

        /// <summary>
        /// Creates an instance of <see cref="LocalLocationStore"/>.
        /// </summary>
        protected virtual LocalLocationStore CreateLocalLocationStore()
        {
            Contract.Assert(RedisDatabaseFactoryForRedisGlobalStore != null);

            var globalStore = CreateRedisGlobalStore();
            var localLocationStore = new LocalLocationStore(Clock, globalStore, Configuration, Copier);
            return localLocationStore;
        }

        /// <summary>
        /// Creates an instance of <see cref="IGlobalLocationStore"/>.
        /// </summary>
        protected virtual IGlobalLocationStore CreateRedisGlobalStore()
        {
            var redisDatabaseForGlobalStore = CreateDatabase(RedisDatabaseFactoryForRedisGlobalStore, "primaryRedisDatabase");
            var secondaryRedisDatabaseForGlobalStore = CreateDatabase(
                RedisDatabaseFactoryForRedisGlobalStoreSecondary,
                "secondaryRedisDatabase",
                optional: true);
            IGlobalLocationStore globalStore = new RedisGlobalStore(Clock, Configuration, redisDatabaseForGlobalStore, secondaryRedisDatabaseForGlobalStore);
            return globalStore;
        }

        private RedisDatabaseAdapter? CreateDatabase(RedisDatabaseFactory? factory, string databaseName, bool optional = false)
        {
            if (factory != null)
            {
                var adapterConfiguration = new RedisDatabaseAdapterConfiguration(
                    KeySpace,
                    Configuration.RedisConnectionErrorLimit,
                    Configuration.RedisReconnectionLimitBeforeServiceRestart,
                    traceOperationFailures: Configuration.TraceRedisFailures,
                    traceTransientFailures: Configuration.TraceRedisTransientFailures,
                    databaseName: databaseName,
                    minReconnectInterval: Configuration.MinRedisReconnectInterval,
                    cancelBatchWhenMultiplexerIsClosed: Configuration.CancelBatchWhenMultiplexerIsClosed,
                    treatObjectDisposedExceptionAsTransient: Configuration.TreatObjectDisposedExceptionAsTransient);

                return new RedisDatabaseAdapter(factory, adapterConfiguration);
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

            RedisDatabaseFactoryForRedisGlobalStore = await RedisDatabaseFactory.CreateAsync(
                context,
                new LiteralConnectionStringProvider(Configuration.RedisGlobalStoreConnectionString),
                logSeverity: Configuration.RedisInternalLogSeverity ?? Severity.Unknown,
                usePreventThreadTheft: Configuration.UsePreventThreadTheftFeature);

            if (Configuration.RedisGlobalStoreSecondaryConnectionString != null)
            {
                RedisDatabaseFactoryForRedisGlobalStoreSecondary = await RedisDatabaseFactory.CreateAsync(
                    context,
                    new LiteralConnectionStringProvider(Configuration.RedisGlobalStoreSecondaryConnectionString),
                    logSeverity: Configuration.RedisInternalLogSeverity ?? Severity.Unknown,
                    usePreventThreadTheft: Configuration.UsePreventThreadTheftFeature);
            }

            return BoolResult.Success;
        }
    }
}
