// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Processes
{
    /// <summary>
    /// An action to take on a file or device that exists or does not exist.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue")]
    public enum CreationDisposition : uint
    {
        /// <summary>
        /// Creates a new file, only if it does not already exist.
        /// </summary>
        /// <remarks>
        /// If the specified file exists, the function fails and the last-error code is set to ERROR_FILE_EXISTS (80).
        /// If the specified file does not exist and is a valid path to a writable location, a new file is created.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        CREATE_NEW = 1,

        /// <summary>
        /// Creates a new file, always.
        /// </summary>
        /// <remarks>
        /// If the specified file exists and is writable, the function overwrites the file, the function succeeds, and last-error
        /// code is set to ERROR_ALREADY_EXISTS (183).
        /// If the specified file does not exist and is a valid path, a new file is created, the function succeeds, and the
        /// last-error code is set to zero.
        /// For more information, see the Remarks section of this topic.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        CREATE_ALWAYS = 2,

        /// <summary>
        /// Opens a file or device, only if it exists.
        /// </summary>
        /// <remarks>
        /// If the specified file or device does not exist, the function fails and the last-error code is set to
        /// ERROR_FILE_NOT_FOUND (2).
        /// For more information about devices, see the Remarks section.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        OPEN_EXISTING = 3,

        /// <summary>
        /// Opens a file, always.
        /// </summary>
        /// <remarks>
        /// If the specified file exists, the function succeeds and the last-error code is set to ERROR_ALREADY_EXISTS (183).
        /// If the specified file does not exist and is a valid path to a writable location, the function creates a file and the
        /// last-error code is set to zero.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        OPEN_ALWAYS = 4,

        /// <summary>
        /// Opens a file and truncates it so that its size is zero bytes, only if it exists.
        /// </summary>
        /// <remarks>
        /// If the specified file does not exist, the function fails and the last-error code is set to ERROR_FILE_NOT_FOUND (2).
        /// The calling process must open the file with the GENERIC_WRITE bit set as part of the dwDesiredAccess parameter.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        TRUNCATE_EXSTING = 5,
    }
}
