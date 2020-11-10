// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
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
        
        /// <summary>
        /// Paths to whether their last fragment is a reparse point
        /// </summary>
        /// <remarks>
        /// Observe there is a symklink cache already in the sandbox process pip executor, but that one is only used
        /// for the mac case (whereas this class is only used for Windows), and it is local to the current pip.
        /// </remarks>
        private readonly ConcurrentBigMap<AbsolutePath, bool> m_reparsePointCache = new ConcurrentBigMap<AbsolutePath, bool>();

        /// <nodoc/>
        public ReparsePointResolver(PipExecutionContext context, [CanBeNull] DirectoryTranslator directoryTranslator)
        {
            Contract.RequiresNotNull(context);
            m_context = context;
            m_directoryTranslator = directoryTranslator;
        }

        /// <summary>
        /// Given an access whose path may contain intermediate reparse points, returns an equivalent resolved access where all the reparse points
        /// are resolved.
        /// </summary>
        /// <remarks>
        /// If the reparse point is the final fragment of the path, this function does not consider it to need resolution.
        /// <paramref name="accessPath"/> should correspond to the path of <paramref name="access"/>. It is passed explicitly to avoid
        /// unnecesary conversions between strings and absolute paths.
        /// </remarks>
        /// <returns>Whether the given access needed resolution</returns>
        public bool ResolveDirectoryReparsePoints(FileAccessManifest manifest, ReportedFileAccess access, AbsolutePath accessPath, out ReportedFileAccess resolvedAccess, out AbsolutePath resolvedPath)
        {
            Contract.Requires(accessPath.IsValid);

            resolvedPath = ResolvePathWithCache(
                accessPath, 
                access.Path, 
                access.Operation, 
                access.FlagsAndAttributes,
                out string resolvedPathAsString,
                out bool isDirectoryReparsePoint);

            return ResolveAccess(manifest, access, accessPath, resolvedPath, resolvedPathAsString, isDirectoryReparsePoint, out resolvedAccess);
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

        /// <summary>
        /// Adds synthetic accesses for all intermediate directory reparse points for the given access path
        /// </summary>
        /// <remarks>
        /// TODO: This function is only adding accesses for reparse points in the given path, so if those reparse points point to others,
        /// those are not added. So the result is not completely sound, consider doing multi-hop resolution.
        /// </remarks>
        public void AddAccessesForIntermediateReparsePoints(FileAccessManifest manifest, ReportedFileAccess access, AbsolutePath accessPath, Dictionary<AbsolutePath, CompactSet<ReportedFileAccess>> accessesByPath)
        {
            Contract.Requires(accessPath.IsValid);
            AbsolutePath currentPath = accessPath.GetParent(m_context.PathTable);

            while (currentPath.IsValid)
            {
                // If we the current path is resolved and its resolved path is the same, then we know there are no more reparse points
                // in the path. Shorcut the search.
                if (m_resolvedPathCache.TryGetValue(currentPath, out var resolvedPath) && currentPath == resolvedPath)
                {
                    return;
                }

                bool isDirReparsePoint = IsDirectorySymlinkOrJunctionWithCache(ExpandedAbsolutePath.CreateUnsafe(currentPath, currentPath.ToString(m_context.PathTable)));

                if (isDirReparsePoint)
                {
                    accessesByPath.TryGetValue(currentPath, out CompactSet<ReportedFileAccess> existingAccessesToPath);
                    var generatedProbe = GenerateProbeForPath(manifest, currentPath, access);
                    accessesByPath[currentPath] = existingAccessesToPath.Add(generatedProbe);
                }

                currentPath = currentPath.GetParent(m_context.PathTable);
            }
        }

        private ReportedFileAccess GenerateProbeForPath(FileAccessManifest manifest, AbsolutePath currentPath, ReportedFileAccess access)
        {
            manifest.TryFindManifestPathFor(currentPath, out AbsolutePath manifestPath, out FileAccessPolicy nodePolicy);

            return new ReportedFileAccess(
                ReportedFileOperation.CreateFile,
                access.Process,
                RequestedAccess.Probe,
                (nodePolicy & FileAccessPolicy.AllowRead) != 0 ? FileAccessStatus.Allowed : FileAccessStatus.Denied,
                (nodePolicy & FileAccessPolicy.ReportAccess) != 0,
                access.Error,
                Usn.Zero,
                DesiredAccess.GENERIC_READ,
                ShareMode.FILE_SHARE_READ,
                CreationDisposition.OPEN_ALWAYS,
                // These generated accesses represent directory reparse points, so therefore we honor
                // the directory attribute
                FlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY,
                manifestPath,
                manifestPath == currentPath? null : currentPath.ToString(m_context.PathTable),
                string.Empty);
        }

        private AbsolutePath ResolvePathWithCache(
            AbsolutePath accessPath, 
            [CanBeNull] string accessPathAsString, 
            ReportedFileOperation operation, 
            FlagsAndAttributes flagsAndAttributes,
            [CanBeNull] out string resolvedPathAsString,
            out bool isDirectoryReparsePoint)
        {
            accessPathAsString ??= accessPath.ToString(m_context.PathTable);
            // If the final segment is a directory reparse point and the operation acts on it, don't resolve it
            if (ShouldNotResolveLastSegment(ExpandedAbsolutePath.CreateUnsafe(accessPath, accessPathAsString), operation, flagsAndAttributes, out isDirectoryReparsePoint))
            {
                AbsolutePath resolvedParentPath = ResolvePathWithCache(accessPath.GetParent(m_context.PathTable), accessPathAsString: null, operation, flagsAndAttributes, out string resolvedParentPathAsString, out _);
                PathAtom name = accessPath.GetName(m_context.PathTable);
                resolvedPathAsString = resolvedParentPathAsString != null ? Path.Combine(resolvedParentPathAsString, name.ToString(m_context.StringTable)) : null;
                return resolvedParentPath.Combine(m_context.PathTable, name);
            }

            // Check the cache
            var cachedResult = m_resolvedPathCache.TryGet(accessPath);
            if (cachedResult.IsFound)
            {
                // Observe we don't cache expanded paths since that might cause the majority of all the paths of a build to live
                // in memory for the whole execution time. So in case of a cache hit, we just return the absolute path. The expanded
                // path needs to be reconstructed as needed.
                resolvedPathAsString = null;
                return cachedResult.Item.Value;
            }

            // If not found and the path is not a reparse point, check the cache for its parent.
            // Many files may have the same parent, and since the path is not a reparse point we know that
            // resolve(parent\segment) == resolve(parent)\segment
            var parentPath = accessPath.GetParent(m_context.PathTable);
            if (parentPath.IsValid && !isDirectoryReparsePoint && m_resolvedPathCache.TryGet(parentPath) is var cachedParent && cachedParent.IsFound)
            {
                var lastFragment = accessPath.GetName(m_context.PathTable);
                resolvedPathAsString = null;
                AbsolutePath cachedParentPath = cachedParent.Item.Value;
                return cachedParentPath.IsValid ? 
                    cachedParentPath.Combine(m_context.PathTable, lastFragment) :
                    AbsolutePath.Create(m_context.PathTable, lastFragment.ToString(m_context.StringTable));
            }

            // The cache didn't have it, so let's resolve it
            if (!TryResolvePath(accessPathAsString, out ExpandedAbsolutePath resolvedExpandedPath))
            {
                // If we cannot get the final path (e.g. file not found causes this), then we assume the path
                // is already canonicalized

                // Observe we cannot update the cache since the path was not resolved.
                // TODO: we could consider always trying to resolve the parent

                resolvedPathAsString = accessPathAsString;

                return accessPath;
            }

            // Update the cache
            m_resolvedPathCache.TryAdd(accessPath, resolvedExpandedPath.Path);

            // If the path is not a reparse point, update the parent as well
            if (!isDirectoryReparsePoint && parentPath.IsValid)
            {
                // Observe we may be storing an invalid path if the resolved path does not have a parent.
                m_resolvedPathCache.TryAdd(parentPath, resolvedExpandedPath.Path.GetParent(m_context.PathTable));
            }

            // If the resolved path is the same as the access path, then we know its last fragment is not a reparse point.
            // So we might just update the reparse point cache as well and hopefully speed up subsequent requests to generate read accesses
            // for intermediate reparse point dirs
            if (accessPath == resolvedExpandedPath.Path)
            {
                while (parentPath.IsValid && m_reparsePointCache.TryAdd(parentPath, false))
                {
                    parentPath = parentPath.GetParent(m_context.PathTable);
                }
            }

            resolvedPathAsString = resolvedExpandedPath.ExpandedPath;
            return resolvedExpandedPath.Path;
        }

        private bool ShouldNotResolveLastSegment(ExpandedAbsolutePath accessPath, ReportedFileOperation operation, FlagsAndAttributes flagsAndAttributes, out bool isReparsePoint)
        {
            isReparsePoint = IsDirectorySymlinkOrJunctionWithCache(accessPath);
            // The following operations act on the reparse point directly, not on the target
            // TODO: the logic is not perfect but good enough. The detours implementation will replace
            // this eventually
            var operationOnReparsePoint = 
                (operation == ReportedFileOperation.CreateHardLinkSource ||
                 operation == ReportedFileOperation.CreateSymbolicLinkSource ||
                 operation == ReportedFileOperation.GetFileAttributes ||
                 operation == ReportedFileOperation.GetFileAttributesEx ||
                 operation == ReportedFileOperation.DeleteFile || 
                 (operation == ReportedFileOperation.CreateFile && (flagsAndAttributes & FlagsAndAttributes.FILE_FLAG_OPEN_REPARSE_POINT) != 0) ||
                 (operation == ReportedFileOperation.NtCreateFile && (flagsAndAttributes & FlagsAndAttributes.FILE_FLAG_OPEN_REPARSE_POINT) != 0) ||
                 (operation == ReportedFileOperation.ZwCreateFile && (flagsAndAttributes & FlagsAndAttributes.FILE_FLAG_OPEN_REPARSE_POINT) != 0) ||
                 (operation == ReportedFileOperation.ZwOpenFile && (flagsAndAttributes & FlagsAndAttributes.FILE_FLAG_OPEN_REPARSE_POINT) != 0));

            return operationOnReparsePoint && isReparsePoint;
        }

        private bool IsDirectorySymlinkOrJunctionWithCache(ExpandedAbsolutePath path)
        {
            if (m_reparsePointCache.TryGet(path.Path) is var cachedResult && cachedResult.IsFound)
            {
                return cachedResult.Item.Value;
            }

            var result = FileUtilities.IsDirectorySymlinkOrJunction(path.ExpandedPath);
            m_reparsePointCache.TryAdd(path.Path, result);

            return result;
        }

        private bool ResolveAccess(
            FileAccessManifest manifest,
            ReportedFileAccess access, 
            AbsolutePath accessPath, 
            AbsolutePath resolvedPath, 
            [CanBeNull]string resolvedPathAsString,
            bool isDirectoryReparsePoint,
            out ReportedFileAccess resolvedAccess)
        {
            var flags = isDirectoryReparsePoint ?
                access.FlagsAndAttributes | FlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY :
                access.FlagsAndAttributes;

            // If they are different, this means the original path contains reparse points we need to resolve
            if (resolvedPath != accessPath)
            {
                if (access.ManifestPath == resolvedPath)
                {
                    // When the manifest path matches the path, we don't store the latter
                    resolvedAccess = access.CreateWithPathAndAttributes(
                        null, 
                        access.ManifestPath, 
                        flags);
                }
                else
                {
                    resolvedPathAsString ??= resolvedPath.ToString(m_context.PathTable);

                    // In this case we need to normalize the manifest path as well
                    // Observe the resolved path is fully resolved, and therefore if a manifest path is found, it will
                    // also be fully resolved
                    // If no manifest is found for the resolved path, the result is invalid, which is precisely what we need in resolvedManifestPath
                    manifest.TryFindManifestPathFor(resolvedPath, out AbsolutePath resolvedManifestPath, out _); 

                    resolvedAccess = access.CreateWithPathAndAttributes(
                        resolvedPath == resolvedManifestPath ? null : resolvedPathAsString,
                        resolvedManifestPath,
                        flags);
                }

                return true;
            }

            resolvedAccess = access;
            return false;
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
