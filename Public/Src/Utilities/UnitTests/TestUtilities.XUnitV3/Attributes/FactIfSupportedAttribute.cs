// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Custom fact attribute that allows dynamically skipping tests based on what operations are supported.
    /// </summary>
    /// <remarks>
    /// Any test using this is non-deterministic with respect to caching since pip fingerprints don't take this dynamic
    /// requirements-based skipping into account. Ideally, tests would run again if a dynamic condition is changed to meet the test requirement.
    /// </remarks>
    [SuppressMessage("Microsoft.Design", "CA1019:DefineAccessorsForAttributeArguments")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class FactIfSupportedAttribute : global::Xunit.FactAttribute
    {
        /// <summary>
        /// The test requirements.
        /// </summary>
        public TestRequirements Requirements { get; }

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
            bool requiresMacOperatingSystem = false,
            bool requiresWindowsOrMacOperatingSystem = false,
            bool requiresWindowsOrLinuxOperatingSystem = false,
            bool requiresLinuxBasedOperatingSystem = false,
            bool requiresEBPFEnabled = false,
            TestRequirements additionalRequirements = TestRequirements.None)
        {
            Requirements = TestRequirementsChecker.BuildRequirements(
                additionalRequirements,
                requiresAdmin,
                requiresJournalScan,
                requiresSymlinkPermission,
                requiresWindowsBasedOperatingSystem,
                requiresUnixBasedOperatingSystem,
                requiresMacOperatingSystem,
                requiresWindowsOrMacOperatingSystem,
                requiresWindowsOrLinuxOperatingSystem,
                requiresLinuxBasedOperatingSystem,
                requiresEBPFEnabled);

            if (Skip != null)
            {
                return;
            }

            Skip = TestRequirementsChecker.GetSkipReason(
                Requirements,
                (TestRequirements.JournalScan, JournalScanCheck));
        }

        private static string JournalScanCheck()
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                return "Test requires a journaled Windows file system, can't be executed on non-windows systems.";
            }

            var loggingContext = new LoggingContext("Dummy", "Dummy");
            var map = JournalUtils.TryCreateMapOfAllLocalVolumes(loggingContext);
            var accessor = JournalUtils.TryGetJournalAccessorForTest(map);
            if (!accessor.Succeeded)
            {
                return accessor.Failure.Describe();
            }

            return null;
        }
    }
}
