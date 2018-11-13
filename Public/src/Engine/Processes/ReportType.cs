// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Processes
{
    // Keep this in sync with the C++ version declared in DataTypes.h

    /// <summary>
    /// Flags indicating the status of a file access
    /// </summary>
    public enum ReportType
    {
        /// <summary>
        /// Unknown status
        /// </summary>
        None = 0,

        /// <summary>
        /// Report file access
        /// </summary>
        FileAccess = 1,

        /// <summary>
        /// Report a windows file
        /// </summary>
        WindowsCall = 2,

        /// <summary>
        /// Report a debug message
        /// </summary>
        DebugMessage = 3,

        /// <summary>
        /// Report a summary of process data such as process times and process IO counters
        /// </summary>
        ProcessData = 4,

        /// <summary>
        /// Report a process detouring status.
        /// </summary>
        ProcessDetouringStatus = 5,

        /// <summary>
        /// This is a non-value, but places an upper-bound on the range of the enum
        /// </summary>
        Max = 6,
    }
}
