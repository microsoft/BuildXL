// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using StackExchange.Redis;

namespace ContentStoreTest.Distributed.Redis
{
    public class FailureInjectingRedisDatabase : MockRedisDatabase
    {
        public int FailingQuery { private get; set; } = 1;

        public bool ThrowRedisException { private get; set; } = true;

        public int Calls { get; private set; }

        public FailureInjectingRedisDatabase(IClock clock, IDictionary<RedisKey, RedisValue> initialData = null)
            : base(clock, initialData)
        {
        }

        public override Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags command = CommandFlags.None)
        {
            ThrowIfConfigured();
            return base.StringGetAsync(key, command);
        }

        public override Task<bool> StringSetAsync(RedisKey key, RedisValue value, When condition)
        {
            ThrowIfConfigured();
            return base.StringSetAsync(key, value, condition);
        }

        private void ThrowIfConfigured()
        {
            if (++Calls != FailingQuery)
            {
                return;
            }

            if (ThrowRedisException)
            {
                // RedisException doesn't have any public constructors so creating an object without trying to call constructors
                Type exceptionType = typeof(RedisException);
                throw (RedisException)FormatterServices.GetUninitializedObject(exceptionType);
            }
            else
            {
                throw new InvalidOperationException("Unknown exception has occurred.");
            }
        }
    }
}
