// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Interop
{
    /// <summary>
    /// Constants for names of various native libraries.
    /// </summary>
    internal static class Libraries
    {
        /// <summary>
        /// BuildXL interop library for macOS
        /// </summary>
        public const string BuildXLInteropLibMacOS = "libBuildXLInterop";

        /// <summary>
        /// Standard C Library
        /// </summary>
        public const string LibC = "libC";

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
