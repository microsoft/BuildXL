// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities.Core;
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
    public sealed class FactIfSupportedAttribute : global::Xunit.FactAttribute, ITestIfSupportedTraitAttribute
    {
        /// <inheritdoc />
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
