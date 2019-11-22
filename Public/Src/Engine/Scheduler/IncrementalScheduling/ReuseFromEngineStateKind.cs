// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Reasons for <see cref="IIncrementalSchedulingState"/> to be reusable or not from the engine state.
    /// </summary>
    public enum ReuseFromEngineStateKind
    {
        /// <summary>
        /// The existing state has mismatched id than the expected one.
        /// </summary>
        MismatchedId,

        /// <summary>
        /// Graph has changed.
        /// </summary>
        ChangedGraph,

        /// <summary>
        /// Engine state id cannot be verified, e.g., incremental scheduling state cannot be loaded.
        /// </summary>
        UnverifiedEngineStateId,

        /// <summary>
        /// Engine state id does not match the expected one.
        /// </summary>
        MismatchedEngineStateId,

        /// <summary>
        /// The existing state is reusable.
        /// </summary>
        Reusable
    }
}
