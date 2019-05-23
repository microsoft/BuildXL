// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL
{
    /// <summary>
    /// Information about the machine running the build
    /// </summary>
    public sealed class MachineInfo
    {
        /// <summary>
        /// Processors on the machine
        /// </summary>
        public int ProcessorCount { get; private set; }

        /// <summary>
        /// Value of PROCESSOR_IDENTIFIER environment variable
        /// </summary>
        public string ProcessorIdentifier { get; private set; }

        /// <summary>
        /// ProcessorNameString for processor 0 from the registry
        /// </summary>
        public string ProcessorName { get; private set; }

        /// <summary>
        /// OS version of the machine
        /// </summary>
        public string OsVersion { get; private set; }

        /// <summary>
        /// CLR environment version currently being used
        /// </summary>
        public string EnvironmentVersion { get; private set; }

        /// <summary>
        /// Installed memory in MB
        /// </summary>
        public int InstalledMemoryMB { get; private set; }

        /// <summary>
        /// Whether the Current Directory's drive has a seek penalty.
        /// </summary>
        /// <remarks>
        /// This stat is an approximate proxy.
        /// Clearly drive configuration is a lot more complicated than just the drive corresponding to the current
        /// directory when the build is invoked. It can still be unclear even after capturing all drives since it
        /// requires connecting up which drives are involved in a build.
        /// </remarks>
        public bool CurrentDriveHasSeekPenalty { get; private set; }

        /// <summary>
        /// Returns a currently installed .NET Framework version.
        /// </summary>
        public string DotNetFrameworkVersion { get; private set; }

        /// <summary>
        /// Creates a MachineInfo describing the current machine
        /// </summary>
        public static MachineInfo CreateForCurrentMachine()
        {
            MachineInfo mi = new MachineInfo();
            mi.ProcessorCount = Environment.ProcessorCount;
            mi.OsVersion = OperatingSystemHelper.GetOSVersion();
            mi.ProcessorName = OperatingSystemHelper.GetProcessorName();
            mi.ProcessorIdentifier = OperatingSystemHelper.GetProcessorIdentifier();

            try {
                mi.EnvironmentVersion = Environment.Version.ToString(4);
            }
            catch (ArgumentException)
            {
                // Fallback for .NETCore3.0, which currently reports "3.0.0" only
                mi.EnvironmentVersion = Environment.Version.ToString();
            }

            mi.InstalledMemoryMB = OperatingSystemHelper.GetPhysicalMemorySize().MB;

            char currentDrive = Environment.CurrentDirectory[0];
            bool ?seekPenalty = (char.IsLetter(currentDrive) && currentDrive > 64 && currentDrive < 123)
                ? FileUtilities.DoesLogicalDriveHaveSeekPenalty(currentDrive)
                : false;
            mi.CurrentDriveHasSeekPenalty = seekPenalty ?? false;
            mi.DotNetFrameworkVersion = OperatingSystemHelper.GetInstalledDotNetFrameworkVersion();
            return mi;
        }
    }
}
