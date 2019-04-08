// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
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
            bool requiresAdmin = traitAttribute.GetNamedArgument<bool>(nameof(FactIfSupportedAttribute.RequiresAdmin));
            bool requiresJournalScan = traitAttribute.GetNamedArgument<bool>(nameof(FactIfSupportedAttribute.RequiresJournalScan));
            bool requiresSymlinkPermission = traitAttribute.GetNamedArgument<bool>(nameof(FactIfSupportedAttribute.RequiresSymlinkPermission));

            if (requiresAdmin || requiresJournalScan || requiresSymlinkPermission)
            {
                yield return new KeyValuePair<string, string>("Category", RequiresAdmin);
            }
        }
    }
}
