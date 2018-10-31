// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
