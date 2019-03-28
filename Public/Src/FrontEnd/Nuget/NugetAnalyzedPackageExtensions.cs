// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// Contains extension methods for <see cref="NugetAnalyzedPackage"/>.
    /// </summary>
    public static class NugetAnalyzedPackageExtensions
    {
        /// <summary>
        /// Returns true if a <paramref name="analyzedPackage"/> has at least one target framework.
        /// </summary>
        public static bool HasTargetFrameworks(this NugetAnalyzedPackage analyzedPackage)
        {
            return analyzedPackage.TargetFrameworkWithFallbacks.Count != 0;
        }

        /// <summary>
        /// Returns a list of target frameworks of a <paramref name="analyzedPackage"/> in sorted order.
        /// </summary>
        public static string[] GetTargetFrameworksInStableOrder(this NugetAnalyzedPackage analyzedPackage, PathTable pathTable)
        {
            Contract.Requires(HasTargetFrameworks(analyzedPackage));

            var targetFrameworks = new HashSet<string>();
            foreach (var kv in analyzedPackage.TargetFrameworkWithFallbacks)
            {
                targetFrameworks.Add(kv.Key.ToString(pathTable.StringTable));
                foreach (var fallback in kv.Value)
                {
                    targetFrameworks.Add(fallback.ToString(pathTable.StringTable));
                }
            }

            return targetFrameworks.OrderByDescending(f => f).ToArray();
        }
    }
}
