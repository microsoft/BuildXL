// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
            string keySpace,
            IAbsFileSystem fileSystem = null,
            RedisMemoizationStoreConfiguration configuration = null)
            : base(contentConnectionStringProvider, machineLocationConnectionStringProvider, clock, contentHashBumpTime, keySpace, fileSystem, configuration)
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
