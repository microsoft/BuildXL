// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
