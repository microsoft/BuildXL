// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    /// Represents the rank of the replica of the given eviction candidate from this machine
    /// </summary>
    public enum ReplicaRank : byte
    {
        /// <summary>
        /// No rank. Use standard eviction ordering.
        /// </summary>
        None,

        /// <summary>
        /// Replica is important. Use eviction ordering for important replicas.
        /// </summary>
        Important,

        /// <summary>
        /// Replica is designated. Same behavior as <see cref="Important"/>. Separate for logging purposes.
        /// </summary>
        Designated,

        /// <summary>
        /// Replica is protected. All other content of lower rank must be evicted first.
        /// </summary>
        Protected
    }
}
