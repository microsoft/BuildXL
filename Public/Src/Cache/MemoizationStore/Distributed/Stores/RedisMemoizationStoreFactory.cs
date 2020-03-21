// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <nodoc />
    public class RedisMemoizationStoreFactory : RedisContentLocationStoreFactory
    {
        private readonly TimeSpan _memoizationExpiryTime;

        /// <nodoc />
        public RedisMemoizationStoreFactory(
            IConnectionStringProvider contentConnectionStringProvider,
            IConnectionStringProvider machineLocationConnectionStringProvider,
            IClock clock,
            TimeSpan contentHashBumpTime,
            RedisMemoizationStoreConfiguration configuration,
            IDistributedContentCopier copier)
            : base(contentConnectionStringProvider, machineLocationConnectionStringProvider, clock, contentHashBumpTime, configuration, copier)
        {
            _memoizationExpiryTime = configuration.MemoizationExpiryTime;
        }

        /// <nodoc />
        public IMemoizationStore CreateMemoizationStore(ILogger logger)
        {
            var redisDatabaseFactory = Configuration.HasReadMode(ContentLocationMode.LocalLocationStore)
                ? RedisDatabaseFactoryForRedisGlobalStore
                : RedisDatabaseFactoryForContent;

            Contract.Assert(redisDatabaseFactory != null);

            var redisDatabaseAdapter = CreateDatabase(redisDatabaseFactory);

            var memoizationDb = new RedisMemoizationDatabase(redisDatabaseAdapter, Clock, _memoizationExpiryTime);
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
