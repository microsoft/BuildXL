// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Processes
{
    /// <summary>
    /// The requested access to the file or device
    /// </summary>
    /// <remarks>
    /// See https://docs.microsoft.com/en-us/windows/win32/fileio/file-access-rights-constants for list of values
    /// </remarks>
    [Flags]
    public enum DesiredAccess : uint
    {
        /// <summary>
        /// For a directory, the right to list the contents of the directory.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_LIST_DIRECTORY = 0x00000001,

        /// <summary>
        /// For a directory, the right to create a file in the directory.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_ADD_FILE = 0x00000002,

        /// <summary>
        /// For a directory, the right to create a subdirectory.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_ADD_SUBDIRECTORY = 0x00000004,

        /// <summary>
        /// The right to read extended file attributes.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_READ_EA = 0x00000008,

        /// <summary>
        /// Right to delete an object.
        /// </summary>
        DELETE = 0x00010000,

        /// <summary>
        /// Right to wait on a handle.
        /// </summary>
        SYNCHRONIZE = 0x00100000,

        /// <summary>
        /// For a file object, the right to append data to the file. (For local files, write operations will not overwrite existing
        /// data if this flag is specified without FILE_WRITE_DATA.) For a directory object, the right to create a subdirectory
        /// (FILE_ADD_SUBDIRECTORY).
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_APPEND_DATA = 0x00000004,

        /// <summary>
        /// The right to write extended file attributes.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_WRITE_EA = 0x00000010,

        /// <summary>
        /// For a native code file, the right to execute the file. This access right given to scripts may cause the script to be executable, depending on the script interpreter.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_EXECUTE = 0x00000020,

        /// <summary>
        /// For a directory, the right to delete a directory and all the files it contains, including read-only files.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_DELETE_CHILD = 0x00000040,

        /// <summary>
        /// The right to read file attributes.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_READ_ATTRIBUTES = 0x00000080,

        /// <summary>
        /// The right to write file attributes.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_WRITE_ATTRIBUTES = 0x00000100,

        /// <summary>
        /// For a file object, the right to write data to the file. For a directory object, the right to create a file in the
        /// directory (FILE_ADD_FILE).
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        FILE_WRITE_DATA = 0x00000002,

        /// <summary>
        /// All possible access rights
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        GENERIC_ALL = 0x10000000,

        /// <summary>
        /// Execute access
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        GENERIC_EXECUTE = 0x20000000,

        /// <summary>
        /// Write access
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        GENERIC_WRITE = 0x40000000,

        /// <summary>
        /// Read access
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
        GENERIC_READ = 0x80000000,
    }
}
