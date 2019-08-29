﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    public class RedisMemoizationStore : DatabaseMemoizationStore
    {
        /// <nodoc />
        public RedisMemoizationStore(ILogger logger, IClock clock, RedisDatabaseAdapter redis)
            : base(logger, new RedisMemoizationDatabase(redis, clock))
        {
        }

        /// <nodoc />
        public RedisMemoizationStore(ILogger logger, RedisMemoizationDatabase database)
            : base(logger, database)
        {
        }

        public static RedisMemoizationStore Create(
            ILogger logger,
            IConnectionStringProvider connectionStringProvider,
            string keyspace,
            IClock clock)
        {
            var context = new Context(logger);
            var redisFactory = RedisDatabaseFactory.CreateAsync(context, connectionStringProvider).GetAwaiter().GetResult();
            var redisAdapter = new RedisDatabaseAdapter(redisFactory, keyspace);
            return new RedisMemoizationStore(logger, clock, redisAdapter);
        }
    }
}
