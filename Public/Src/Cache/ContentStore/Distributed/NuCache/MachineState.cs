// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents machine state in a cluster.
    /// </summary>
    public enum MachineState
    {
        /// <summary>
        /// Machine state is unknown.
        /// </summary>
        Unknown,

        /// <summary>
        /// Machine is available to get content.
        /// </summary>
        Active,

        /// <summary>
        /// Machine is considered missing until next successful heartbeat
        /// </summary>
        Unavailable,

        /// <summary>
        /// Machine heartbeat is sufficiently old so it has been marked as dead
        /// All content locations associated with the machine will be scrubbed
        /// </summary>
        Expired
    }
}
