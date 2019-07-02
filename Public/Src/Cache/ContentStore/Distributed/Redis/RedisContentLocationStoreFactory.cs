// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Instantiates a new ContentLocationStore backed by a Redis instance.
    /// </summary>
    public sealed class RedisContentLocationStoreFactory : IContentLocationStoreFactory
    {
        /// <summary>
        /// Default value for keyspace used for partitioning Redis data
        /// </summary>
        public const string DefaultKeySpace = "Default:";

        /// <summary>
        /// Salt to determine keyspace's current version
        /// </summary>
        public const string Salt = "V4";

        private readonly ContentSessionTracer _tracer = new ContentSessionTracer(nameof(RedisContentLocationStoreFactory));

        private readonly IConnectionStringProvider /*CanBeNull*/ _contentConnectionStringProvider;
        private readonly IConnectionStringProvider /*CanBeNull*/ _machineConnectionStringProvider;

        // https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Basics.md
        // Maintain the same connection multiplexer to reuse across sessions
        private RedisDatabaseFactory /*CanBeNull*/ _redisDatabaseFactoryForContent;
        private RedisDatabaseFactory /*CanBeNull*/ _redisDatabaseFactoryForMachineLocations;

        private readonly IClock _clock;
        private readonly TimeSpan _contentHashBumpTime;
        private readonly string _keySpace;
        private readonly byte[] _localMachineLocation;
        private readonly RedisContentLocationStoreConfiguration _configuration;

        private RedisDatabaseFactory _redisDatabaseFactoryForRedisGlobalStore;
        private RedisDatabaseFactory _redisDatabaseFactoryForRedisGlobalStoreSecondary;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisContentLocationStoreFactory"/> class.
        /// </summary>
        public RedisContentLocationStoreFactory(
            /*CanBeNull*/IConnectionStringProvider contentConnectionStringProvider,
            /*CanBeNull*/IConnectionStringProvider machineLocationConnectionStringProvider,
            IClock clock,
            TimeSpan contentHashBumpTime,
            string keySpace,
            byte[] localMachineLocation,
            IAbsFileSystem fileSystem = null,
            RedisContentLocationStoreConfiguration configuration = null)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(keySpace));

            _contentConnectionStringProvider = contentConnectionStringProvider;
            _machineConnectionStringProvider = machineLocationConnectionStringProvider;
            _clock = clock;
            _contentHashBumpTime = contentHashBumpTime;
            _keySpace = keySpace + Salt;
            _localMachineLocation = localMachineLocation;
            _configuration = configuration ?? RedisContentLocationStoreConfiguration.Default;

            if (_configuration.HasReadOrWriteMode(ContentLocationMode.Redis))
            {
                Contract.Assert(contentConnectionStringProvider != null, "When ReadFromRedis is on 'contentConnectionStringProvider' must not be null.");
                Contract.Assert(machineLocationConnectionStringProvider != null, "When ReadFromRedis is on 'machineLocationConnectionStringProvider' must not be null.");
            }
        }

        /// <inheritdoc />
        public Task<IContentLocationStore> CreateAsync()
        {
            IContentLocationStore contentLocationStore = null;

            if (_configuration.HasReadOrWriteMode(ContentLocationMode.Redis))
            {
                var redisDatabaseAdapter = CreateDatabase(_redisDatabaseFactoryForContent);
                var machineLocationRedisDatabaseAdapter = CreateDatabase(_redisDatabaseFactoryForMachineLocations);

                contentLocationStore = new RedisContentLocationStore(
                    redisDatabaseAdapter,
                    machineLocationRedisDatabaseAdapter,
                    _clock,
                    _contentHashBumpTime,
                    _localMachineLocation,
                    _configuration);
            }

            if (_configuration.HasReadOrWriteMode(ContentLocationMode.LocalLocationStore))
            {
                Contract.Assert(_redisDatabaseFactoryForRedisGlobalStore != null);
                var redisDatabaseForGlobalStore = CreateDatabase(_redisDatabaseFactoryForRedisGlobalStore);
                var secondaryRedisDatabaseForGlobalStore = CreateDatabase(_redisDatabaseFactoryForRedisGlobalStoreSecondary, optional: true);
                IGlobalLocationStore globalStore = new RedisGlobalStore(_clock, _configuration, new MachineLocation(_localMachineLocation), redisDatabaseForGlobalStore, secondaryRedisDatabaseForGlobalStore);
                var localLocationStore = new LocalLocationStore(_clock, globalStore, _configuration);

                contentLocationStore = new TransitioningContentLocationStore(_configuration, (RedisContentLocationStore)contentLocationStore, localLocationStore);
            }

            return Task.FromResult(contentLocationStore);
        }

        private RedisDatabaseAdapter CreateDatabase(RedisDatabaseFactory factory, bool optional = false)
        {
            if (factory != null)
            {
                return new RedisDatabaseAdapter(factory, _keySpace);
            }
            else
            {
                Contract.Assert(optional);
                return null;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            return StartupCall<ContentSessionTracer>.RunAsync(
                  _tracer,
                  context,
                  async () =>
                  {
                      _tracer.TraceStartupConfiguration(context, _configuration);

                      if (_configuration.RedisGlobalStoreConnectionString != null)
                      {
                          _redisDatabaseFactoryForRedisGlobalStore = await RedisDatabaseFactory.CreateAsync(
                              context,
                              new LiteralConnectionStringProvider(_configuration.RedisGlobalStoreConnectionString));

                          if (_configuration.RedisGlobalStoreSecondaryConnectionString != null)
                          {
                              _redisDatabaseFactoryForRedisGlobalStoreSecondary = await RedisDatabaseFactory.CreateAsync(
                                  context,
                                  new LiteralConnectionStringProvider(_configuration.RedisGlobalStoreSecondaryConnectionString));
                          }
                      }
                      else
                      {
                          // Local location store can only be used if connection string is provided for redis global store
                          Contract.Assert(!_configuration.HasReadOrWriteMode(ContentLocationMode.LocalLocationStore));
                      }

                      // Instantiate factories for old redis only when we use old redis.
                      if (_configuration.HasReadOrWriteMode(ContentLocationMode.Redis))
                      {
                          Contract.Assert(_contentConnectionStringProvider != null);
                          Contract.Assert(_machineConnectionStringProvider != null);
                          _redisDatabaseFactoryForContent = await RedisDatabaseFactory.CreateAsync(context, _contentConnectionStringProvider);
                          _redisDatabaseFactoryForMachineLocations = await RedisDatabaseFactory.CreateAsync(context, _machineConnectionStringProvider);
                      }

                      StartupCompleted = true;
                      return BoolResult.Success;
                  });
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            return ShutdownCall<ContentSessionTracer>.RunAsync(_tracer, context, () =>
            {
                ShutdownCompleted = true;
                return Task.FromResult(BoolResult.Success);
            });
        }
    }
}
