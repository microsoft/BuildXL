// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Change reason for incremental scheduling state.
    /// </summary>
    internal enum PreciseChangeReason
    {
        /// <summary>
        /// Changes are precisely captured.
        /// </summary>
        /// <remarks>
        /// All changes in changed journal are processed and are refelected in the updated incremental scheduling state.
        /// </remarks>
        PreciseChange,

        /// <summary>
        /// Changes are imprecisely captured due to failed journal scan.
        /// </summary>
        FailedJournalScanning,

        /// <summary>
        /// Changes are imprecisely captured due to added new artifacts.
        /// </summary>
        NewFilesOrDirectoriesAdded,
    }
}
