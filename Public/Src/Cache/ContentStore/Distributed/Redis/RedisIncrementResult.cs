// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
