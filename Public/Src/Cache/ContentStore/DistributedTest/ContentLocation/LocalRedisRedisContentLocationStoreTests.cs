// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation
{
    [Collection("Redis-based tests")]
    [Trait("Category", "LongRunningTest")]
    public class LocalRedisRedisContentLocationStoreTests : RedisContentLocationStoreTests
    {
        protected LocalRedisFixture Redis { get; }

        /// <inheritdoc />
        public LocalRedisRedisContentLocationStoreTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(output)
        {
            Redis = redis;
        }

        protected override ITestRedisDatabase GetRedisDatabase(
            MemoryClock clock,
            IDictionary<RedisKey, RedisValue> initialData = null,
            IDictionary<RedisKey, DateTime> expiryData = null,
            IDictionary<RedisKey, RedisValue[]> setData = null)
        {
            return LocalRedisProcessDatabase.CreateAndStart(Redis, TestGlobal.Logger, clock, initialData, expiryData, setData);
        }
    }
}
