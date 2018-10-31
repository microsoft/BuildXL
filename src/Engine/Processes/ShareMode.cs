// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Processes
{
    /// <summary>
    /// The requested sharing mode of the file or device.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1714:FlagsEnumsShouldHavePluralNames")]
    [SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue")]
    [Flags]
    public enum ShareMode : uint
    {
        /// <summary>
        /// Prevents other processes from opening a file or device if they request delete, read, or write access.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_SHARE_NONE = 0x0,

        /// <summary>
        /// Enables subsequent open operations on a file or device to request read access.
        /// </summary>
        /// <remarks>
        /// Otherwise, other processes cannot open the file or device if they request read access.
        /// If this flag is not specified, but the file or device has been opened for read access, the function fails.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_SHARE_READ = 0x1,

        /// <summary>
        /// Enables subsequent open operations on a file or device to request write access.
        /// </summary>
        /// <remarks>
        /// Otherwise, other processes cannot open the file or device if they request write access.
        /// If this flag is not specified, but the file or device has been opened for write access or has a file mapping with write
        /// access, the function fails.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_SHARE_WRITE = 0x2,

        /// <summary>
        /// Enables subsequent open operations on a file or device to request delete access.
        /// </summary>
        /// <remarks>
        /// Otherwise, other processes cannot open the file or device if they request delete access.
        /// If this flag is not specified, but the file or device has been opened for delete access, the function fails.
        /// Note  Delete access allows both delete and rename operations.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_SHARE_DELETE = 0x4,
    }
}
