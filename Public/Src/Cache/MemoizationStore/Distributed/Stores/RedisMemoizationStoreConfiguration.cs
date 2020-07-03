// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.Redis;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <nodoc />
    public class RedisMemoizationStoreConfiguration : RedisContentLocationStoreConfiguration
    {
        /// <summary>
        /// Time before memoization entries expire.
        /// </summary>
        public TimeSpan MemoizationExpiryTime { get; set; }

        /// <nodoc />
        public RedisMemoizationStoreConfiguration()
        {
        }
    }
}
