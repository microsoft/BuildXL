// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Utilities.Core;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Evaluates <see cref="TestRequirements"/> flags and returns a skip reason when a requirement is not met.
    /// Shared by both xunit v2 and v3 FactIfSupportedAttribute implementations.
    /// </summary>
    public static class TestRequirementsChecker
    {
        private static readonly ConcurrentDictionary<TestRequirements, string> s_requirementsToFailureMessageMap
            = new ConcurrentDictionary<TestRequirements, string>();

        /// <summary>
        /// Evaluates the given <paramref name="requirements"/> flags and returns a skip reason
        /// if any requirement is not met, or <c>null</c> if all requirements are satisfied.
        /// </summary>
        /// <param name="requirements">The combined requirement flags to check.</param>
        /// <param name="additionalChecks">
        /// Optional additional checks (e.g., JournalScan) that need references not available in this assembly.
        /// Each entry maps a <see cref="TestRequirements"/> flag to a check function that returns a failure message or null.
        /// </param>
        public static string GetSkipReason(
            TestRequirements requirements,
            params (TestRequirements requirement, Func<string> check)[] additionalChecks)
        {
            string skip = null;

            CheckRequirement(requirements, TestRequirements.NotSupported, ref skip, () => "Test is marked not supported.");

            CheckRequirement(
                requirements,
                TestRequirements.Admin,
                ref skip,
                () =>
                {
                    if (OperatingSystemHelper.IsWindowsOS)
                    {
                        return !CurrentProcess.IsElevated ? "Test must be run elevated!" : null;
                    }
                    else
                    {
                        return !CurrentProcess.CanSudoNonInteractive() ? "Test must be able to do a non-interactive sudo!" : null;
                    }
                });

            CheckRequirement(
                requirements,
                TestRequirements.SymlinkPermission,
                ref skip,
                () =>
                {
                    string tempFile = FileUtilities.GetTempFileName();
                    string symlinkPath = FileUtilities.GetTempFileName();
                    FileUtilities.DeleteFile(symlinkPath);

                    var canCreateSymlink = FileUtilities.TryCreateSymbolicLink(symlinkPath, tempFile, true).Succeeded && FileUtilities.FileExistsNoFollow(symlinkPath);
                    FileUtilities.DeleteFile(symlinkPath);
                    FileUtilities.DeleteFile(tempFile);

                    return !canCreateSymlink ? "Test must be run with symbolic link creation privileged" : null;
                });

            // Run any additional checks provided by the caller (e.g., JournalScan)
            if (additionalChecks != null)
            {
                foreach (var (requirement, check) in additionalChecks)
                {
                    CheckRequirement(requirements, requirement, ref skip, check);
                }
            }

            CheckRequirement(
                requirements,
                TestRequirements.WindowsOs,
                ref skip,
                () => OperatingSystemHelper.IsUnixOS ? "Test must be run on the CLR on Windows based operating systems!" : null);

            CheckRequirement(
                requirements,
                TestRequirements.UnixBasedOs,
                ref skip,
                () => !OperatingSystemHelper.IsUnixOS ? "Test must be run on the CoreCLR on Unix based operating systems!" : null);

            CheckRequirement(
                requirements,
                TestRequirements.MacOs,
                ref skip,
                () => !OperatingSystemHelper.IsMacOS ? "Test must be run on macOS" : null);

            CheckRequirement(
                requirements,
                TestRequirements.WindowsProjFs,
                ref skip,
                () =>
                {
                    if (OperatingSystemHelper.IsUnixOS)
                    {
                        return "WindowsProjFs requires running on Windows operating system";
                    }

                    bool foundGvfsService = System.Diagnostics.Process.GetProcessesByName("GVFS.Service").Length != 0;
                    if (!foundGvfsService)
                    {
                        return "Could not find GVFS.Service. Is Windows Projected FileSystem enabled?";
                    }

                    return null;
                });

            CheckRequirement(
                requirements,
                TestRequirements.WindowsOrMacOs,
                ref skip,
                () => OperatingSystemHelper.IsLinuxOS ? "Test must be run on Windows or macOS" : null);

            CheckRequirement(
                requirements,
                TestRequirements.WindowsOrLinuxOs,
                ref skip,
                () => OperatingSystemHelper.IsMacOS ? "Test must be run on Windows or Linux OS" : null);

            CheckRequirement(
                requirements,
                TestRequirements.LinuxOs,
                ref skip,
                () => !OperatingSystemHelper.IsLinuxOS ? "Test must be run on Linux OS" : null);

            CheckRequirement(
                requirements,
                TestRequirements.EBPFEnabled,
                ref skip,
                () =>
                {
                    if (!OperatingSystemHelper.IsLinuxOS)
                    {
                        return "Test requires EBPF sandboxing, which is only available on Linux";
                    }

                    if (!BuildXLTestBase.IsUsingEBPFSandbox())
                    {
                        return "Test requires EBPF sandboxing to be enabled";
                    }

                    return null;
                });

            return skip;
        }

        private static void CheckRequirement(TestRequirements requirements, TestRequirements requirement, ref string skip, Func<string> check)
        {
            if (requirements.HasFlag(requirement))
            {
                if (!s_requirementsToFailureMessageMap.TryGetValue(requirement, out var failureMessage))
                {
                    failureMessage = check();
                    s_requirementsToFailureMessageMap[requirement] = failureMessage;
                }

                if (!string.IsNullOrEmpty(failureMessage))
                {
                    skip = failureMessage;
                }
            }
        }

        /// <summary>
        /// Helper to build a <see cref="TestRequirements"/> flags value from named boolean parameters.
        /// </summary>
        public static TestRequirements BuildRequirements(
            TestRequirements additionalRequirements = TestRequirements.None,
            bool requiresAdmin = false,
            bool requiresJournalScan = false,
            bool requiresSymlinkPermission = false,
            bool requiresWindowsBasedOperatingSystem = false,
            bool requiresUnixBasedOperatingSystem = false,
            bool requiresMacOperatingSystem = false,
            bool requiresWindowsOrMacOperatingSystem = false,
            bool requiresWindowsOrLinuxOperatingSystem = false,
            bool requiresLinuxBasedOperatingSystem = false,
            bool requiresEBPFEnabled = false)
        {
            var requirements = additionalRequirements;
            if (requiresAdmin) requirements |= TestRequirements.Admin;
            if (requiresJournalScan) requirements |= TestRequirements.JournalScan;
            if (requiresSymlinkPermission) requirements |= TestRequirements.SymlinkPermission;
            if (requiresWindowsBasedOperatingSystem) requirements |= TestRequirements.WindowsOs;
            if (requiresUnixBasedOperatingSystem) requirements |= TestRequirements.UnixBasedOs;
            if (requiresMacOperatingSystem) requirements |= TestRequirements.MacOs;
            if (requiresWindowsOrMacOperatingSystem) requirements |= TestRequirements.WindowsOrMacOs;
            if (requiresWindowsOrLinuxOperatingSystem) requirements |= TestRequirements.WindowsOrLinuxOs;
            if (requiresLinuxBasedOperatingSystem) requirements |= TestRequirements.LinuxOs;
            if (requiresEBPFEnabled) requirements |= TestRequirements.EBPFEnabled;
            return requirements;
        }
    }
}
