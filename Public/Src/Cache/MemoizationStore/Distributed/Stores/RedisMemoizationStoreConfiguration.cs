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

        /// <summary>
        /// Time Delay given to raided redis databases to complete its result after the first redis instance has completed.
        /// </summary>
        public TimeSpan? MemoizationSlowOperationCancellationTimeout { get; set; }

        /// <summary>
        /// A default timeout for memoization database operations.
        /// </summary>
        public static readonly TimeSpan DefaultMemoizationOperationTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// A timeout for memoization database operations.
        /// </summary>
        public TimeSpan MemoizationOperationTimeout { get; set; } = DefaultMemoizationOperationTimeout;

        /// <nodoc />
        public RedisMemoizationStoreConfiguration()
        {
        }
    }
}
