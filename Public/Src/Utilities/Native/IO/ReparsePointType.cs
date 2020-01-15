// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Enum that defines the possible actionable reparse point types.
    /// </summary>
    public enum ReparsePointType
    {
        /// <nodoc />
        None = 0,

        /// <nodoc />
        SymLink = 1,

        /// <nodoc />
        MountPoint = 2,

        /// <nodoc />
        NonActionable = 3,
    }
}
