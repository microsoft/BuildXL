// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents machine state in a cluster.
    ///
    /// The machine status boils down to the following conceptual states:
    ///     - Open: the machine is up and ready for business
    ///     - Closed: the machine shutdown gracefully not too long ago, and hasn't been reimaged
    ///     - Dead: machine has been reimaged, or shutdown gracefully some time ago
    ///     - Resurrecting: machine was dead, but is now booting up.
    ///     - Non-Existent: machine has been permanently removed, and all of its content scrubbed
    /// The last two states are not actually reified, but are conceptually different from the others in terms of what
    /// transitions may be performed.
    ///
    /// When a machine is closed, we consider it will reopen soon, so it doesn't affect any other subsystem of the
    /// cache except for downgrading reputation.
    ///
    /// When a machine is dead, its content will be purged from content tracking over time.
    /// </summary>
    public enum MachineState
    {
        /// <summary>
        /// Machine state is unknown.
        /// </summary>
        /// <remarks>
        /// It is not possible to manually set the machine state to unknown. When heartbeating with a Unknown state,
        /// the actual state won't be updated.
        /// </remarks>
        Unknown,

        /// <summary>
        /// Machine is available
        /// </summary>
        Open,

        /// <summary>
        /// Machine is considered missing until next successful heartbeat
        /// </summary>
        DeadUnavailable,

        /// <summary>
        /// Machine heartbeat is sufficiently old so it has been marked as dead
        /// All content locations associated with the machine will be scrubbed
        /// </summary>
        DeadExpired,

        /// <summary>
        /// Machine shut down gracefully not too long ago, and hasn't been reimaged
        /// </summary>
        Closed,
    }
}
