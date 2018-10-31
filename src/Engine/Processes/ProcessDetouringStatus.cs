// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Processes
{
    // Keep this in sync with the C++ version declared in DataTypes.h

    /// <summary>
    /// Flags indicating the type of the Detouring Status Report.
    /// </summary>
    [System.Flags]
    public enum ProcessDetouringStatus
    {
        /// <summary>
        /// Unknown status
        /// </summary>
        None = 0,

        /// <summary>
        /// About to do CreateProcess
        /// </summary>
        Starting = 1,

        /// <summary>
        /// Process created
        /// </summary>
        Created = 2,

        /// <summary>
        /// Starting to inject.
        /// </summary>
        Injecting = 3,

        /// <summary>
        /// Resuming suspended process
        /// </summary>
        Resuming = 4,

        /// <summary>
        /// Processed resumed.
        /// </summary>
        Resumed = 5,

        /// <summary>
        /// Cleanup started but failed to inject process
        /// </summary>
        Cleanup = 7,

        /// <summary>
        /// Process creation done.
        /// </summary>
        Done = 8,

        /// <summary>
        /// This is a non-value, but places an upper-bound on the range of the enum
        /// </summary>
        Max = 9,
    }
}
