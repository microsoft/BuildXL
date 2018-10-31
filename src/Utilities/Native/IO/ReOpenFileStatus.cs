// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Well-known expected failure cases for <see cref="IFileSystem.TryReOpenFile(Microsoft.Win32.SafeHandles.SafeFileHandle, FileDesiredAccess, System.IO.FileShare, FileFlagsAndAttributes, out Microsoft.Win32.SafeHandles.SafeFileHandle)"/>
    /// </summary>
    public enum ReOpenFileStatus
    {
        /// <summary>
        /// The file was opened (a valid handle was obtained).
        /// </summary>
        Success,

        /// <summary>
        /// The file was opened already with an incompatible share mode, and no handle was obtained.
        /// </summary>
        SharingViolation,

        /// <summary>
        /// The file cannot be opened with the requested access level, and no handle was obtained.
        /// </summary>
        AccessDenied,
    }
}
