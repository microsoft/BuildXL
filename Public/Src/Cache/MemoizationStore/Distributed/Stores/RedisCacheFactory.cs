using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    public class RedisCacheFactory : RedisContentLocationStoreFactory
    {
        public RedisCacheFactory(
            IConnectionStringProvider contentConnectionStringProvider,
            IConnectionStringProvider machineLocationConnectionStringProvider,
            IClock clock,
            TimeSpan contentHashBumpTime,
            string keySpace,
            IAbsFileSystem fileSystem = null,
            RedisContentLocationStoreConfiguration configuration = null)
            : base(contentConnectionStringProvider, machineLocationConnectionStringProvider, clock, contentHashBumpTime, keySpace, fileSystem, configuration)
        {
        }

        public IMemoizationStore CreateMemoizationStore(ILogger logger)
        {
            var redisDatabaseFactory = Configuration.HasReadMode(ContentLocationMode.LocalLocationStore)
                ? _redisDatabaseFactoryForRedisGlobalStore
                : _redisDatabaseFactoryForContent;

            Contract.Assert(redisDatabaseFactory != null);

            var redisDatabaseAdapter = CreateDatabase(redisDatabaseFactory);

            var memoizationDb = new RedisMemoizationDatabase(redisDatabaseAdapter, _clock);
            return new RedisMemoizationStore(logger, memoizationDb);
        }

        private RedisDatabaseAdapter CreateDatabase(RedisDatabaseFactory factory, bool optional = false)
        {
            if (factory != null)
            {
                return new RedisDatabaseAdapter(factory, KeySpace);
            }
            else
            {
                Contract.Assert(optional);
                return null;
            }
        }
    }
}
