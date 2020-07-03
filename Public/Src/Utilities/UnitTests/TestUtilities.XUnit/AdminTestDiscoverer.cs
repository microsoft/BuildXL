// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Trait discoverer for discovering tests that require admin privilege to be really tested.
    /// </summary>
    public class AdminTestDiscoverer : ITraitDiscoverer
    {
        /// <summary>
        /// Assembly name.
        /// </summary>
        public const string AssemblyName = DiscovererUtils.AssemblyName;

        /// <summary>
        /// Class name.
        /// </summary>
        public const string ClassName = AssemblyName + "." + nameof(AdminTestDiscoverer);

        private const string RequiresAdmin = "RequiresAdmin";

        /// <inheritdoc />
        public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
        {
            var requirements = traitAttribute.GetNamedArgument<TestRequirements>(nameof(ITestIfSupportedTraitAttribute.Requirements));

            if (requirements.HasFlag(TestRequirements.Admin) 
                || requirements.HasFlag(TestRequirements.JournalScan)
                || requirements.HasFlag(TestRequirements.SymlinkPermission))
            {
                yield return new KeyValuePair<string, string>("Category", RequiresAdmin);
            }
        }
    }
}
