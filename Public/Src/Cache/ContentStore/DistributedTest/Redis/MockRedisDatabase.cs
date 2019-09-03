// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using StackExchange.Redis;

namespace ContentStoreTest.Distributed.Redis
{
    /// <summary>
    /// Represents a mock for a union of <see cref="IDatabase"/> and <see cref="IBatch"/>
    /// </summary>
    public class MockRedisDatabase : ITestRedisDatabase
    {
        private readonly IClock _clock;
        private readonly ConcurrentDictionary<RedisKey, RedisValue> _dbMaster = new ConcurrentDictionary<RedisKey, RedisValue>();
        private readonly ConcurrentDictionary<RedisKey, RedisValue> _dbReplica = new ConcurrentDictionary<RedisKey, RedisValue>();

        public readonly ConcurrentDictionary<RedisKey, RedisValue[]> DbSet = new ConcurrentDictionary<RedisKey, RedisValue[]>();

        private readonly ConcurrentDictionary<RedisKey, DateTime> _dbExpiry = new ConcurrentDictionary<RedisKey, DateTime>();

        private readonly ConcurrentDictionary<RedisKey, ConcurrentDictionary<RedisValue, RedisValue>> _dbHash = new ConcurrentDictionary<RedisKey, ConcurrentDictionary<RedisValue, RedisValue>>();

        public IDictionary<RedisKey, MockRedisValueWithExpiry> GetDbWithExpiry()
        {
            return _dbMaster.ToDictionary(
                kvp => kvp.Key,
                kvp => new MockRedisValueWithExpiry(kvp.Value, _dbExpiry.ContainsKey(kvp.Key) ? _dbExpiry[kvp.Key] : (DateTime?)null)
            );
        }

        public bool BatchCalled { get; set; }

        public MockRedisDatabase(IClock clock, IDictionary<RedisKey, RedisValue> initialData = null, IDictionary<RedisKey, DateTime> expiryData = null, IDictionary<RedisKey, RedisValue[]> setData = null)
        {
            _clock = clock;
            if (initialData != null)
            {
                foreach (var pair in initialData)
                {
                    _dbMaster.GetOrAdd(pair.Key, pair.Value);
                }
            }

            if (expiryData != null)
            {
                foreach (var pair in expiryData)
                {
                    _dbExpiry.GetOrAdd(pair.Key, pair.Value);
                }
            }

            if (setData != null)
            {
                foreach (var pair in setData)
                {
                    DbSet.GetOrAdd(pair.Key, pair.Value);
                }
            }
        }

        public virtual Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags command = CommandFlags.None)
        {
            RedisValue value;

            if (command == CommandFlags.PreferSlave)
            {
                _dbReplica.TryGetValue(key, out value);
            }
            else
            {
                _dbMaster.TryGetValue(key, out value);
            }

            return Task.FromResult(value);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords", MessageId = "When")]
        public virtual Task<bool> StringSetAsync(RedisKey key, RedisValue value, When condition)
        {
            if (value.IsNull)
            {
                throw new InvalidOperationException("Calling SET with no value throws RedisServerException: ERR wrong number of arguments for 'set' command");
            }

            switch (condition)
            {
                case When.NotExists:
                    bool success = _dbMaster.TryAdd(key, value);
                    return Task.FromResult(success);
                case When.Always:
                    _dbMaster.AddOrUpdate(key, value, (_, existValue) => value);
                    return Task.FromResult(true);
                default:
                    throw new NotImplementedException();
            }
        }

        public Task<bool> KeyExistsAsync(RedisKey key, CommandFlags command)
        {
            return Task.FromResult(_dbMaster.ContainsKey(key));
        }

        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiryTimespan, When condition)
        {
            if (value.IsNull)
            {
                throw new InvalidOperationException("Calling SET with no value throws RedisServerException: ERR wrong number of arguments for 'set' command");
            }

            bool success;
            switch (condition)
            {
                case When.NotExists:
                    success = _dbMaster.TryAdd(key, value);
                    break;
                case When.Always:
                    _dbMaster.AddOrUpdate(key, value, (_, existValue) => value);
                    success = true;
                    break;
                default:
                    throw new NotImplementedException();
            }

            success = success && _dbExpiry.TryAdd(key, _clock.UtcNow.Add(expiryTimespan.GetValueOrDefault()));
            return Task.FromResult(success);
        }

        public Task<long> StringIncrementAsync(RedisKey key)
        {
            RedisValue result = _dbMaster.AddOrUpdate(key, 1, (_, value) => (long)value + 1);
            return Task.FromResult((long)result);
        }

        public Task<bool> StringSetBitAsync(RedisKey key, long offset, bool bit)
        {
            _dbMaster.AddOrUpdate(key, SetBit(new byte[1], offset, bit), (_, value) => SetBit(value, offset, bit));
            return Task.FromResult(true);
        }

        public Task<RedisValue> StringSetRangeAsync(RedisKey key, long offset, RedisValue range)
        {
            RedisValue result = _dbMaster.AddOrUpdate(key, range, (_, value) => SetRange(value, offset, range));
            return Task.FromResult(result);
        }

        public Task<bool> KeyExpireAsync(RedisKey key, DateTime expiryDateTime)
        {
            _dbExpiry.AddOrUpdate(key, expiryDateTime, (x, y) => expiryDateTime);
            return Task.FromResult(true);
        }

        public Task<TimeSpan?> KeyTimeToLiveAsync(RedisKey key)
        {
            DateTime value;
            _dbExpiry.TryGetValue(key, out value);
            TimeSpan? expiry = value == default(DateTime) ? (TimeSpan?)null : value.Subtract(_clock.UtcNow);
            return Task.FromResult(expiry);
        }

