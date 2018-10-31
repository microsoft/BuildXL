// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Processes
{
    /// <summary>
    /// Data-structure that identifies paths to certain files required to realize Detours monitoring
    /// </summary>
    public sealed class FileAccessSetup
    {
        /// <summary>
        /// Path to report file; use "#1234" format to denote a handle to an existing file
        /// </summary>
        public string ReportPath { get; set; }

        /// <summary>
        /// Path to X64 .dll that contains detours instrumentation code
        /// </summary>
        public string DllNameX64 { get; set; }

        /// <summary>
        /// Path to X86 .dll that contains detours instrumentation code
        /// </summary>
        public string DllNameX86 { get; set; }
    }
}
