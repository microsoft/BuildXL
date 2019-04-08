// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Comparer that the <see cref="WorkspaceProvider"/> uses for determining when two
    /// module definitions are the same. This is used when computing the set of modules
    /// from all resolvers: each resolver returns a non-duplicated collection of modules by definition,
    /// but when put together, this comparer is used to respect the resolver order and not introduce duplicates.
    /// </summary>
    /// <remarks>
    /// The comparer relies on the module descriptor of each module definition, <see cref="ModuleDefinitionWorkspaceComparer"/>.
    /// </remarks>
    public sealed class ModuleDefinitionWorkspaceComparer : IEqualityComparer<ModuleDefinition>
    {
        private readonly IEqualityComparer<ModuleDescriptor> m_descriptorComparer;

        /// <summary>
        /// Comparer to use.
        /// </summary>
        public static IEqualityComparer<ModuleDefinition> Comparer { get; } = new ModuleDefinitionWorkspaceComparer();

        private ModuleDefinitionWorkspaceComparer()
        {
            m_descriptorComparer = ModuleDescriptorWorkspaceComparer.Comparer;
        }

        /// <inheritdoc/>
        public bool Equals(ModuleDefinition left, ModuleDefinition right)
        {
            if (left == null && right == null)
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return m_descriptorComparer.Equals(left.Descriptor, right.Descriptor);
        }

        /// <inheritdoc/>
        public int GetHashCode(ModuleDefinition moduleDefinition)
        {
            return m_descriptorComparer.GetHashCode(moduleDefinition.Descriptor);
        }
    }
}
