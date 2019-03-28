// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Custom test class attribute that allows dynamically skipping tests based on what operations are supported.
    /// Attributes (and Xunit tests cases) are inherited by child test classes, but the child class can optionally
    /// override the parent's <see cref="TestClassIfSupportedAttribute"/> by specifying a different one.
    /// 
    /// Note that the child and parent attributes do NOT stack, so the child will have to re-require all parent requirements
    /// in the overridden attribute in addition to the new child requirements.
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
    [TraitDiscoverer(AdminTestDiscoverer.ClassName, AdminTestDiscoverer.AssemblyName)]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class TestClassIfSupportedAttribute : Attribute, ITraitAttribute
    {
        /// <summary>
        /// Whether the entire test class should be skipped or not.
        /// </summary>
        public readonly string Skip = null;

        /// <nodoc/>
        public bool RequiresAdmin { get; set; }

        /// <nodoc/>
        public bool RequiresJournalScan { get; set; }

        /// <nodoc/>
        public bool RequiresSymlinkPermission { get; set; }

        /// <nodoc/>
        public TestClassIfSupportedAttribute(
            bool requiresAdmin = false,
            bool requiresJournalScan = false,
            bool requiresSymlinkPermission = false,
            bool requiresWindowsBasedOperatingSystem = false,
            bool requiresUnixBasedOperatingSystem = false,
            bool requiresHeliumDriversAvailable = false)
        {
            RequiresAdmin = requiresAdmin;
            RequiresJournalScan = requiresJournalScan;
            RequiresSymlinkPermission = requiresSymlinkPermission;

            // Use same logic and underlying static state to determine wheter to Skip tests
            Skip = new FactIfSupportedAttribute(
                requiresAdmin: requiresAdmin,
                requiresJournalScan: requiresJournalScan,
                requiresSymlinkPermission: requiresSymlinkPermission,
                requiresWindowsBasedOperatingSystem: requiresWindowsBasedOperatingSystem,
                requiresUnixBasedOperatingSystem: requiresUnixBasedOperatingSystem

            ).Skip;
        }
    }
}
