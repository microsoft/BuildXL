// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Custom theory attribute that allows dynamically skipping tests based on what operations are supported.
    /// </summary>
    /// <remarks>
    /// Any test using this is non-deterministic with respect to caching since pip fingerprints don't take this dynamic
    /// requirements-based skipping into account. Ideally, tests would run again if a dynamic condition is changed to meet the test requirement.
    /// </remarks>
    [SuppressMessage("Microsoft.Design", "CA1019:DefineAccessorsForAttributeArguments")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class TheoryIfSupportedAttribute : TheoryAttribute
    {
        /// <summary>
        /// The test requirements.
        /// </summary>
        public TestRequirements Requirements => m_inner.Requirements;

        private readonly FactIfSupportedAttribute m_inner;

        /// <nodoc/>
        public TheoryIfSupportedAttribute(TestRequirements requirements)
            : this(additionalRequirements: requirements)
        {
        }

        /// <nodoc/>
        public TheoryIfSupportedAttribute(
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
            // Use same logic and underlying static state to determine whether to Skip tests
            m_inner = new FactIfSupportedAttribute(
                requiresAdmin: requiresAdmin,
                requiresJournalScan: requiresJournalScan,
                requiresSymlinkPermission: requiresSymlinkPermission,
                requiresWindowsBasedOperatingSystem: requiresWindowsBasedOperatingSystem,
                requiresUnixBasedOperatingSystem: requiresUnixBasedOperatingSystem,
                requiresMacOperatingSystem: requiresMacOperatingSystem,
                requiresWindowsOrMacOperatingSystem: requiresWindowsOrMacOperatingSystem,
                requiresWindowsOrLinuxOperatingSystem: requiresWindowsOrLinuxOperatingSystem,
                requiresLinuxBasedOperatingSystem: requiresLinuxBasedOperatingSystem,
                requiresEBPFEnabled: requiresEBPFEnabled,
                additionalRequirements: additionalRequirements
            );

            Skip = m_inner.Skip;
        }
    }
}
