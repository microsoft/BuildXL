// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// The status of the worker, <see cref="Worker"/>
    /// The status has sense only for remote nodes but is declared in the interface
    /// to node handling uniform.
    ///
    /// Normal State tranistions:
    /// Stopped -> Starting -> Started -> Attached -> Running -> Stopping -> Stopped
    ///
    /// Stopped -> Starting: Triggered by call to Start
    /// Starting -> Started: Triggered when node attach call completes
    /// Started -> Attached: Triggered when node is attached (i.e. pip graph downloaded)
    /// Attached -> Running: Triggered when node attach completion is acknowledged
    /// Running -> Stopped: Triggered by call to Exit when node is shutwon
    ///
    /// Failure transitions:
    /// Any state -> Stopped: Triggered when error condition occurs and node should no longer be used
    /// Running -> Paused: RPC calls to node have failed and node is paused until a successful heartbeat
    /// Paused -> Running: Triggered on successful heartbeat after entering paused state
    /// </summary>
    public enum WorkerNodeStatus
    {
        /// <summary>
        /// The node has not been started
        /// </summary>
        NotStarted,

        /// <summary>
        /// The node is not running and cannot accept commands
        /// </summary>
        Stopped,

        /// <summary>
        /// Calling Attach() and waiting for acknowledgement.
        /// </summary>
        Starting,

        /// <summary>
        /// Attach() is called and the node is waiting for Attach completion (i.e. pip graph downloaded on worker).
        /// </summary>
        Started,

        /// <summary>
        /// The node is attached and waiting to verify cache connection to worker.
        /// </summary>
        Attached,

        /// <summary>
        /// The node is running and can processs new requests.
        /// </summary>
        Running,

        /// <summary>
        /// The node is running but prefer not to get new requests.
        /// A request will not be denied (to prevent time races) but has a good chance to fail.
        /// </summary>
        Paused,

        /// <summary>
        /// The node received a stop request.
        /// </summary>
        Stopping,
    }
}
