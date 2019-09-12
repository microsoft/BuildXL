// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
