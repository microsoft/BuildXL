// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using Microsoft.Win32;
using static BuildXL.Interop.Windows.Memory;

#if FEATURE_CORECLR
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
#endif

namespace BuildXL.Utilities
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
        /// Indicates if BuildXL is running on a Unix based operating system
        /// </summary>
        /// <remarks>This is used for as long as we have older .NET Framework dependencies</remarks>
        public static readonly bool IsUnixOS = Environment.OSVersion.Platform == PlatformID.Unix;

#if FEATURE_CORECLR

        // Sysctl constants to query CPU information
        private static string MACHDEP_CPU_BRAND_STRING = "machdep.cpu.brand_string";
        private static string MACHDEP_CPU_MODEL = "machdep.cpu.model";
        private static string MACHDEP_CPU_FAMILY = "machdep.cpu.family";
        private static string MACHDEP_CPU_STEPPING = "machdep.cpu.stepping";
        private static string MACHDEP_CPU_VENDOR = "machdep.cpu.vendor";
        private const int ProcessTimeoutMilliseconds = 1000;

        /// <summary>
        /// Indicates if BuildXL is running on macOS
        /// </summary>
        public static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        /// <summary>
        /// Indicates if Catalina (10.15) or a higher macOS version is running on the host
        /// </summary>
        public static readonly bool IsMacOSCatalinaOrHigher = GetOSVersionMacOS().CompareTo("macOS 10.15.0") <= 0;
        

        private static readonly Tuple<string, string> ProcessorNameAndIdentifierMacOS =
            IsMacOS ? GetProcessorNameAndIdentifierMacOS() : Tuple.Create(String.Empty, String.Empty);

        /// <summary>
        /// Indicates if BuildXL is running on Linux
        /// </summary>
        public static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

#endif

        /// <summary>
        /// Gets the current OS description e.g. "Windows 10 Enterprise 10.0.10240"
        /// </summary>
        public static string GetOSVersion()
        {
            if (!IsUnixOS)
            {
                return GetOSVersionWindows();
            }
#if FEATURE_CORECLR
            else if (IsMacOS)
            {
                return GetOSVersionMacOS();
            }
#endif
            // Extend this once we start supporting Linux etc.
            throw new NotImplementedException("Getting OS version string is not supported on this platform!");
        }

        /// <summary>
        /// Gets the current CPU description e.g. "Intel(R) Xeon(R) CPU E5-1620 v3 @ 3.50GHz"
        /// </summary>
        public static string GetProcessorName()
        {
            if (!IsUnixOS)
            {
                return GetProcessorNameWindows();
            }
#if FEATURE_CORECLR
            else if (IsMacOS)
            {
                return ProcessorNameAndIdentifierMacOS.Item1;
            }
#endif
            // Extend this once we start supporting Linux etc.
            throw new NotImplementedException("Getting CPU name is not supported on this platform!");
        }

        /// <summary>
        /// Gets the current CPU identifier e.g. "Intel64 Family 6 Model 63 Stepping 2, GenuineIntel"
        /// </summary>
        public static string GetProcessorIdentifier()
        {
            if (!IsUnixOS)
            {
                return GetProcessorIdentifierWindows();
            }
#if FEATURE_CORECLR
            else if (IsMacOS)
            {
                return ProcessorNameAndIdentifierMacOS.Item2;
            }
#endif
            // Extend this once we start supporting Linux etc.
            throw new NotImplementedException("Getting CPU identifier is not supported on this platform!");
        }

        /// <summary>
        /// Gets the current physical memory size in MB
        /// </summary>
        public static FileSize GetPhysicalMemorySize()
        {
            if (!IsUnixOS)
            {
                return GetPhysicalMemorySizeWindows();
            }
#if FEATURE_CORECLR
            else if (IsMacOS)
            {
                return GetPhysicalMemorySizeMacOS();
            }
#endif
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
        public static FileSize GetPhysicalMemorySizeWindows()
        {
            MEMORYSTATUSEX memoryStatusEx = new MEMORYSTATUSEX();

            ulong bytes = GlobalMemoryStatusEx(memoryStatusEx)
                ? memoryStatusEx.ullTotalPhys
                : 0;
            return new FileSize(bytes);
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
            if (IsUnixOS)
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

            bool checkFor45PlusVersion(int releaseKey, out string result)
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

        #region macOS Helpers
#if FEATURE_CORECLR

        private static string GetOSVersionMacOS()
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
                                    Version version = Version.Parse(versionString);
                                    if (version.Major > 10 || (version.Major == 10 && version.Minor >= 13))
                                    {
                                        return string.Format("macOS {0}.{1}.{2}", version.Major, version.Minor, version.Build);
                                    }
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

            return String.Empty;
        }

        // This could potentially be replaced by a C wrapper querying the system information, the sysctl is just more convinient currently
        private static Tuple<string, string> GetProcessorNameAndIdentifierMacOS()
        {
            try
            {
                var process = new Process()
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
                var sysctlOutput = result.Split(Environment.NewLine);
                var cpuDescription =
                    sysctlOutput.Select(x => x.Split(":")).Where(y => y.Length == 2).ToDictionary(z => z[0], z => z[1].Trim());

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
                return Tuple.Create(String.Empty, String.Empty);
            }
#pragma warning restore ERP022
        }

        [DllImport("libc", SetLastError = true)]
        private static extern long sysconf(int name);
        private const int _SC_PAGESIZE_OSX = 29;
        private const int _SC_PHYS_PAGES_OSX = 200;

        private static FileSize GetPhysicalMemorySizeMacOS()
        {
            long physicalPages = sysconf(_SC_PHYS_PAGES_OSX);
            long pageSize = sysconf(_SC_PAGESIZE_OSX);

            return new FileSize(physicalPages * pageSize);
        }

#endif

        #endregion
    }
}
