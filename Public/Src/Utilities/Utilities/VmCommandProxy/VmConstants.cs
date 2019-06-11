// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.VmCommandProxy
{
    /// <summary>
    /// Commands used by VmCommandProxy.
    /// </summary>
    public static class VmCommands
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
        public static class Params
        {
            /// <summary>
            /// Input JSON file.
            /// </summary>
            public const string InputJsonFile = nameof(InputJsonFile);

            /// <summary>
            /// Output JSON file.
            /// </summary>
            public const string OutputJsonFile = nameof(OutputJsonFile);
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
    /// Special environment variable for executions in VM.
    /// </summary>
    public static class VmSpecialEnvironmentVariables
    {
        /// <summary>
        /// Environment variable whose value/presence indicates that the process is running in VM.
        /// </summary>
        public const string IsInVm = "[BUILDXL]IS_IN_VM";

        /// <summary>
        /// Environment variable whose value is %TEMP%, and whose presence indicates that the process in VM has relocated temporary folder.
        /// </summary>
        public const string VmTemp = "[BUILDXL]VM_TEMP";

        /// <summary>
        /// Property indicating if a process is running in VM.
        /// </summary>
        public static bool IsRunningInVm => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsInVm));

        /// <summary>
        /// Property indicating if the process in VM has relocated temporary folder.
        /// </summary>
        public static bool HasRelocatedTemp => IsRunningInVm && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VmTemp));
    }

    /// <summary>
    /// Constants for IO in Vm.
    /// </summary>
    /// <remarks>
    /// These constants constitute a kind of contract between BuildXL and VmCommandProxy.
    /// </remarks>
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
            public const string Drive = "T";

            /// <summary>
            /// Root for temporary folder.
            /// </summary>
            public static readonly string Root = $@"{Drive}:\BxlInt\Temp";
        }

        /// <summary>
        /// IO relating VMs and their hosts.
        /// </summary>
        public static class Host
        {
            /// <summary>
            /// Host's (net shared) drive that is net used by VM.
            /// </summary>
            public const string NetUseDrive = "D";

            /// <summary>
            /// Host IP address.
            /// </summary>
            public const string IpAddress = "192.168.0.1";
        }
    }
}
