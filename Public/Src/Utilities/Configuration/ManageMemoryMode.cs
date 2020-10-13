// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Modes for managing memory
    /// </summary>
    public enum ManageMemoryMode : byte
    {
        /// <summary>
        /// Cancellation based on ram usage
        /// </summary>
        CancellationRam,

        /// <summary>
        /// Cancellation based on commit usage
        /// </summary>
        CancellationCommit,

        /// <summary>
        /// Empty working set of processes
        /// </summary>
        EmptyWorkingSet,

        /// <summary>
        /// Empty working set and suspend processes
        /// </summary>
        Suspend,

        /// <summary>
        /// Resume suspended processes
        /// </summary>
        Resume,

        /// <summary>
        /// Cancel suspended processes
        /// </summary>
        CancelSuspended
    }
}
