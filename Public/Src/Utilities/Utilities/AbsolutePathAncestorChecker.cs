// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Provides functionality to check whether a given <see cref="AbsolutePath"/> has an ancestor in a collection of paths
    /// </summary>
    /// <remarks>
    /// The ancestor check is done in O(length of the provided path)
    /// This class is not thread safe
    /// </remarks>
    public sealed class AbsolutePathAncestorChecker
    {
        private readonly HashSet<HierarchicalNameId> m_paths = new HashSet<HierarchicalNameId>();

        /// <nodoc/>
        public AbsolutePathAncestorChecker()
        {
        }

        /// <summary>
        /// Adds a path to the collection of paths known to this class
        /// </summary>
        public bool AddPath(AbsolutePath absolutePath)
        {
            Contract.Requires(absolutePath.IsValid);
            return m_paths.Add(absolutePath.Value);
        }

        /// <summary>
        /// Checks whether any of the paths added with <see cref="AddPath(AbsolutePath)"/> is an ancestor (or is equal) to the given path.
        /// </summary>
        public bool HasKnownAncestor(PathTable pathTable, AbsolutePath absolutePath)
        {
            Contract.Requires(absolutePath.IsValid);

            var currentPath = absolutePath;
            while(currentPath != AbsolutePath.Invalid)
            {
                if (m_paths.Contains(currentPath.Value))
                {
                    return true;
                }

                currentPath = currentPath.GetParent(pathTable);
            }

            return false;
        }

        /// <summary>
        /// Clears all the paths added with <see cref="AddPath(AbsolutePath)"/>
        /// </summary>
        public void Clear() => m_paths.Clear();
    }
}
