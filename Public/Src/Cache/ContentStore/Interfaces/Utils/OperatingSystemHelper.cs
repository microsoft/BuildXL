// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Utils
{
    /// <nodoc />
    public static class OperatingSystemHelper
    {
        /// <summary>
        /// Indicates if ContentStore is running on a Unix based operating system.
        /// </summary>
        /// <remarks>This is used for as long as we have older .NET Framework dependencies. Copied from BuildXL.Utilities.OperatingSystemHelper.IsUnixOS.</remarks>
        public static readonly bool IsUnixOS = Environment.OSVersion.Platform == PlatformID.Unix;

        /// <summary>
        /// Returns true if the current platform is a Windows platform.
        /// </summary>
        public static bool IsWindowsOS { get; } = Environment.OSVersion.Platform != PlatformID.MacOSX && Environment.OSVersion.Platform != PlatformID.Unix;
    }
}
