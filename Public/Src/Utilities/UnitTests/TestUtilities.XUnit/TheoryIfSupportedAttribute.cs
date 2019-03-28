// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Xunit;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Custom theory attribute that allows dynamically skipping tests based on what operations are supported
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
    public sealed class TheoryIfSupportedAttribute : TheoryAttribute, ITraitAttribute
    {
        /// <nodoc/>
        public bool RequiresAdmin { get; set; }

        /// <nodoc/>
        public bool RequiresJournalScan { get; set; }

        /// <nodoc/>
        public bool RequiresSymlinkPermission { get; set; }

        /// <nodoc/>
        public TheoryIfSupportedAttribute(
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
                requiresUnixBasedOperatingSystem: requiresUnixBasedOperatingSystem,
                requiresHeliumDriversAvailable: requiresHeliumDriversAvailable
            ).Skip;
        }
    }
}
