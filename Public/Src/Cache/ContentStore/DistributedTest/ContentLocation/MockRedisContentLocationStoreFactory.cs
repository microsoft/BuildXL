// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStoreTest.Distributed.Redis;

namespace ContentStoreTest.Distributed.ContentLocation
{
    public sealed class MockRedisContentLocationStoreFactory : IContentLocationStoreFactory
    {
        private readonly RedisContentLocationStoreConfiguration _configuration;
        private readonly IClock _mockClock;
        private readonly MachineLocation _localMachineLocation;

        public MockRedisContentLocationStoreFactory(
            ITestRedisDatabase redisDatabase,
            ITestRedisDatabase machineLocationRedisDatabase,
            AbsolutePath localCacheRoot,
            IClock mockClock = null,
            RedisContentLocationStoreConfiguration configuration = null)
        {
            _mockClock = mockClock ?? SystemClock.Instance;
            RedisDatabase = redisDatabase;
            MachineLocationRedisDatabase = machineLocationRedisDatabase;
            _localMachineLocation = new MachineLocation(localCacheRoot.Path);
            _configuration = configuration ?? RedisContentLocationStoreConfiguration.Default;
        }

        internal TimeSpan BumpTime { get; } = TimeSpan.FromHours(1);

        internal ITestRedisDatabase RedisDatabase { get; }

        internal ITestRedisDatabase MachineLocationRedisDatabase { get; }

        internal IPathTransformer<AbsolutePath> PathTransformer { get; } = new TestPathTransformer();

        internal RedisDatabaseAdapter RedisDatabaseAdapter { get; set; }

        private RedisDatabaseAdapter MachineRedisDatabaseAdapter { get; set; }

        public bool StartupCompleted { get; private set; }

        public bool StartupStarted { get; private set; }

        public bool ShutdownCompleted { get; private set; }

        public bool ShutdownStarted { get; private set; }

        public Task<IContentLocationStore> CreateAsync()
        {
            return CreateAsync(_localMachineLocation);
        }

        public async Task<IContentLocationStore> CreateAsync(MachineLocation machineLocation)
        {
            var connection = MockRedisDatabaseFactory.CreateConnection(RedisDatabase);
            RedisDatabaseAdapter = RedisDatabaseAdapter ?? new RedisDatabaseAdapter(await RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), connection), RedisContentLocationStoreFactory.DefaultKeySpace);
            var machineLocationConnection = MockRedisDatabaseFactory.CreateConnection(MachineLocationRedisDatabase);
            MachineRedisDatabaseAdapter = MachineRedisDatabaseAdapter ?? new RedisDatabaseAdapter(await RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), machineLocationConnection), RedisContentLocationStoreFactory.DefaultKeySpace);
            IContentLocationStore store = new RedisContentLocationStore(
                RedisDatabaseAdapter,
                MachineRedisDatabaseAdapter,
                _mockClock,
                BumpTime,
                machineLocation.Data,
                _configuration);

            var redisStore = (RedisContentLocationStore) store;
            redisStore.DisableReplica = true;
            return store;
        }

        public void Dispose()
        {
            RedisDatabase.Dispose();
            MachineLocationRedisDatabase.Dispose();
        }

        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = StartupCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = ShutdownCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }
    }
}
