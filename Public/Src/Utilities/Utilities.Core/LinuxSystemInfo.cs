// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

#pragma warning disable IDE1006 // Naming rule violation

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Helper for accessing system information for Linux.
    /// </summary>
    internal static class LinuxSystemInfo
    {
        /// <summary>
        /// Return when a particular system information is not available.
        /// </summary>
        private const string InfoNotAvailable = "'NOT AVAILABLE'";

        private const string RegexValueGroupName = "value";

        /// <summary>
        /// Cached OS version value.
        /// </summary>
        private static string m_osVersion;

        /// <summary>
        /// Cached processor name value.
        /// </summary>
        private static string m_processorName;

        /// <summary>
        /// Cached processor identifier value.
        /// </summary>
        private static string m_processorIdentifier;

        /// <nodoc />
        public static string InvalidVersionIdExceptionMessage = "Failed to parse the Linux distro Version_Id";

        /// <nodoc />
        public static string InvalidDistroInfoExceptionMessage = "Failed to obtain Linux distribution name and version ID due to a parsing error";

        public static string GetOSVersion()
        {
            if (m_osVersion != null)
            {
                // Return cached value
                return m_osVersion;
            }

            m_osVersion = File.ReadLines("/proc/version").FirstOrDefault() ?? InfoNotAvailable;
            return m_osVersion;
        }

        public static string GetProcessorName()
        {
            if (m_processorName != null)
            {
                // Return cached value
                return m_processorName;
            }

            m_processorName =
                FirstMatchOrDefault(
                    File.ReadLines("/proc/cpuinfo"),
                    new Regex(@$"^model name\s+:(?<{RegexValueGroupName}>.*)$", RegexOptions.Compiled, TimeSpan.FromMinutes(1)))
                ?? InfoNotAvailable;
            return m_processorName;
        }

        public static string GetProcessorIdentifier()
        {
            if (m_processorIdentifier != null)
            {
                // Return cached value
                return m_processorIdentifier;
            }

            var cpu_family_regex = new Regex(@$"^cpu family\s+:(?<{RegexValueGroupName}>.*)$", RegexOptions.Compiled, TimeSpan.FromMinutes(1));
            var model_regex = new Regex(@$"^model\s+:(?<{RegexValueGroupName}>.*)$", RegexOptions.Compiled, TimeSpan.FromMinutes(1));
            var stepping_regex = new Regex(@$"^stepping\s+:(?<{RegexValueGroupName}>.*)$", RegexOptions.Compiled, TimeSpan.FromMinutes(1));
            var vendor_id_regex = new Regex(@$"^vendor_id\s+:(?<{RegexValueGroupName}>.*)$", RegexOptions.Compiled, TimeSpan.FromMinutes(1));

            string cpu_family = null;
            string model = null;
            string stepping = null;
            string vendor_id = null;
            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                FirstMatchOrDefault(ref cpu_family, line, cpu_family_regex);
                FirstMatchOrDefault(ref model, line, model_regex);
                FirstMatchOrDefault(ref stepping, line, stepping_regex);
                FirstMatchOrDefault(ref vendor_id, line, vendor_id_regex);
            }

            m_processorIdentifier = $"Family {cpu_family} Model {model} Stepping {stepping}, {vendor_id}";
            return m_processorIdentifier;
        }

        private static string FirstMatchOrDefault(IEnumerable<string> lines, Regex regex)
        {
            foreach (string line in lines)
            {
                Match match = regex.Matches(line).Cast<Match>().FirstOrDefault();
                if (match != null)
                {
                    return match.Groups["value"].Value.Trim();
                }
            }

            return null;
        }

        private static void FirstMatchOrDefault(ref string value, string line, Regex regex)
        {
            if (value != null)
            {
                return;
            }

            Match match = regex.Matches(line).Cast<Match>().FirstOrDefault();
            value = match != null ? match.Groups["value"].Value.Trim() : null;
        }

        /// <summary>
        /// Obtains the linux distro information of the machine.
        /// </summary>
        /// <remarks>
        /// /etc/os-release file provides the information about the underlying OS.
        /// Sample file contents
        /// VERSION = "20.04.2 LTS (Focal Fossa)"
        /// ID = ubuntu
        /// ID_LIKE = debian
        /// PRETTY_NAME = "Ubuntu 20.04.2 LTS"
        /// VERSION_ID = "20.04"
        /// VERSION_CODENAME = focal
        /// UBUNTU_CODENAME = focal
        /// We make use of the Version_Id and Id to obtain the required information.
        /// </remarks>
        public static LinuxDistribution GetLinuxDistroInfo()
        {
            return ParseLinuxDistroInfo(File.ReadLines("/etc/os-release"));
        }
        
        /// <summary>
        /// Parse linux distro information to obtain the distro name and the version id.
        /// </summary>
        internal static LinuxDistribution ParseLinuxDistroInfo(IEnumerable<string> content)
        {
            string distroName = null;
            Version distroVersionId = null;

            foreach (string line in content)
            {
                var keyValuePair = line.Split('=');

                if (keyValuePair.Length == 2)
                {
                    string key = keyValuePair[0].Trim();
                    // Whitespace is trimmed to handle cases where it may surround the value unexpectedly.
                    // Double quotes are trimmed because the os-release file format may enclose values in quotes.
                    string value = keyValuePair[1].Trim(new[] { ' ', '"' });

                    // Capture the Version_Id and Id from this file.
                    if (key == "ID")
                    {
                        distroName = value.ToLower();
                    }
                    else if (key == "VERSION_ID")
                    {
                        value = value.Replace("\"", "");

                        // In some cases, the Linux distribution `VERSION_ID` might be a single integer, such as "11" or "17".
                        // These single-integer versions (often found in Debian-based systems) do not include a minor component,
                        // which causes `Version.ParseVersion` within `TryParse` to fail since it expects a "major.minor" format (e.g., "11.0").
                        // To handle this, we append ".0" to the `VERSION_ID` (e.g., converting "11" to "11.0") to allow `Version.TryParse` 
                        // to create a valid `Version` object, ensuring compatibility with systems that use single-integer versioning.
                        if (!value.Contains("."))
                        {
                            value += ".0";
                        }

                        if (!Version.TryParse(value, out distroVersionId))
                        {
                            throw new ArgumentException($"{InvalidVersionIdExceptionMessage} '{value}'");
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(distroName) || distroVersionId == null)
            {
                throw new BuildXLException(InvalidDistroInfoExceptionMessage);
            }

            return new LinuxDistribution(distroName, distroVersionId);
        }

        /// <summary>
        /// Parses the Linux Kernel version.
        /// </summary>
        /// <returns>Kernel version, major revision, and minor revision</returns>
        public static (int kernelVersion, int majorRevision, int minorRevision) GetLinuxKernelVersion()
        {
            // Parse kernel version by reading /proc/version
            // Example output: Linux version 5.15.146.1-microsoft-standard-WSL2 (root@65c757a075e2) (gcc (GCC) 11.2.0, GNU ld (GNU Binutils) 2.37) #1 SMP Thu Jan 11 04:09:03 UTC 2024
            // We are interested in the major and minor parts of the kernel version.
            var procVersion = GetOSVersion();
            var kernelVersionPattern = @"Linux\sversion\s[0-9]+\.[0-9]+\.[0-9]+";
            var match = Regex.Match(procVersion, kernelVersionPattern, RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var kernelVersion = match.Value.Replace("Linux version ", "");
                var kernelVersionParts = kernelVersion.Split('.');

                return (int.Parse(kernelVersionParts[0]), int.Parse(kernelVersionParts[1]), int.Parse(kernelVersionParts[2]));
            }

            return (0, 0, 0);
        }
    }

}