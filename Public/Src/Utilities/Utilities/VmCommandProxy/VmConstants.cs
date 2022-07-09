// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
    /// Special files in VM that pips may access. 
    /// </summary>
    public static class VmSpecialFilesAndDirectories
    {
        /// <summary>
        /// CopyLocalShim.exe in this directory can be injected using Image File Execution Options to intercept processes such as LSBuild.exe
        /// that do not work well when executed from the network mapped D: drive. It will copy the original .exe and its entire folder into the VM
        /// and re-execute the original command-line from there.
        /// CODESYNC (CB codebase): private\Common\VmUtils\VmCommandProxy\VmCommandProxy.cs
        /// </summary>
        public const string CopyLocalShimDirectory = @"C:\VmAgent\Dependencies\CopyLocalShim";

        /// <summary>
        /// Named of shared temp folder that all pips executing in the VM will have untracked accesses.
        /// </summary>
        public const string SharedTempFolder = "BuildXLVmSharedTemp";
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
        /// Environment variable whose value is %TEMP% before it gets relocated, and its presence indicates that the process in VM has relocated temporary folder
        /// whose new value is stored in <see cref="VmTemp"/>.
        /// </summary>
        public const string VmOriginalTemp = "[BUILDXL]VM_ORIGINAL_TEMP";

        /// <summary>
        /// Environment variable whose value is a path to a temp folder shared by all pips executing in the VM.
        /// </summary>
        /// <remarks>
        /// Shared temp folder is needed particularly in the case of pips needing to use <see cref="VmSpecialFilesAndDirectories.CopyLocalShimDirectory"/>.
        /// One pip will copy the tool into a folder in this shared temp folder once, and other pips will simply use the copied tool in that folder. Without
        /// this folder, each pip will copy the tool into its own temp folder. Not only that this has performance impact, the VM will run out of space quickly.
        /// </remarks>
        public const string VmSharedTemp = "[BUILDXL]VM_SHARED_TEMP";

        /// <summary>
        /// Property indicating if a process is running in VM.
        /// </summary>
        public static bool IsRunningInVm => GetFlag(IsInVm);

        /// <summary>
        /// Property indicating if the process in VM has relocated temporary folder.
        /// </summary>
        public static bool HasRelocatedTemp => IsRunningInVm && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VmTemp));

        /// <summary>
        /// Prefix for host environment variable.
        /// </summary>
        public const string HostEnvVarPrefix = "[BUILDXL]VM_HOST_";

        /// <summary>
        /// Environment variable containing path to the host's user profile, or a redirected one.
        /// </summary>
        public static readonly string HostUserProfile = $"{HostEnvVarPrefix}USERPROFILE";

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

            /// <summary>
            /// Host name.
            /// </summary>
            public const string Name = "BuilderHost";
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

            /// <summary>
            /// Prefix for ApplicationData special folder.
            /// </summary>
            public readonly static string AppDataPrefix = $@"{Path}\AppData";

            /// <summary>
            /// LocalApplicationData special folder.
            /// </summary>
            public readonly static string LocalAppData = $@"{AppDataPrefix}\Local";

            private static Func<string> GetEnvFolderFunc(Environment.SpecialFolder folder) => new Func<string>(() => Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify));

            /// <summary>
            /// Environment variables related to user profile.
            /// </summary>
            public readonly static Dictionary<string, (string value, Func<string> toVerify)> Environments = new Dictionary<string, (string, Func<string>)>(StringComparer.OrdinalIgnoreCase)
            {
                { "APPDATA",          ($@"{AppDataPrefix}\Roaming",                                                    GetEnvFolderFunc(Environment.SpecialFolder.ApplicationData)) },
                { "LOCALAPPDATA",     (LocalAppData,                                                                   GetEnvFolderFunc(Environment.SpecialFolder.LocalApplicationData)) },
                { "USERPROFILE",      (Path,                                                                           GetEnvFolderFunc(Environment.SpecialFolder.UserProfile)) },
                { "USERNAME",         (Name,                                                                           null) },
                { "HOMEDRIVE",        (System.IO.Path.GetPathRoot(Path).Trim(System.IO.Path.DirectorySeparatorChar),   null) }, 
                { "HOMEPATH",         (Path.Substring(2),                                                              null) },
                { "INTERNETCACHE" ,   ($@"{LocalAppData}\Microsoft\Windows\INetCache",                                 GetEnvFolderFunc(Environment.SpecialFolder.InternetCache)) },
                { "INTERNETHISTORY",  ($@"{LocalAppData}\Microsoft\Windows\History",                                   GetEnvFolderFunc(Environment.SpecialFolder.History)) },
                { "INETCOOKIES",      ($@"{LocalAppData}\Microsoft\Windows\INetCookies",                               GetEnvFolderFunc(Environment.SpecialFolder.Cookies)) },
                { "LOCALLOW" ,        ($@"{AppDataPrefix}\LocalLow",                                                   new Func<string>(() => GetKnownFolderPath(new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16")))) },
            };

            [DllImport("shell32.dll")]
            private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

            private static string GetKnownFolderPath(Guid knownFolderId)
            {
                IntPtr pszPath = IntPtr.Zero;
                try
                {
                    int hr = SHGetKnownFolderPath(knownFolderId, 0, IntPtr.Zero, out pszPath);
                    return hr >= 0 ? Marshal.PtrToStringAuto(pszPath) : throw Marshal.GetExceptionForHR(hr);
                }
                finally
                {
                    if (pszPath != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(pszPath);
                    }
                }
            }
        }
    }
}
