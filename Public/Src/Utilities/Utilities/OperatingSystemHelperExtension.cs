// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Xml.Linq;
using BuildXL.Interop.Unix;
using BuildXL.Utilities.Core;
using Microsoft.Win32;
using static BuildXL.Interop.Windows.Memory;

#pragma warning disable IDE1006 // Naming rule violation

namespace BuildXL.Utilities
{
    /// <summary>
    /// Helper class for cross platform operating system detection and handling.
    /// </summary>
    public static class OperatingSystemHelperExtension
    {
        /// <summary>
        /// List of officially Supported Linux distributions by BuildXL.
        /// </summary>
        public static readonly List<LinuxDistribution> SupportedLinuxDistributions =
        [
            new("ubuntu", new Version("20.04")),
            new("ubuntu", new Version("22.04")),
            new("ubuntu", new Version("24.04")),
            new("mariner", new Version("2.0")),
            new("azurelinux", new Version("3.0"))
        ];

        private static readonly Lazy<Version> CurrentMacOSVersion = new Lazy<Version>(() => GetOSVersionMacOS());

        private static readonly Tuple<string, string> ProcessorNameAndIdentifierMacOS =
            OperatingSystemHelper.IsMacOS ? GetProcessorNameAndIdentifierMacOS() : Tuple.Create(string.Empty, string.Empty);

        // Sysctl constants to query CPU information
        private static readonly string MACHDEP_CPU_BRAND_STRING = "machdep.cpu.brand_string";
        private static readonly string MACHDEP_CPU_MODEL = "machdep.cpu.model";
        private static readonly string MACHDEP_CPU_FAMILY = "machdep.cpu.family";
        private static readonly string MACHDEP_CPU_STEPPING = "machdep.cpu.stepping";
        private static readonly string MACHDEP_CPU_VENDOR = "machdep.cpu.vendor";

        private const int ProcessTimeoutMilliseconds = 1000;

        /// <summary>
        /// Gets the current OS description e.g. "Windows 10 Enterprise 10.0.10240"
        /// </summary>
        public static string GetOSVersion()
        {
            if (!OperatingSystemHelper.IsUnixOS)
            {
                return GetOSVersionWindows();
            }
            else if (OperatingSystemHelper.IsMacOS)
            {
                return string.Format("macOS {0}.{1}.{2}", CurrentMacOSVersion.Value.Major, CurrentMacOSVersion.Value.Minor, CurrentMacOSVersion.Value.Build);
            }
            else if (OperatingSystemHelper.IsLinuxOS)
            {
                return LinuxSystemInfo.GetOSVersion();
            }

            // Extend this once we start supporting Linux etc.
            throw new NotImplementedException("Getting OS version string is not supported on this platform!");
        }

        /// <summary>
        /// Gets the current CPU description e.g. "Intel(R) Xeon(R) CPU E5-1620 v3 @ 3.50GHz"
        /// </summary>
        public static string GetProcessorName()
        {
            if (!OperatingSystemHelper.IsUnixOS)
            {
                return GetProcessorNameWindows();
            }
            else if (OperatingSystemHelper.IsMacOS)
            {
                return ProcessorNameAndIdentifierMacOS.Item1;
            }
            else if (OperatingSystemHelper.IsLinuxOS)
            {
                return LinuxSystemInfo.GetProcessorName();
            }

            // Extend this once we start supporting Linux etc.
            throw new NotImplementedException("Getting CPU name is not supported on this platform!");
        }

        /// <summary>
        /// Gets the current CPU identifier e.g. "Intel64 Family 6 Model 63 Stepping 2, GenuineIntel"
        /// </summary>
        public static string GetProcessorIdentifier()
        {
            if (!OperatingSystemHelper.IsUnixOS)
            {
                return GetProcessorIdentifierWindows();
            }
            else if (OperatingSystemHelper.IsMacOS)
            {
                return ProcessorNameAndIdentifierMacOS.Item2;
            }
            else if (OperatingSystemHelper.IsLinuxOS)
            {
                return LinuxSystemInfo.GetProcessorIdentifier();
            }

            // Extend this once we start supporting Linux etc.
            throw new NotImplementedException("Getting CPU identifier is not supported on this platform!");
        }

        /// <summary>
        /// Gets the current physical memory size
        /// </summary>
        public static OperatingSystemHelper.FileSize GetPhysicalMemorySize()
        {
            if (!OperatingSystemHelper.IsUnixOS)
            {
                return GetPhysicalMemorySizeWindows();
            }
            else
            {
                var buf = new Memory.RamUsageInfo();
                return BuildXL.Interop.Unix.Memory.GetRamUsageInfo(ref buf) == 0
                    ? new OperatingSystemHelper.FileSize(buf.TotalBytes)
                    : new OperatingSystemHelper.FileSize(0);
            }

            // Extend this once we start supporting Linux etc.
            throw new NotImplementedException("Getting physical memory size is not supported on this platform!");
        }

