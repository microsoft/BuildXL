// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

#pragma warning disable IDE1006 // Naming rule violation

namespace BuildXL.Utilities
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
    }
}