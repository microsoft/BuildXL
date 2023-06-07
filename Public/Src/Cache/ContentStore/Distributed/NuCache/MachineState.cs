// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.NuCache;

/// <summary>
/// Represents the state of a machine in the cluster.
/// </summary>
/// <remarks>
/// Machine states are reported in <see cref="ClusterState"/>.
/// </remarks>
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
    /// Machine is available.
    /// </summary>
    Open,

    /// <summary>
    /// Machine is considered to be permanently unavailable.
    /// </summary>
    /// <remarks>
    /// WARNING: This state is only computed locally due to clock accuracy issues.
    /// 
    /// Impact on the cache:
    /// 1. Copies: Dead machines will never be contacted.
    /// 2. Content locations: Dead machines are pruned from content tracking.
    /// 3. Machine ID assignment: the ID is eligible for takeover by another machine.
    /// </remarks>
    DeadUnavailable,

    /// <summary>
    /// Machine's last heartbeat is sufficiently old so it has been marked as dead.
    /// </summary>
    /// <remarks>
    /// WARNING: This state is only computed locally due to clock accuracy issues.
    ///
    /// Impact on the cache:
    /// 1. Copies: Dead machines will never be contacted.
    /// 2. Content locations: Dead machines are pruned from content tracking.
    /// 3. Machine ID assignment: the ID is NOT eligible for takeover by another machine.
    /// </remarks>
    DeadExpired,

    /// <summary>
    /// Machine has voluntarily declared that they are unavailable for a short period of time.
    /// </summary>
    /// <remarks>
    /// This is intended to be a temporary state used when a machine is doing a short-lived maintenance task. For
    /// example, restarting the service.
    ///
    /// Impact on the cache:
    /// 1. Copies: will still attempt to contact Closed machines, but will do so as a last resort.
    /// 2. Content locations: Closed machines are still tracked and won't be scrubbed.
    /// 3. Machine ID assignment: the ID is NOT eligible for takeover by another machine.
    /// </remarks>
    Closed,
}
