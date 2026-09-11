// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Provides functionality to check whether a given <see cref="AbsolutePath"/> has an ancestor
    /// (the path itself or any path reached by repeatedly taking its parent) in a collection of paths
    /// </summary>
    /// <remarks>
    /// Known paths are stored directly. Ancestor queries walk toward the root until they encounter either a known path
    /// or a parent whose result was memoized by an earlier query. Only traversed parents are memoized because queried
    /// leaf paths are typically unique and retaining them would provide little reuse.
    ///
    /// Positive memoized results remain valid as known paths are added because the set of known ancestors only grows.
    /// Negative memoized results are cleared whenever a new known path is added because that path may invalidate an
    /// earlier negative result.
    ///
    /// The retained-entry capacity lets an owning pool discard unusually large instances rather than retaining their
    /// hash-set storage after <see cref="Clear"/>. This class is not thread safe.
    /// </remarks>
    public sealed class AbsolutePathAncestorChecker
    {
        private readonly HashSet<HierarchicalNameId> m_paths = new HashSet<HierarchicalNameId>();
        private readonly HashSet<HierarchicalNameId> m_pathsWithKnownAncestor = new HashSet<HierarchicalNameId>();
        private readonly HashSet<HierarchicalNameId> m_pathsWithoutKnownAncestor = new HashSet<HierarchicalNameId>();

        /// <summary>
        /// Number of entries allocated in the path and memoization sets' backing storage.
        /// </summary>
        internal int RetainedEntryCapacity =>
            Pools.GetSetCapacity(m_paths) +
            Pools.GetSetCapacity(m_pathsWithKnownAncestor) +
            Pools.GetSetCapacity(m_pathsWithoutKnownAncestor);

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

            var added = m_paths.Add(absolutePath.Value);
            if (added)
            {
                m_pathsWithoutKnownAncestor.Clear();
            }

            return added;
        }

        /// <summary>
        /// Checks whether any of the paths added with <see cref="AddPath(AbsolutePath)"/> is an ancestor (or is equal) to the given path.
        /// </summary>
        public bool HasKnownAncestor(PathTable pathTable, AbsolutePath absolutePath)
        {
            Contract.Requires(absolutePath.IsValid);
            if (m_paths.Count == 0)
            {
                return false;
            }

            var queriedPath = absolutePath.Value;
            var currentPath = queriedPath;
            while (currentPath.IsValid)
            {
                if (m_paths.Contains(currentPath) || m_pathsWithKnownAncestor.Contains(currentPath))
                {
                    CacheTraversedParents(pathTable, queriedPath, currentPath, m_pathsWithKnownAncestor);
                    return true;
                }

                if (m_pathsWithoutKnownAncestor.Contains(currentPath))
                {
                    CacheTraversedParents(pathTable, queriedPath, currentPath, m_pathsWithoutKnownAncestor);
                    return false;
                }

                currentPath = pathTable.GetContainer(currentPath);
            }

            CacheTraversedParents(pathTable, queriedPath, HierarchicalNameId.Invalid, m_pathsWithoutKnownAncestor);
            return false;
        }

        /// <summary>
        /// Clears all the paths added with <see cref="AddPath(AbsolutePath)"/>
        /// </summary>
        public void Clear()
        {
            m_paths.Clear();
            m_pathsWithKnownAncestor.Clear();
            m_pathsWithoutKnownAncestor.Clear();
        }

        private void CacheTraversedParents(
            PathTable pathTable,
            HierarchicalNameId queriedPath,
            HierarchicalNameId knownResultPath,
            HashSet<HierarchicalNameId> cache)
        {
            if (queriedPath == knownResultPath)
            {
                return;
            }

            for (var currentPath = pathTable.GetContainer(queriedPath); currentPath.IsValid; currentPath = pathTable.GetContainer(currentPath))
            {
                cache.Add(currentPath);
                if (currentPath == knownResultPath)
                {
                    break;
                }
            }
        }
    }
}
