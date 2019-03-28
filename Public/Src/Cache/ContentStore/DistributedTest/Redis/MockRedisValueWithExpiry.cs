// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using StackExchange.Redis;

namespace ContentStoreTest.Distributed.Redis
{
    public class MockRedisValueWithExpiry
    {
        private static readonly TimeSpan ThresholdForExpiry = TimeSpan.FromMinutes(2);

        public MockRedisValueWithExpiry(RedisValue value, DateTime? expiry)
        {
            Value = value;
            Expiry = expiry;
        }

        public RedisValue Value { get; }

        public DateTime? Expiry { get; }

        public override string ToString()
        {
            return $"Value:{Value}; Expiry={Expiry.GetValueOrDefault(DateTime.MinValue)}";
        }

        public override bool Equals(object obj)
        {
            MockRedisValueWithExpiry other = obj as MockRedisValueWithExpiry;
            if (other == null)
            {
                return base.Equals(obj);
            }
            else
            {
                if (!Value.Equals(other.Value))
                {
                    return false;
                }

                if (Expiry == null ^ other.Expiry == null)
                {
                    return false;
                }

                if (Expiry == null)
                {
                    return true;
                }

                if (Expiry.Value >= other.Expiry.Value && (Expiry.Value - other.Expiry.Value < ThresholdForExpiry))
                {
                    return true;
                }

                if (Expiry.Value < other.Expiry.Value && (other.Expiry.Value - Expiry.Value < ThresholdForExpiry))
                {
                    return true;
                }

                return false;
            }
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode() ^ (Expiry.GetHashCode() * 310);
        }
    }
}
