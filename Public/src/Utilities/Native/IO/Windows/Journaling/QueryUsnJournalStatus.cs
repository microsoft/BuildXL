// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Status indication of <c>FSCTL_QUERY_USN_JOURNAL</c>.
    /// </summary>
    public enum QueryUsnJournalStatus
    {
        /// <summary>
        /// Querying the journal succeeded.
        /// </summary>
        Success,

        /// <summary>
        /// The journal on the specified volume is not active.
        /// </summary>
        JournalNotActive,

        /// <summary>
        /// The journal on the specified volume is being deleted (a later read would return <see cref="JournalNotActive"/>).
        /// </summary>
        JournalDeleteInProgress,

        /// <summary>
        /// The queried volume does not support writing a change journal.
        /// </summary>
        VolumeDoesNotSupportChangeJournals,

        /// <summary>
        /// Incorrect parameter error happens when the volume format is broken.
        /// </summary>
        InvalidParameter,

        /// <summary>
        /// Access denied error when querying the journal.
        /// </summary>
        AccessDenied,
    }
}
