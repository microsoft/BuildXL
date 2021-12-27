// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Services;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <nodoc />
    public class RedisMemoizationStoreFactory
    {
    /// <nodoc />
        public static IMemoizationStore CreateMemoizationStore(ContentLocationStoreServices services)
        {
            var redisGlobalStore = services.RedisGlobalStore.Instance;
            var memoizationDb = new RedisMemoizationDatabase(
                redisGlobalStore.RaidedRedis.PrimaryRedisDb,
                redisGlobalStore.RaidedRedis.SecondaryRedisDb,
                services.Configuration.Memoization);
            return new RedisMemoizationStore(memoizationDb);
        }
    }
}
