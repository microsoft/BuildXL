// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Processes
{
    /// <summary>
    /// Level of access requested by a reported file operation (e.g. CreateFile can request read, write or both).
    /// </summary>
    /// <remarks>
    /// This must agree with the enum definition for RequestedAccess in FileAccessHelpers.h
    /// </remarks>
    [Flags]
    public enum RequestedAccess : byte
    {
        /// <summary>
        /// No access requested.
        /// </summary>
        None = 0,

        /// <summary>
        /// Read access requested.
        /// </summary>
        Read = 1,

        /// <summary>
        /// Write access requested.
        /// </summary>
        Write = 2,

        /// <summary>
        /// Metadata-only probe access requested (e.g. <c>GetFileAttributes</c>).
        /// </summary>
        Probe = 4,

        /// <summary>
        /// Directory enumeration access requested (on the directory itself; immediate children will be enumerated).
        /// </summary>
        Enumerate = 8,

        /// <summary>
        /// Metadata-only probe access requested; probed as part of a directory enumeration (e.g. <c>FindNextFile</c>).
        /// </summary>
        EnumerationProbe = 16,

        /// <summary>
        /// Both read and write access requested.
        /// </summary>
        ReadWrite = Read | Write,

        /// <summary>
        /// All defined access levels requested.
        /// </summary>
        All = Read | Write | Probe | Enumerate | EnumerationProbe,
    }
}
