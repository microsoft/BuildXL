// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Interop
{
    /// <summary>
    /// Constants for names of various native libraries.
    /// </summary>
    public static class Libraries
    {
        /// <summary>
        /// BuildXL interop library for macOS
        /// </summary>
        public const string BuildXLInteropLibMacOS = "libBuildXLInterop";

        /// <summary>
        /// Standard C Library
        /// </summary>
        public const string LibC = "libc";

        /// <summary>
        /// Windows Kernel32
        /// </summary>
        public const string WindowsKernel32 = "kernel32.dll";

        /// <summary>
        /// Windows Psapi
        /// </summary>
        public const string WindowsPsApi = "Psapi.dll";

        /// <summary>
        /// Windows Psapi
        /// </summary>
        public const string WindowsAdvApi32 = "advapi32.dll";
    }
}
