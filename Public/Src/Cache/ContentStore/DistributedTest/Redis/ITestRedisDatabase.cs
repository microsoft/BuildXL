// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace ContentStoreTest.Distributed.Redis
{
    public interface ITestRedisDatabase : IDisposable
    {
        bool BatchCalled { get; set; }

        void Execute();

        Task<bool> KeyExistsAsync(RedisKey key, CommandFlags command = CommandFlags.None);

        Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags command = CommandFlags.None);

        Task<bool> StringSetAsync(RedisKey key, RedisValue value, When condition);

        Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiryTimespan, When condition);

        Task<long> StringIncrementAsync(RedisKey key);

        Task<bool> StringSetBitAsync(RedisKey key, long offset, bool bit);

        Task<HashEntry[]> HashGetAllAsync(RedisKey key, CommandFlags command = CommandFlags.None);

        Task<bool> HashSetAsync(
            RedisKey key,
            RedisValue hashField,
            RedisValue value,
            When when = When.Always,
            CommandFlags flags = CommandFlags.None);

        Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags);

        Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue range);

        Task<bool> KeyExpireAsync(RedisKey key, DateTime expiryDateTime);

        Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key);

        Task<bool> KeyDeleteAsync(RedisKey key);

        Task<bool> SetAddAsync(RedisKey key, RedisValue value);

        Task<long> SetAddAsync(RedisKey key, RedisValue[] values);

        Task<bool> SetRemoveAsync(RedisKey key, RedisValue value);

        Task<RedisValue[]> SetMembersAsync(RedisKey key);

        Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count);

        IDictionary<RedisKey, MockRedisValueWithExpiry> GetDbWithExpiry();

        Task<RedisResult> ExecuteScriptAsync(string script, RedisKey[] keys, RedisValue[] values);

        Task<long> SetLengthAsync(RedisKey key);
    }
}
