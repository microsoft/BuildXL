// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;

namespace BuildXL.Cache.ContentStore.Interfaces.Utils
{
    /// <nodoc />
    public static class OperatingSystemHelper
    {
        /// <summary>
        /// Indicates if ContentStore is running on a Unix based operating system (i.e., Linux and macOS).
        /// </summary>
        /// <remarks>This is used for as long as we have older .NET Framework dependencies. Copied from BuildXL.Utilities.OperatingSystemHelper.IsUnixOS.</remarks>
        public static readonly bool IsUnixOS = Environment.OSVersion.Platform == PlatformID.Unix;

        /// <summary>
        /// Indicates if ContentStore is running on Linux.
        /// </summary>
        public static readonly bool IsLinuxOS =
#if NETCOREAPP
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
#else
            false;
#endif

        /// <summary>
        /// Returns true if the current platform is a Windows platform.
        /// </summary>
        public static bool IsWindowsOS { get; } = Environment.OSVersion.Platform != PlatformID.MacOSX && Environment.OSVersion.Platform != PlatformID.Unix;
    }
}
