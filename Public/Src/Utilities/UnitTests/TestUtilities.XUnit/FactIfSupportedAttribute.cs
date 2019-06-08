// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
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
    public sealed class FactIfSupportedAttribute : global::Xunit.FactAttribute, ITraitAttribute
    {
        // Cache these at the test process invocation level
        private static bool? s_isElevated;
        private static bool? s_canCreateSymlink;
        private static bool? s_canScanJournal;
        private static bool? s_isHeliumFiltersAvailable;
        private static string s_journalAccessorFailure;

        /// <nodoc/>
        public bool RequiresAdmin { get; set; }

        /// <nodoc/>
        public bool RequiresJournalScan { get; set; }

        /// <nodoc/>
        public bool RequiresSymlinkPermission { get; set; }

        /// <nodoc/>
        public FactIfSupportedAttribute(
            bool requiresAdmin = false, 
            bool requiresJournalScan = false, 
            bool requiresSymlinkPermission = false, 
            bool requiresWindowsBasedOperatingSystem = false, 
            bool requiresUnixBasedOperatingSystem = false, 
            bool requiresHeliumDriversAvailable = false,
            bool requiresHeliumDriversNotAvailable = false)
        {
            RequiresAdmin = requiresAdmin;
            RequiresJournalScan = requiresJournalScan;
            RequiresSymlinkPermission = requiresSymlinkPermission;

            if (Skip != null)
            {
                // If skip is specified, do nothing because the test will be skipped anyway.
                return;
            }

            if (requiresAdmin)
            {
                if (!s_isElevated.HasValue)
                {
                    s_isElevated = CurrentProcess.IsElevated;
                }

                if (!s_isElevated.Value)
                {
                    Skip = "Test must be run elevated!";
                    return;
                }
            }

            if (requiresSymlinkPermission)
            {
                if (!s_canCreateSymlink.HasValue)
                {
                    string tempFile = Path.GetTempFileName();
                    string symlinkPath = Path.GetTempFileName();
                    FileUtilities.DeleteFile(symlinkPath);

                    // For reliable tests, we ensure that the symlink is created.
                    s_canCreateSymlink = FileUtilities.TryCreateSymbolicLink(symlinkPath, tempFile, true).Succeeded && FileUtilities.FileExistsNoFollow(symlinkPath);
                    FileUtilities.DeleteFile(symlinkPath);
                    FileUtilities.DeleteFile(tempFile);
                }

                if (!s_canCreateSymlink.Value)
                {
                    Skip = "Test must be run with symbolic link creation privileged";
                    return;
                }
            }

            if (requiresJournalScan)
            {
                if (!s_canScanJournal.HasValue)
                {
                    var loggingContext = new LoggingContext("Dummy", "Dummy");
                    var map = JournalUtils.TryCreateMapOfAllLocalVolumes(loggingContext);
                    var accessor = JournalUtils.TryGetJournalAccessorForTest(map);
                    s_canScanJournal = accessor.Succeeded;
                    if (!accessor.Succeeded)
                    {
                        s_journalAccessorFailure = accessor.Failure.Describe();
                    }
                }

                if (!s_canScanJournal.Value)
                {
                    Skip = $"Test requires access to the in process change journal scan but getting the journal access failed. {s_journalAccessorFailure ?? string.Empty}{Environment.NewLine}Either run elevated or install RS2.";
                    return; 
                }
            }

            if (requiresWindowsBasedOperatingSystem)
            {
                if (OperatingSystemHelper.IsUnixOS)
                {
                    Skip = "Test must be run on the CLR on Windows based operating systems!";
                    return;
                }
            }

            if (requiresUnixBasedOperatingSystem)
            {
                if (!OperatingSystemHelper.IsUnixOS)
                {
                    Skip = "Test must be run on the CoreCLR on Unix based operating systems!";
                    return;
                }
            }

            if (requiresHeliumDriversAvailable)
            {
                if (!s_isHeliumFiltersAvailable.HasValue)
                {
                    s_isHeliumFiltersAvailable = ProcessUtilities.IsWciAndBindFiltersAvailable();
                }

                if (!s_isHeliumFiltersAvailable.Value)
                {
                    Skip = "Test must be run elevated on a machine with WCI and Bind filters available.";
                    return;
                }
            }

            if (requiresHeliumDriversNotAvailable)
            {
                if (!s_isHeliumFiltersAvailable.HasValue)
                {
                    s_isHeliumFiltersAvailable = ProcessUtilities.IsWciAndBindFiltersAvailable();
                }
                if (s_isHeliumFiltersAvailable.Value)
                {
                    Skip = "Test must be run on a machine where WCI and Bind filters are NOT available.";
                    return;
                }
            }
        }
    }
}
