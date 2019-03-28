// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// The result of a Redis TrimOrGetLastAccessTime operation
    /// </summary>
    internal readonly struct RedisLastAccessTimeResult
    {
        /// <summary>
        /// Remote last-access time.
        /// </summary>
        public readonly DateTime LastAccessTime;

        /// <summary>
        /// Number of replicas.
        /// </summary>
        public readonly long LocationCount;

        /// <summary>
        /// Whether or not the content is safe to evict.
        /// </summary>
        public readonly bool SafeToEvict;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisLastAccessTimeResult"/> struct.
        /// </summary>
        public RedisLastAccessTimeResult(bool safeToEvict, DateTime lastAccessTime, long locationCount)
        {
            SafeToEvict = safeToEvict;
            LastAccessTime = lastAccessTime;
            LocationCount = locationCount;
        }
    }
}
