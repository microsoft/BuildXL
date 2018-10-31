// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Desired access flags for <see cref="Windows.FileSystemWin.CreateFileW"/>
    /// </summary>
    [Flags]
    public enum FileDesiredAccess : uint
    {
        /// <summary>
        /// No access requested.
        /// </summary>
        None = 0,

        /// <summary>
        /// Waitable handle (always required by CreateFile?)
        /// </summary>
        Synchronize = 0x00100000,

        /// <summary>
        /// Object can be deleted.
        /// </summary>
        Delete = 0x00010000,

        /// <summary>
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa364399(v=vs.85).aspx
        /// </summary>
        GenericRead = 0x80000000,

        /// <summary>
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa364399(v=vs.85).aspx
        /// </summary>
        GenericWrite = 0x40000000,

        /// <summary>
        /// Can read file or directory attributes.
        /// </summary>
        FileReadAttributes = 0x0080,

        /// <summary>
        /// The right to write file attributes.
        /// </summary>
        FileWriteAttributes = 0x00100,
    }
}
