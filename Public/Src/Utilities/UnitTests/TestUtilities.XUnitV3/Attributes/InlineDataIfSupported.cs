// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Test.BuildXL.TestUtilities.XUnit
{
    /// <summary>
    /// Custom inline data attribute that allows dynamically skipping tests based on what operations are supported.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InlineDataIfSupported : DataAttribute
    {
        private readonly object[] m_data;

        /// <summary>
        /// The test requirements.
        /// </summary>
        public TestRequirements Requirements => m_inner.Requirements;

        private readonly FactIfSupportedAttribute m_inner;

        /// <nodoc/>
        public InlineDataIfSupported(
            bool requiresAdmin = false,
            bool requiresJournalScan = false,
            bool requiresSymlinkPermission = false,
            bool requiresWindowsBasedOperatingSystem = false,
            bool requiresUnixBasedOperatingSystem = false,
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
                requiresMacOperatingSystem: requiresMacOperatingSystem,
                requiresWindowsOrMacOperatingSystem: requiresWindowsOrMacOperatingSystem,
                requiresWindowsOrLinuxOperatingSystem: requiresWindowsOrLinuxOperatingSystem,
                additionalRequirements: additionalRequirements
            );

            Skip = m_inner.Skip;
        }

        /// <inheritdoc />
        public override ValueTask<IReadOnlyCollection<global::Xunit.ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
        {
            var rows = new global::Xunit.ITheoryDataRow[] { ConvertDataRow(m_data) };
            return new ValueTask<IReadOnlyCollection<global::Xunit.ITheoryDataRow>>(rows);
        }

        /// <inheritdoc />
        public override bool SupportsDiscoveryEnumeration() => true;
    }
}
