// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public static bool IsRunningInVm => GetFlag(IsInVm);

        /// <summary>
        /// Property indicating if the process in VM has relocated temporary folder.
        /// </summary>
        public static bool HasRelocatedTemp => IsRunningInVm && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VmTemp));

        /// <summary>
        /// Environment variable containing path to the host's user profile, or a redirected one.
        /// </summary>
        public const string HostUserProfile = "[BUILDXL]VM_HOST_USERPROFILE";

        /// <summary>
        /// Environment variable indicating if the host's user profile is a redirected one.
        /// </summary>
        public const string HostHasRedirectedUserProfile = "[BUILDXL]IS_VM_HOST_REDIRECTED_USERPROFILE";

        /// <summary>
        /// Checks if host's user profile has been redirected.
        /// </summary>
        public static bool IsHostUserProfileRedirected => GetFlag(HostHasRedirectedUserProfile);

        private static bool GetFlag(string environmentVariable)
        {
            string value = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.ToLowerInvariant())
            {
                case "0":
                case "false":
                    return false;
                case "1":
                case "true":
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Constants Vm.
    /// </summary>
    /// <remarks>
    /// These constants constitute a kind of contract between BuildXL and VmCommandProxy.
    /// </remarks>
    public static class VmConstants
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
            public static readonly string Root = $@"{Drive}:\BxlTemp";
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

        /// <summary>
        /// User that runs pips in VM.
        /// </summary>
        public static class UserProfile
        {
            /// <summary>
            /// The user that runs the pip in the VM is 'Administrator'.
            /// </summary>
            public const string Name = "Administrator";

            /// <summary>
            /// User profile path.
            /// </summary>
            public readonly static string Path = $@"C:\Users\{Name}";
        }
    }
}
