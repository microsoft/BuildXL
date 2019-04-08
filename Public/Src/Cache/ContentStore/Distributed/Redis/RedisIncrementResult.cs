// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// The result of a redis increment operation
    /// </summary>
    internal readonly struct RedisIncrementResult
    {
        /// <summary>
        /// Gets the actually applied increment to the redis value
        /// </summary>
        public readonly long AppliedIncrement;

        /// <summary>
        /// Gets the incremented value
        /// </summary>
        public readonly long IncrementedValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisIncrementResult"/> struct.
        /// </summary>
        public RedisIncrementResult(long appliedIncrement, long incrementedValue)
        {
            AppliedIncrement = appliedIncrement;
            IncrementedValue = incrementedValue;
        }
    }
}
