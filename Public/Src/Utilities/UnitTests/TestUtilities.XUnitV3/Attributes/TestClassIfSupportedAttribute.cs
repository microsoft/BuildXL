// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Custom test class attribute that allows dynamically skipping tests based on what operations are supported.
    /// Attributes (and xunit test cases) are inherited by child test classes, but the child class can optionally
    /// override the parent's <see cref="TestClassIfSupportedAttribute"/> by specifying a different one.
    /// 
    /// Note that the child and parent attributes do NOT stack, so the child will have to re-require all parent requirements
    /// in the overridden attribute in addition to the new child requirements.
    /// </summary>
    /// <remarks>
    /// In v3, <see cref="BuildXLTestBehaviorAttribute"/> checks for this attribute at runtime
    /// and dynamically skips the test if the requirements are not met.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class TestClassIfSupportedAttribute : Attribute
    {
        /// <summary>
        /// The skip reason, or null if the test class is supported.
        /// </summary>
        public string Skip { get; }

        /// <summary>
        /// The test requirements.
        /// </summary>
        public TestRequirements Requirements => m_inner.Requirements;

        private readonly FactIfSupportedAttribute m_inner;

        /// <nodoc/>
        public TestClassIfSupportedAttribute(TestRequirements requirements)
            : this(additionalRequirements: requirements)
        {
        }

        /// <nodoc/>
        public TestClassIfSupportedAttribute(
            bool requiresAdmin = false,
            bool requiresJournalScan = false,
            bool requiresSymlinkPermission = false,
            bool requiresWindowsBasedOperatingSystem = false,
            bool requiresUnixBasedOperatingSystem = false,
            bool requiresMacOperatingSystem = false,
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
                requiresWindowsOrLinuxOperatingSystem: requiresWindowsOrLinuxOperatingSystem,
                requiresLinuxBasedOperatingSystem: requiresLinuxBasedOperatingSystem,
                requiresEBPFEnabled: requiresEBPFEnabled,
                additionalRequirements: additionalRequirements
            );

            Skip = m_inner.Skip;
        }
    }
}
