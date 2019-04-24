// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Processes
{
    // Keep this in sync with the C++ version declared in DataTypes.h

    /// <summary>
    /// Flags indicating the status of a file access
    /// </summary>
    [Flags]
    public enum FileAccessStatus : byte
    {
        /// <summary>
        /// Unknown status
        /// </summary>
        None = 0,

        /// <summary>
        /// File access was allowed according to manifest
        /// </summary>
        Allowed = 1,

        /// <summary>
        /// File access was denied according to manifest
        /// </summary>
        Denied = 2,

        /// <summary>
        /// File access policy couldn't be determined as path couldn't be canonicalized
        /// </summary>
        CannotDeterminePolicy = 3,
    }
}
