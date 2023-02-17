// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BuildXL.Interop.Unix;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Helper class for cross platform operating system detection and handling.
    /// </summary>
    public static class OperatingSystemHelper
    {
        /// <summary>
        /// Simple struct encapsulating file size
        /// </summary>
        public readonly struct FileSize
        {
            /// <nodoc />
            public ulong Bytes { get; }

            /// <nodoc />
            public int KB => (int)(Bytes >> 10);

            /// <nodoc />
            public int MB => (int)(Bytes >> 20);

            /// <nodoc />
            public int GB => (int)(Bytes >> 30);

            /// <nodoc />
            public FileSize(ulong bytes)
            {
                Bytes = bytes;
            }

            /// <nodoc />
            public FileSize(long bytes) : this((ulong)bytes)
            { }
        }

        /// <summary>
        /// Indicates if BuildXL is running on macOS
        /// </summary>
        public static readonly bool IsMacOS =
#if NETCOREAPP
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#else
            false;
#endif

        /// <summary>
        /// Indicates if BuildXL is running on Linux
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

        /// <summary>
        /// Indicates if BuildXL is running on .NET Core
        /// </summary>
        public static readonly bool IsDotNetCore =
#if NETCOREAPP
            true;
#else
            false;
#endif

        /// <summary>
        /// Indicates if BuildXL is running on a Unix based operating system
        /// </summary>
        /// <remarks>This is used for as long as we have older .NET Framework dependencies</remarks>
        public static readonly bool IsUnixOS = Environment.OSVersion.Platform == PlatformID.Unix;

        /// <summary>
        /// Checks if path comparison case sensitive.
        /// </summary>
        public static bool IsPathComparisonCaseSensitive => IsLinuxOS;

        /// <summary>
        /// Checks if environment variable comparison is case sensitive.
        /// </summary>
        public static bool IsEnvVarComparisonCaseSensitive => IsUnixOS;

        /// <summary>
        /// Comparer to use when comparing paths as strings.
        /// On Linux, a case-sensitive string comparer is returned; elsewhere, a case-insensitive comparer is returned.
        /// </summary>
        public static StringComparer PathComparer { get; } = IsPathComparisonCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// String comparison to use for comparing paths as strings.
        /// </summary>
        public static StringComparison PathComparison { get; } = IsPathComparisonCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        /// <summary>
        /// Comparer to use when comparing environment variable as strings.
        /// </summary>
        public static StringComparer EnvVarComparer { get; } = IsEnvVarComparisonCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// String comparison to use when comparing environment variable as strings.
        /// </summary>
        public static StringComparison EnvVarComparison { get; } = IsEnvVarComparisonCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        /// <summary>
        /// Returns method for canonicalizing paths as strings.
        /// </summary>
        /// <remarks>
        /// The canonicalization is only turning the path into all uppercase letters if path comparison is case insensitive, i.e.,
        /// <see cref="IsPathComparisonCaseSensitive"/> is false. The canonicalization does not eliminate '.' or '..', nor dedupe directory
        /// separators. If path comparison is case sensitive, then the canonicalization method simply returns the string as is.
        /// </remarks>
        public static Func<string, string> CanonicalizePath { get; } = GetPathCanonicalizer();

        private static Func<string, string> GetPathCanonicalizer()
        {
            if (!IsPathComparisonCaseSensitive)
            {
                return path => path.ToUpperInvariant();
            }

            return path => path;
        }

        /// <summary>
        /// Returns method for canonicalizing environment variable names as strings.
        /// </summary>
        /// <remarks>
        /// The canonicalization is only turning the environment variable name into all uppercase letters if the comparison is case insensitive, i.e.,
        /// <see cref="IsEnvVarComparisonCaseSensitive"/> is false. If the comparison is case sensitive, then the canonicalization method simply returns
        /// the string as is.
        /// </remarks>
        public static Func<string, string> CanonicalizeEnvVar { get; } = GetEnvVarCanonicalizer();

        private static Func<string, string> GetEnvVarCanonicalizer()
        {
            if (!IsEnvVarComparisonCaseSensitive)
            {
                return varName => varName.ToUpperInvariant();
            }

            return varName => varName;
        }
    }
}
