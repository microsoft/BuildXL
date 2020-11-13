// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Processes
{
    /// <summary>
    /// Resolves file accesses containing reparse points by normalizing them into corresponding reparse point free ones
    /// </summary>
    public sealed class ReparsePointResolver
    {
        private readonly PipExecutionContext m_context;
        private readonly DirectoryTranslator m_directoryTranslator;

        /// <summary>
        /// Paths (potentially containing reparse points) to resolved paths
        /// </summary>
        private readonly ConcurrentBigMap<AbsolutePath, AbsolutePath> m_resolvedPathCache = new ConcurrentBigMap<AbsolutePath, AbsolutePath>();
        
        /// <nodoc/>
        public ReparsePointResolver(PipExecutionContext context, [CanBeNull] DirectoryTranslator directoryTranslator)
        {
            Contract.RequiresNotNull(context);
            m_context = context;
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
            var parentPath = path.GetParent(m_context.PathTable);
            
            // If no parent, there is nothing to resolve
            if (!parentPath.IsValid)
            {
                return path;
            }

            PathAtom filename = path.GetName(m_context.PathTable);

            // Check the cache
            var cachedResult = m_resolvedPathCache.TryGet(parentPath);
            if (cachedResult.IsFound)
            {
                return cachedResult.Item.Value.Combine(m_context.PathTable, filename);
            }

            // The cache didn't have it, so let's resolve it
            if (!TryResolvePath(parentPath.ToString(m_context.PathTable), out ExpandedAbsolutePath resolvedExpandedPath))
            {
                // If we cannot get the final path (e.g. file not found causes this), then we assume the path
                // is already canonicalized

                // Observe we cannot update the cache since the path was not resolved.
                return path;
            }

            // Update the cache
            m_resolvedPathCache.TryAdd(parentPath, resolvedExpandedPath.Path);

            return resolvedExpandedPath.Path.Combine(m_context.PathTable, filename);
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
            var success = AbsolutePath.TryCreate(m_context.PathTable, finalPathAsString, out var finalPath);
            if (!success)
            {
                Contract.Assume(false, $"The result of GetFinalPathNameByPath should always be a path we can parse. Original path is '{path}', final path is '{finalPathAsString}'.");
            }

            expandedFinalPath = ExpandedAbsolutePath.CreateUnsafe(finalPath, finalPathAsString);
            return true;
        }
    }
}