        public Task<bool> KeyDeleteAsync(RedisKey key)
        {
            DateTime expiry;
            _dbExpiry.TryRemove(key, out expiry);

            RedisValue value;
            bool success = _dbMaster.TryRemove(key, out value);

            RedisValue[] values;
            success = success || DbSet.TryRemove(key, out values);

            return Task.FromResult(success);
        }

        public Task<bool> SetAddAsync(RedisKey key, RedisValue value)
        {
            var update = DbSet.AddOrUpdate(key, new[] { value }, (_, existValue) => existValue.ToList().Union(new[] { value }).ToArray());
            return Task.FromResult(true);
        }

        public Task<long> SetAddAsync(RedisKey key, RedisValue[] values)
        {
            if (values.Length == 0)
            {
                throw new InvalidOperationException("Calling SADD with no values throws RedisServerException: ERR wrong number of arguments for 'sadd' command");
            }

            var result = DbSet.AddOrUpdate(key, values, (_, value) => value.ToList().Union(values).ToArray());
            return Task.FromResult(result.LongLength);
        }

        public Task<bool> SetRemoveAsync(RedisKey key, RedisValue value)
        {
            RedisValue[] result;
            DbSet.TryGetValue(key, out result);
            if (result == null)
            {
                return Task.FromResult(false);
            }

            var update = DbSet.TryUpdate(key, result.Where(val => !val.Equals(value)).ToArray(), result);

            return Task.FromResult(update);
        }

        public Task<bool> SetRemoveAsync(RedisKey key, RedisValue[] values)
        {
            RedisValue[] result;
            DbSet.TryGetValue(key, out result);
            if (result == null)
            {
                return Task.FromResult(false);
            }

            DbSet.TryUpdate(key, result.Where(val => !values.Contains(val)).ToArray(), result);

            return Task.FromResult(true);
        }

        public Task<RedisValue[]> SetMembersAsync(RedisKey key)
        {
            RedisValue[] values;
            DbSet.TryGetValue(key, out values);
            return Task.FromResult(values ?? new RedisValue[0]);
        }

        public Task<RedisValue[]> SetRandomMembersAsync(RedisKey key, long count)
        {
            RedisValue[] values;
            DbSet.TryGetValue(key, out values);

            RedisValue[] result;
            if (values != null)
            {
                ThreadSafeRandom.Shuffle(values);
                if (values.Length < count)
                {
                    result = values;
                }
                else
                {
                    result = values.ToList().GetRange(0, (int)count).ToArray();
                }
            }
            else
            {
                result = new RedisValue[0];
            }

            return Task.FromResult(result);
        }

        public Task<long> SetLengthAsync(RedisKey key)
        {
            RedisValue[] values;
            DbSet.TryGetValue(key, out values);

            long total = 0;

            if (values != null)
            {
                total = values.Length;
            }

            return Task.FromResult(total);
        }

        public void Execute()
        {
            // Even batched calls are immediately executed in the Mock
        }

        private RedisValue SetBit(RedisValue redisValue, long offset, bool bit)
        {
            int n = (int)offset / 8;
            var bytes = PadByteArray(redisValue, n);

            offset = offset % 8;
            int x = bit ? 1 : 0;
            byte value = bytes[n];
            bytes[n] = (byte)(value ^ ((-x ^ value) & (1 << (7 - (int)offset))));
            return bytes.ToArray();
        }

        private RedisValue SetRange(RedisValue redisValue, long offset, RedisValue bits)
        {
            byte[] existingValue = redisValue;
            byte[] newValueRange = bits;

            int oldLength = existingValue.Length;
            int offsetPlusRange = (int)offset + newValueRange.Length;

            int newLength = Math.Max(oldLength, offsetPlusRange);

            byte[] newArray = new byte[newLength];
            Array.Copy(existingValue, newArray, existingValue.Length);
            Array.Copy(newValueRange, 0, newArray, offset, newValueRange.Length);
            return newArray;
        }

        private IList<byte> PadByteArray(RedisValue redisValue, int n)
        {
            var bytes = new List<byte>((byte[])redisValue);

            int byteCount = bytes.Count;
            if (n + 1 > byteCount)
            {
                for (int i = 0; i <= n - byteCount; i++)
                {
                    bytes.Add(0);
                }
            }

            return bytes;
        }

        public Task<RedisResult> ExecuteScriptAsync(string script, RedisKey[] keys, RedisValue[] values)
        {
            throw new NotImplementedException();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public Task<HashEntry[]> HashGetAllAsync(RedisKey key, CommandFlags command = CommandFlags.None)
        {
            if (_dbHash.TryGetValue(key, out var entries))
            {
                return Task.FromResult(entries.Select(kvp => new HashEntry(kvp.Key, kvp.Value)).ToArray());
            }
            return Task.FromResult<HashEntry[]>(null);
        }

        public Task<bool> HashSetAsync(
            RedisKey key,
            RedisValue hashField,
            RedisValue value,
            When when = When.Always,
            CommandFlags flags = CommandFlags.None)
        {
            var fields = _dbHash.GetOrAdd(key, _ => new ConcurrentDictionary<RedisValue, RedisValue>());
            fields.AddOrUpdate(hashField, value, (field, prev) => value);
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public Task<RedisValue> HashGetAsync(RedisKey key, RedisValue hashField, CommandFlags flags)
        {
            if (_dbHash.TryGetValue(key, out var fields))
            {
                if (fields.TryGetValue(hashField, out var value))
                {
                    return Task.FromResult(value);
                }
            }
            return Task.FromResult<RedisValue>(default);
        }
    }
}
