// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// Resolves file accesses containing reparse points by normalizing them into corresponding reparse point free ones
    /// </summary>
    public sealed class ReparsePointResolver
    {
        private readonly PathTable m_pathTable;
        private readonly DirectoryTranslator m_directoryTranslator;

        /// <summary>
        /// Paths (potentially containing reparse points) to resolved paths
        /// </summary>
        private readonly ConcurrentBigMap<AbsolutePath, AbsolutePath> m_resolvedPathCache = new ConcurrentBigMap<AbsolutePath, AbsolutePath>();

        /// <summary>
        /// Paths to all reparse its points involved in the process of fully resolving it
        /// </summary>
        private readonly ConcurrentBigMap<AbsolutePath, IReadOnlyList<AbsolutePath>> m_reparsePointChainsCache = new ConcurrentBigMap<AbsolutePath, IReadOnlyList<AbsolutePath>>();

        /// <nodoc/>
        public ReparsePointResolver(PathTable pathTable, [MaybeNull] DirectoryTranslator directoryTranslator)
        {
            Contract.RequiresNotNull(pathTable);
            m_pathTable = pathTable;
            m_directoryTranslator = directoryTranslator;
        }

        /// <summary>
        /// Return a path where all intermediate reparse point directories are resolved to their final destinations.
        /// </summary>
        /// <remarks>
        /// The final segment of the path is never resolved
        /// </remarks>
        public AbsolutePath ResolveIntermediateDirectoryReparsePoints(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            // This function only takes care of intermediate directories, so let's fully resolve the parent path
            var parentPath = path.GetParent(m_pathTable);

            // If no parent, there is nothing to resolve
            if (!parentPath.IsValid)
            {
                return path;
            }

            PathAtom filename = path.GetName(m_pathTable);

            // Check the cache
            var cachedResult = m_resolvedPathCache.TryGet(parentPath);
            if (cachedResult.IsFound)
            {
                return cachedResult.Item.Value.Combine(m_pathTable, filename);
            }

            // The cache didn't have it, so let's resolve it
            if (!TryResolvePath(parentPath.ToString(m_pathTable), out ExpandedAbsolutePath resolvedExpandedPath))
            {
                // If we cannot get the final path (e.g. file not found causes this), then we assume the path
                // is already canonicalized

                // Observe we cannot update the cache since the path was not resolved.
                return path;
            }

            // Update the cache
            m_resolvedPathCache.TryAdd(parentPath, resolvedExpandedPath.Path);

            return resolvedExpandedPath.Path.Combine(m_pathTable, filename);
        }

        /// <summary>
        /// Returns all reparse points contained in the given path.
        /// </summary>
        /// <remarks>
        /// The result represents all the read operations on reparse points that the OS would perform if
        /// the given path was asked to be fully resolved. E.g. given the path /a/b/c/d where
        /// 
        /// b -> reparse-b
        /// reparse-b -> reparse-b1
        /// d -> reparse-d
        /// 
        /// This function returns /a/reparse-b, /a/reparse-b1, /a/reparse-b1/c/reparse-d
        /// The result of this call is cached, so please consider scenarios where build tools could modify
        /// the actual result.
        /// </remarks>
        public IEnumerable<AbsolutePath> GetAllReparsePointsInChains(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            // Check the cache and return if found
            if (m_reparsePointChainsCache.TryGet(path) is var cacheResult && cacheResult.IsFound)
            {
                return cacheResult.Item.Value;
            }

            // Put all components of the path in a list so we can traverse it from root to leaf
            using var pathPrefixesWrapper = Pools.StringListPool.GetInstance();
            var pathFragments = pathPrefixesWrapper.Instance;
            var root = path.GetRoot(m_pathTable);

            while (path.IsValid)
            {
                // On Linux the root path does not have a name, so treat the root differently
                pathFragments.Add(path == root ? path.ToString(m_pathTable) : path.GetName(m_pathTable).ToString(m_pathTable.StringTable));
                path = path.GetParent(m_pathTable);
            }
            
            using var intermediateReparsePointsWrapper = Pools.StringListPool.GetInstance();
            var intermediateReparsePoints = intermediateReparsePointsWrapper.Instance;
            string currentPath = string.Empty;
            for (int i = pathFragments.Count - 1; i >= 0; i--)
            {
                currentPath = Path.Combine(currentPath, pathFragments[i]);

                // Retrieve the chain of reparse points for this prefix
                if (FileUtilities.GetChainOfReparsePoints(currentPath, intermediateReparsePoints, includeOnlyReparsePoints: true) is var resolvedPath && resolvedPath != null)
                {
                    // Continue from the last resolved chain
                    currentPath = resolvedPath;
                }
            }

            var result = intermediateReparsePoints.Select(path => AbsolutePath.Create(m_pathTable, m_directoryTranslator?.Translate(path) ?? path)).ToList();
            m_reparsePointChainsCache.TryAdd(path, result);

            return result;
        }

        private bool TryResolvePath(string path, out ExpandedAbsolutePath expandedFinalPath)
        {
            if (!FileUtilities.TryGetFinalPathNameByPath(path, out string finalPathAsString, out _, volumeGuidPath: false))
            {
                // If the final path cannot be resolved (most common cause is file not found), we stay with the original path
                expandedFinalPath = default;
                return false;
            }

            if (m_directoryTranslator != null)
            {
                finalPathAsString = m_directoryTranslator.Translate(finalPathAsString);
            }

            // We want to compare the final path with the parsed path, so let's go through the path table
            // to fully canonicalize it
            var success = AbsolutePath.TryCreate(m_pathTable, finalPathAsString, out var finalPath);
            if (!success)
            {
                Contract.Assume(false, $"The result of GetFinalPathNameByPath should always be a path we can parse. Original path is '{path}', final path is '{finalPathAsString}'.");
            }

            expandedFinalPath = ExpandedAbsolutePath.CreateUnsafe(finalPath, finalPathAsString);
            return true;
        }
    }
}