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
        FileSymlink = 1,

        /// <nodoc />
        DirectorySymlink = 2,

        /// <nodoc />
        UnixSymlink = 3,

        /// <nodoc />
        Junction = 4,

        /// <nodoc />
        NonActionable = 5
    }
}
