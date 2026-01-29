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
        /// <remarks>
        /// For Windows, this means the executing process is running elevated. For Linux, that 'sudo' can be successfully executed
        /// without user interaction
        /// </remarks>
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
        /// Requires ability to use Windows Projected Filesystem
        /// (also includes symlink permission as this is required by current VFS implementation)
        /// </summary>
        WindowsProjFs = 1 << 8 | WindowsOs | SymlinkPermission,

        /// <summary>
        /// Requires running on either Windows or Mac operating system (excluding Linux)
        /// </summary>
        WindowsOrMacOs = 1 << 9,

        /// <summary>
        /// Used to disable a test. Typically used with #ifdef
        /// </summary>
        NotSupported = 1 << 10,

        /// <summary>
        /// Requires running on either Windows or Linux operating system (excludes macOS)
        /// </summary>
        WindowsOrLinuxOs = 1 << 11,

        /// <summary>
        /// Requires running on a Linux OS
        /// </summary>
        LinuxOs = 1 << 12,

        /// <summary>
        /// Requires EBPF to be enabled on Linux
        /// </summary>
        EBPFEnabled = 1 << 13,
    }
}
