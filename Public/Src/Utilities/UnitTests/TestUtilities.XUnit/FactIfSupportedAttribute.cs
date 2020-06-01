// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Custom fact attribute that allows dynamically skipping tests based on what operations are supported
    /// </summary>
    /// <remarks>
    /// Any test using this is non-deterministic with respect to caching since pip fingerprints don't take this dynamic
    /// requirements-based skipping into account. Ideally, tests would run again if a dynamic condition is changed to meet the test requirement.
    /// 
    /// For example: 
    /// 1. Test run #1 is run without admin rights, all tests with requiresAdmin are skipped, the test run's outputs are cached
    /// 2. User switches to an admin account and runs the tests again with no filesystem changes
    /// 3. Test run #2 is run with admin rights, tests with requiresAdmin should be run, but instead get a cache hit on #1's results and skips the tests again
    /// </remarks>
    [SuppressMessage("Microsoft.Design", "CA1019:DefineAccessorsForAttributeArguments")]
    [TraitDiscoverer(AdminTestDiscoverer.ClassName, AdminTestDiscoverer.AssemblyName)]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class FactIfSupportedAttribute : global::Xunit.FactAttribute, ITestIfSupportedTraitAttribute
    {
        /// <inheritdoc />
        public TestRequirements Requirements { get; }

        // Cache these at the test process invocation level
        private static readonly ConcurrentDictionary<TestRequirements, string> s_requirementsToFailureMessageMap
            = new ConcurrentDictionary<TestRequirements, string>();

        /// <nodoc/>
        public FactIfSupportedAttribute(TestRequirements requirements)
            : this(additionalRequirements: requirements)
        {
        }

        /// <nodoc/>
        public FactIfSupportedAttribute(
            bool requiresAdmin = false,
            bool requiresJournalScan = false,
            bool requiresSymlinkPermission = false,
            bool requiresWindowsBasedOperatingSystem = false,
            bool requiresUnixBasedOperatingSystem = false,
            bool requiresHeliumDriversAvailable = false,
            bool requiresHeliumDriversNotAvailable = false,
            bool requiresMacOperatingSystem = false,
            TestRequirements additionalRequirements = TestRequirements.None)
        {
            var requirements = additionalRequirements;
            AddRequirement(ref requirements, requiresAdmin, TestRequirements.Admin);
            AddRequirement(ref requirements, requiresJournalScan, TestRequirements.JournalScan);
            AddRequirement(ref requirements, requiresSymlinkPermission, TestRequirements.SymlinkPermission);
            AddRequirement(ref requirements, requiresWindowsBasedOperatingSystem, TestRequirements.WindowsOs);
            AddRequirement(ref requirements, requiresUnixBasedOperatingSystem, TestRequirements.UnixBasedOs);
            AddRequirement(ref requirements, requiresHeliumDriversAvailable, TestRequirements.HeliumDriversAvailable);
            AddRequirement(ref requirements, requiresHeliumDriversNotAvailable, TestRequirements.HeliumDriversNotAvailable);
            AddRequirement(ref requirements, requiresMacOperatingSystem, TestRequirements.MacOs);

            Requirements = requirements;

            if (Skip != null)
            {
                // If skip is specified, do nothing because the test will be skipped anyway.
                return;
            }

            CheckRequirement(
                TestRequirements.Admin,
                () =>
                {
                    return !CurrentProcess.IsElevated ? "Test must be run elevated!" : null;
                });

            CheckRequirement(
                TestRequirements.SymlinkPermission,
                () =>
                {
                    string tempFile = FileUtilities.GetTempFileName();
                    string symlinkPath = FileUtilities.GetTempFileName();
                    FileUtilities.DeleteFile(symlinkPath);

                    // For reliable tests, we ensure that the symlink is created.
                    var canCreateSymlink = FileUtilities.TryCreateSymbolicLink(symlinkPath, tempFile, true).Succeeded && FileUtilities.FileExistsNoFollow(symlinkPath);
                    FileUtilities.DeleteFile(symlinkPath);
                    FileUtilities.DeleteFile(tempFile);

                    return !canCreateSymlink ? "Test must be run with symbolic link creation privileged" : null;
                });

            CheckRequirement(
                TestRequirements.JournalScan,
                () =>
                {
                    if (OperatingSystemHelper.IsUnixOS)
                    {
                        return $"Test requires a journaled Windows file system, can't be executed on non-windows systems.";
                    }

                    var loggingContext = new LoggingContext("Dummy", "Dummy");
                    var map = JournalUtils.TryCreateMapOfAllLocalVolumes(loggingContext);
                    var accessor = JournalUtils.TryGetJournalAccessorForTest(map);
                    if (!accessor.Succeeded)
                    {
                        return accessor.Failure.Describe();
                    }

                    return null;
                });

            CheckRequirement(
                TestRequirements.WindowsOs,
                () =>
                {
                    if (OperatingSystemHelper.IsUnixOS)
                    {
                        return "Test must be run on the CLR on Windows based operating systems!";
                    }

                    return null;
                });

            CheckRequirement(
                TestRequirements.UnixBasedOs,
                () =>
                {
                    if (!OperatingSystemHelper.IsUnixOS)
                    {
                        return "Test must be run on the CoreCLR on Unix based operating systems!";
                    }

                    return null;
                });

            CheckRequirement(
                TestRequirements.MacOs,
                () =>
                {
                    if (!OperatingSystemHelper.IsMacOS)
                    {
                        return "Test must be run on macOS";
                    }

                    return null;
                });

            CheckRequirement(
                TestRequirements.HeliumDriversAvailable,
                () =>
                {
                    if (!ProcessUtilities.IsWciAndBindFiltersAvailable())
                    {
                        return "Test must be run elevated on a machine with WCI and Bind filters available.";
                    }

                    return null;
                });

            CheckRequirement(
                TestRequirements.HeliumDriversNotAvailable,
                () =>
                {
                    if (ProcessUtilities.IsWciAndBindFiltersAvailable())
                    {
                        return "Test must be run on a machine where WCI and Bind filters are NOT available.";
                    }

                    return null;
                });

            CheckRequirement(
                TestRequirements.WindowsProjFs,
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
        }

        private void CheckRequirement(TestRequirements requirement, Func<string> check)
        {
            if (Requirements.HasFlag(requirement))
            {
                if (!s_requirementsToFailureMessageMap.TryGetValue(requirement, out var failureMessage))
                {
                    failureMessage = check();
                    s_requirementsToFailureMessageMap[requirement] = failureMessage;
                }

                if (!string.IsNullOrEmpty(failureMessage))
                {
                    Skip = failureMessage;
                }
            }
        }

        private static void AddRequirement(ref TestRequirements requirements, bool condition, TestRequirements requirement)
        {
            if (condition)
            {
                requirements |= requirement;
            }
        }
    }
}
