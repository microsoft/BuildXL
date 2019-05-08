// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.VmCommandProxy
{
    /// <summary>
    /// Commands used by VmCommandProxy.
    /// </summary>
    public static class VmCommand
    {
        /// <summary>
        /// Initialize VM.
        /// </summary>
        public const string StartBuild = "StartBuild";

        /// <summary>
        /// Run process in VM.
        /// </summary>
        public const string Run = "Run";

        /// <summary>
        /// Commands' parameters.
        /// </summary>
        public static class Param
        {
            /// <summary>
            /// Input JSON file.
            /// </summary>
            public const string InputJsonFile = "InputJsonFile";

            /// <summary>
            /// Output JSON file.
            /// </summary>
            public const string OutputJsonFile = "OutputJsonFile";
        }
    }

    /// <summary>
    /// Executable.
    /// </summary>
    public static class VmExecutable
    {
        /// <summary>
        /// Default relative path.
        /// </summary>
        public const string DefaultRelativePath = @"tools\VmCommandProxy\tools\VmCommandProxy.exe";
    }
}
