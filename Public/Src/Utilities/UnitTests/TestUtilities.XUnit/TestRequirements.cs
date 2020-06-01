// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Tracing;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Represents machine and environment requirements of tests
    /// </summary>
    [Flags]
    public enum TestRequirements
    {
        /// <summary>
        /// Test has no special requirements
        /// </summary>
        None = 0,

        /// <summary>
        /// Requires Admin priviledges
        /// </summary>
        Admin = 1 << 0,

        /// <summary>
        /// Requires ability to perform USN journal scan
        /// </summary>
        JournalScan = 1 << 1,

        /// <summary>
        /// Requires ability to create symlinks
        /// </summary>
        SymlinkPermission = 1 << 2,

        /// <summary>
        /// Requires running on Windows operating system
        /// </summary>
        WindowsOs = 1 << 3,

        /// <summary>
        /// Requires running on Unix based operating system
        /// </summary>
        UnixBasedOs = 1 << 4,

        /// <summary>
        /// Requires running on Mac operating systsem
        /// </summary>
        MacOs = 1 << 5,

        /// <summary>
        /// Requires Helium drivers present
        /// </summary>
        HeliumDriversAvailable = 1 << 6,

        /// <summary>
        /// Requires Helium drivers not present
        /// </summary>
        HeliumDriversNotAvailable = 1 << 7,

        /// <summary>
        /// Requires ability to use Windows Projected Filesystem
        /// (also includes symlink permission as this is required by current VFS implementation)
        /// </summary>
        WindowsProjFs = 1 << 8 | WindowsOs | SymlinkPermission,
    }
}
