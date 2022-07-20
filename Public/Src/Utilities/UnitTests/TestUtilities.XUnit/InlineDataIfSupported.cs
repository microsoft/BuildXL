// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit
{
    /// <summary>
    /// Custom inline data attribute that allows dynamically skipping tests based on what operations are supported
    /// </summary>
    [TraitDiscoverer(AdminTestDiscoverer.ClassName, AdminTestDiscoverer.AssemblyName)]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InlineDataIfSupported : DataAttribute, ITraitAttribute
    {
        private readonly object[] m_data;
        private readonly ITestIfSupportedTraitAttribute m_inner;

        /// <inheritdoc />
        public TestRequirements Requirements => m_inner.Requirements;

        /// <nodoc/>
        public InlineDataIfSupported(
            bool requiresAdmin = false,
            bool requiresJournalScan = false,
            bool requiresSymlinkPermission = false,
            bool requiresWindowsBasedOperatingSystem = false,
            bool requiresUnixBasedOperatingSystem = false,
            bool requiresHeliumDriversAvailable = false,
            bool requiresMacOperatingSystem = false,
            bool requiresWindowsOrMacOperatingSystem = false,
            bool requiresWindowsOrLinuxOperatingSystem = false,
            TestRequirements additionalRequirements = TestRequirements.None,
            params object[] data)
        {
            m_data = data;

            m_inner = new FactIfSupportedAttribute(
                requiresAdmin: requiresAdmin,
                requiresJournalScan: requiresJournalScan,
                requiresSymlinkPermission: requiresSymlinkPermission,
                requiresWindowsBasedOperatingSystem: requiresWindowsBasedOperatingSystem,
                requiresUnixBasedOperatingSystem: requiresUnixBasedOperatingSystem,
                requiresHeliumDriversAvailable: requiresHeliumDriversAvailable,
                requiresMacOperatingSystem: requiresMacOperatingSystem,
                requiresWindowsOrMacOperatingSystem: requiresWindowsOrMacOperatingSystem,
                additionalRequirements: additionalRequirements,
                requiresWindowsOrLinuxOperatingSystem: requiresWindowsOrLinuxOperatingSystem
            );

            Skip = m_inner.Skip;
        }

        /// <nodoc/>
        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            return new object[1][] { m_data };
        }
    }
}
