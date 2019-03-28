// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Comparer that the <see cref="WorkspaceProvider"/> uses for determining when two
    /// module descriptors are the same.
    /// </summary>
    /// <remarks>
    /// The moduleId is assigned by each resolver, so it is always unique. So this comparer uses
    /// the module descriptor name and version.
    /// </remarks>
    public sealed class ModuleDescriptorWorkspaceComparer : IEqualityComparer<ModuleDescriptor>
    {
        /// <summary>
        /// Comparer to use.
        /// </summary>
        public static IEqualityComparer<ModuleDescriptor> Comparer { get; } = new ModuleDescriptorWorkspaceComparer();

        private ModuleDescriptorWorkspaceComparer()
        {
        }

        /// <inheritdoc/>
        public bool Equals(ModuleDescriptor left, ModuleDescriptor right)
        {
            return left.Name.Equals(right.Name) && left.Version.Equals(right.Version);
        }

        /// <inheritdoc/>
        public int GetHashCode(ModuleDescriptor moduleDescriptor)
        {
            return HashCodeHelper.Combine(moduleDescriptor.Name.GetHashCode(), moduleDescriptor.Version.GetHashCode());
        }
    }
}