        /// <summary>
        /// Gets the currently available physical memory size
        /// </summary>
        public static OperatingSystemHelper.FileSize GetAvailablePhysicalMemorySize()
        {
            if (!OperatingSystemHelper.IsUnixOS)
            {
                return GetAvailablePhysicalMemorySizeWindows();
            }
            else
            {
                var buf = new Memory.RamUsageInfo();
                return Memory.GetRamUsageInfo(ref buf) == 0
                    ? new OperatingSystemHelper.FileSize(buf.FreeBytes)
                    : new OperatingSystemHelper.FileSize(0);
            }

            // Extend this once we start supporting Linux etc.
            throw new NotImplementedException("Getting physical memory size is not supported on this platform!");
        }

        private static string GetOSVersionWindows()
        {
            try
            {
                using (var pool = Pools.StringBuilderPool.GetInstance())
                {
                    var sb = pool.Instance;
                    using (var currentVersion = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion"))
                    {
                        if (currentVersion != null)
                        {
                            sb.Append(currentVersion.GetValue("ProductName"));
                            sb.Append(' ');
                            sb.Append(currentVersion.GetValue("BuildLabEx"));
                            sb.Append(' ');
                            sb.Append(currentVersion.GetValue("CurrentBuild"));
                            sb.Append('.');
                            sb.Append(currentVersion.GetValue("UBR"));
                        }
                    }

                    return sb.ToString();
                }
            }
#pragma warning disable ERP022 // Checking is best effort.
            catch
            {
            }
#pragma warning restore ERP022

            return string.Empty;
        }

        /// <summary>
        /// Gets the current CPU description e.g. "Intel(R) Xeon(R) CPU E5-1620 v3 @ 3.50GHz"
        /// </summary>
        private static string GetProcessorNameWindows()
        {
            try
            {
                using (RegistryKey processorZero = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (processorZero != null)
                    {
                        var name = processorZero.GetValue("ProcessorNameString");
                        if (name != null)
                        {
                            return name.ToString();
                        }
                    }
                }
            }
#pragma warning disable ERP022 // Checking is best effort.
            catch
            {
            }
#pragma warning restore ERP022

            return string.Empty;
        }

        /// <summary>
        /// Gets the current CPU identifier e.g. "Intel64 Family 6 Model 63 Stepping 2, GenuineIntel"
        /// </summary>
        public static string GetProcessorIdentifierWindows()
        {
            return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        }

        /// <summary>
        /// Gets the current physical memory size in MB
        /// </summary>
        public static OperatingSystemHelper.FileSize GetPhysicalMemorySizeWindows()
        {
            MEMORYSTATUSEX memoryStatusEx = new MEMORYSTATUSEX();

            ulong bytes = GlobalMemoryStatusEx(memoryStatusEx)
                ? memoryStatusEx.ullTotalPhys
                : 0;
            return new OperatingSystemHelper.FileSize(bytes);
        }

        private static OperatingSystemHelper.FileSize GetAvailablePhysicalMemorySizeWindows()
        {
            MEMORYSTATUSEX memoryStatusEx = new MEMORYSTATUSEX();

            ulong bytes = GlobalMemoryStatusEx(memoryStatusEx)
                ? memoryStatusEx.ullAvailPhys
                : 0;
            return new OperatingSystemHelper.FileSize(bytes);
        }

        /// <summary>
        /// Gets .NET Framework version installed on the machine in a human readable form.
        /// </summary>
        public static string GetInstalledDotNetFrameworkVersion()
        {
            if (TryGetInstalledDotNetFrameworkVersion(out var result))
            {
                return result;
            }

            return "No .NET Framework is detected";
        }

        /// <summary>
        /// Returns the current runtime version.
        ///
        /// On .NET Core, the return string is something like ".NETCoreApp, Version=v2.0"
        ///
        /// On .NET Framework, the return string is something like ".NETFramework, Version = v4.7.2".
        /// </summary>
        public static string GetRuntimeFrameworkNameAndVersion()
        {
#if NETSTANDARD2_0
            return ".NETStandard,Version=v2.0";
#else
            return OperatingSystemHelper.IsDotNetCore
                ? Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? ".NETCoreApp"
                : AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName;
#endif
        }

        /// <summary>
        /// Checks the installed .NET Framework version on the machine by looking up the registry.
        /// </summary>
        /// <remarks>
        /// This method does not require elevated permissions.
        /// </remarks>
        /// <returns>
        /// Returns true if .NET Framework version 4.5 or higher is installed and <paramref name="version"/> will contain an exact version.
        /// Otherwise the method returns false.
        /// </returns>
        public static bool TryGetInstalledDotNetFrameworkVersion(out string version)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                version = null;
                return false;
            }

            const string Subkey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\";

