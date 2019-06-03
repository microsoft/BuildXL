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
        public const string InitializeVm = nameof(InitializeVm);

        /// <summary>
        /// Run process in VM.
        /// </summary>
        public const string Run = nameof(Run);

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

    /// <summary>
    /// Constants for IO in Vm.
    /// </summary>
    public static class VmIOConstants
    {
        /// <summary>
        /// IO for temporary folder.
        /// </summary>
        public static class Temp
        {
            /// <summary>
            /// Drive for temporary folder.
            /// </summary>
            public const string Drive = "T:";

            /// <summary>
            /// Root for temporary folder.
            /// </summary>
            public static readonly string Root = $@"{Drive}\BxlInt\Temp";
        }
    }
}