            using (var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(Subkey))
            {
                if (ndpKey?.GetValue("Release") != null)
                {
                    var rawValue = (int)ndpKey.GetValue("Release");
                    if (checkFor45PlusVersion(rawValue, out var humanReadableVersion))
                    {
                        version = $"{humanReadableVersion} (raw value: {rawValue})";
                        return true;
                    }
                }

                version = null;
                return false;
            }

            static bool checkFor45PlusVersion(int releaseKey, out string result)
            {
                // The code was adopted from the following docs page:
                // https://docs.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed
                result = null;
                if (releaseKey >= 461808)
                {
                    result = "4.7.2 or later";
                }
                else if (releaseKey >= 461308)
                {
                    result = "4.7.1";
                }
                else if (releaseKey >= 460798)
                {
                    result = "4.7";
                }
                else if (releaseKey >= 394802)
                {
                    result = "4.6.2";
                }
                else if (releaseKey >= 394254)
                {
                    result = "4.6.1";
                }
                else if (releaseKey >= 393295)
                {
                    result = "4.6";
                }
                else if (releaseKey >= 379893)
                {
                    result = "4.5.2";
                }
                else if (releaseKey >= 378675)
                {
                    result = "4.5.1";
                }
                else if (releaseKey >= 378389)
                {
                    result = "4.5";
                }

                // This code should never execute. A non-null release key should mean
                // that 4.5 or later is installed.
                return result != null;
            }
        }

        /// <summary>
        /// Get the current linux distro info from LinuxSystemInfo internal class
        /// </summary>
        public static LinuxDistribution GetLinuxDistribution()
        {
            if (!OperatingSystemHelper.IsLinuxOS)
            {
                return null;
            }

            return LinuxSystemInfo.GetLinuxDistroInfo();
        }

        /// <summary>
        /// Checks if the current Linux distro version on the machine is among those officially supported by BuildXL.
        /// </summary>
        public static bool IsLinuxDistroVersionSupported(out LinuxDistribution distribution)
        {
            distribution = LinuxSystemInfo.GetLinuxDistroInfo();
            return SupportedLinuxDistributions.Contains(distribution);
        }

        /// <summary>
        /// Checks whether the provided kernel version is newer or the same as the current kernel version.
        /// </summary>
        public static bool IsLinuxKernelVersionSameOrNewer(int kernelVersion, int majorRevision, int minorRevision)
        {
            if (!OperatingSystemHelper.IsLinuxOS)
            {
                return false;
            }

            var kernel = LinuxSystemInfo.GetLinuxKernelVersion();

            return kernel.kernelVersion != 0 && kernel.kernelVersion >= kernelVersion && kernel.majorRevision >= majorRevision && kernel.minorRevision >= minorRevision;
        }

        #region macOS Helpers

        private static Version GetOSVersionMacOS()
        {
            try
            {
                XElement dict = XDocument.Load("/System/Library/CoreServices/SystemVersion.plist").Root.Element("dict");
                if (dict != null)
                {
                    foreach (XElement key in dict.Elements("key"))
                    {
                        if ("ProductVersion".Equals(key.Value))
                        {
                            XElement stringElement = key.NextNode as XElement;
                            if (stringElement != null && stringElement.Name.LocalName.Equals("string"))
                            {
                                string versionString = stringElement.Value;
                                if (versionString != null)
                                {
                                    return Version.Parse(versionString);
                                }
                            }
                        }
                    }
                }
            }
#pragma warning disable ERP022 // Checking is best effort.
            catch
            {
            }
#pragma warning restore ERP022

            // Fallback is version 0.0
            return new Version();
        }

        // This could potentially be replaced by a C wrapper querying the system information, the sysctl is just more convinient currently
        private static Tuple<string, string> GetProcessorNameAndIdentifierMacOS()
        {
            try
            {
                var process = new System.Diagnostics.Process()
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = "/usr/sbin/sysctl",
                        RedirectStandardOutput = true,
                        Arguments = "machdep.cpu",
                    }
                };

                process.Start();
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit(ProcessTimeoutMilliseconds);

                // Format of standard output looks like this: 'option_name: value'
                var sysctlOutput = result.Split(Environment.NewLine.ToCharArray());
                var cpuDescription =
                    sysctlOutput.Select(x => x.Split(':')).Where(y => y.Length == 2).ToDictionary(z => z[0], z => z[1].Trim());

                var name = cpuDescription[MACHDEP_CPU_BRAND_STRING];
                var identifier = string.Format("Family {0} Model {1} Stepping {2}, {3}",
                    cpuDescription[MACHDEP_CPU_FAMILY],
                    cpuDescription[MACHDEP_CPU_MODEL],
                    cpuDescription[MACHDEP_CPU_STEPPING],
                    cpuDescription[MACHDEP_CPU_VENDOR]);

                return Tuple.Create(name, identifier);
            }
#pragma warning disable ERP022 // Checking is best effort.
            catch
            {
                return Tuple.Create(string.Empty, string.Empty);
            }
#pragma warning restore ERP022
        }

        #endregion
    }
}
