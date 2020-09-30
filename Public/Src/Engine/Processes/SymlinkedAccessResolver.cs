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
    /// Resolves file accesses containing symlinked paths by normalizing them into corresponding real (non-symlinked) ones
    /// </summary>
    public sealed class SymlinkedAccessResolver
    {
        private readonly PipExecutionContext m_context;
        private readonly DirectoryTranslator m_directoryTranslator;

        /// <summary>
        /// Paths (containing symlinks or not) to resolved (non-symlinked) paths
        /// </summary>
        private readonly ConcurrentBigMap<AbsolutePath, AbsolutePath> m_resolvedPathCache = new ConcurrentBigMap<AbsolutePath, AbsolutePath>();
        
        /// <summary>
        /// Paths to whether their last fragment is a symlink
        /// </summary>
        /// <remarks>
        /// Observe there is a symklink cache already in the sandbox process pip executor, but that one is only used
        /// for the mac case (whereas this class is only used for Windows), and it is local to the current pip.
        /// </remarks>
        private readonly ConcurrentBigMap<AbsolutePath, bool> m_symlinkCache = new ConcurrentBigMap<AbsolutePath, bool>();

        /// <nodoc/>
        public SymlinkedAccessResolver(PipExecutionContext context, [CanBeNull] DirectoryTranslator directoryTranslator)
        {
            Contract.RequiresNotNull(context);
            m_context = context;
            m_directoryTranslator = directoryTranslator;
        }

        /// <summary>
        /// Given an access whose path may contain intermediate symlinks, returns an equivalent resolved access where all the symlinks
        /// are resolved.
        /// </summary>
        /// <remarks>
        /// If the symlink is the final fragment of the path, this function does not consider it to need resolution.
        /// <paramref name="accessPath"/> should correspond to the path of <paramref name="access"/>. It is passed explicitly to avoid
        /// unnecesary conversions between strings and absolute paths.
        /// </remarks>
        /// <returns>Whether the given access needed resolution</returns>
        public bool ResolveDirectorySymlinks(FileAccessManifest manifest, ReportedFileAccess access, AbsolutePath accessPath, out ReportedFileAccess resolvedAccess, out AbsolutePath resolvedPath)
        {
            Contract.Requires(accessPath.IsValid);

            resolvedPath = ResolvePathWithCache(accessPath, access.Path, out string resolvedPathAsString);

            return ResolveAccess(manifest, access, accessPath, resolvedPath, resolvedPathAsString, out resolvedAccess);
        }

        /// <summary>
        /// Adds synthetic accesses for all intermediate directory symlinks for the given access path
        /// </summary>
        /// <remarks>
        /// TODO: This function is only adding accesses for symlinks in the given path, so if those suymlinks point to other symlinks,
        /// those are not added. So the result is not completely sound, consider doing multi-hop resolution.
        /// </remarks>
        public void AddAccessesForIntermediateSymlinks(FileAccessManifest manifest, ReportedFileAccess access, AbsolutePath accessPath, Dictionary<AbsolutePath, CompactSet<ReportedFileAccess>> accessesByPath)
        {
            Contract.Requires(accessPath.IsValid);
            AbsolutePath currentPath = accessPath.GetParent(m_context.PathTable);

            while (currentPath.IsValid)
            {
                // If we the current path is resolved and its resolved path is the same, then we know there are no more symlinks
                // in the path. Shorcut the search.
                if (m_resolvedPathCache.TryGetValue(currentPath, out var resolvedPath) && currentPath == resolvedPath)
                {
                    return;
                }

                bool isDirSymlink;
                var result = m_symlinkCache.TryGet(currentPath);
                if (!result.IsFound)
                {
                    isDirSymlink = FileUtilities.IsDirectorySymlinkOrJunction(currentPath.ToString(m_context.PathTable));
                    m_symlinkCache.TryAdd(currentPath, isDirSymlink);
                }
                else
                {
                    isDirSymlink = result.Item.Value;
                }

                if (isDirSymlink)
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
                FlagsAndAttributes.FILE_ATTRIBUTE_ARCHIVE,
                manifestPath,
                manifestPath == currentPath? null : currentPath.ToString(m_context.PathTable),
                string.Empty);
        }

        private AbsolutePath ResolvePathWithCache(AbsolutePath accessPath, [CanBeNull] string accessPathAsString, [CanBeNull] out string resolvedPathAsString)
        {
            // BuildXL already handles the case of paths whose final fragment is a symlink. So we make sure to resolve everything else.

            // If the final segment is a directory reparse point, don't resolve it
            accessPathAsString ??= accessPath.ToString(m_context.PathTable);
            if (FileUtilities.IsDirectorySymlinkOrJunction(accessPathAsString))
            {
                AbsolutePath resolvedParentPath = ResolvePathWithCache(accessPath.GetParent(m_context.PathTable), accessPathAsString: null, out string resolvedParentPathAsString);
                PathAtom name = accessPath.GetName(m_context.PathTable);
                resolvedPathAsString = resolvedParentPathAsString != null ? Path.Combine(resolvedParentPathAsString, name.ToString(m_context.StringTable)) : null;
                return resolvedParentPath.Combine(m_context.PathTable, name);
            }

            var parentPath = accessPath.GetParent(m_context.PathTable);
            if (!parentPath.IsValid)
            {
                // The path doesn't have a root, so even if it is a symlink, BuildXL should be good with it
                resolvedPathAsString = accessPathAsString;
                return accessPath;
            }

            var lastFragment = accessPath.GetName(m_context.PathTable);

            // In addition to buildxl already handling final fragment symlinks, by caching parents we enhance the chance of a hit, where
            // many files shared the same parent. Query the cache with it.
            var cachedResult = m_resolvedPathCache.TryGet(parentPath);
            if (cachedResult.IsFound)
            {
                // Observe we don't cache expanded paths since that might cause the majority of all the paths of a build to live
                // in memory for the whole execution time. So in case of a cache hit, we just return the absolute path. The expanded
                // path needs to be reconstructed as needed.
                resolvedPathAsString = null;
                var resolvedParentPath = cachedResult.Item.Value;

                // The cached resolved path could be invalid, meaning the original parent pointed to a drive's root
                return resolvedParentPath.IsValid ?
                    resolvedParentPath.Combine(m_context.PathTable, lastFragment) :
                    AbsolutePath.Create(m_context.PathTable, lastFragment.ToString(m_context.StringTable));
            }

            // The cache didn't have it, so let's resolve it
            if (!TryResolveSymlinkedPath(accessPathAsString, out ExpandedAbsolutePath resolvedExpandedPath))
            {
                // If we cannot get the final path (e.g. file not found causes this), then we assume the path
                // is already canonicalized

                // Observe we cannot update the cache since the path was not resolved.
                // TODO: we could consider always trying to resolve the parent
                
                resolvedPathAsString = accessPathAsString;

                return accessPath;
            }

            // Update the cache. Observe we may be storing an invalid path if the resolved path does not have a parent.
            var resolvedPathParent = resolvedExpandedPath.Path.GetParent(m_context.PathTable);
            m_resolvedPathCache.TryAdd(parentPath, resolvedPathParent);

            // If the resolved path is the same as the access path, then we know its last fragment is not a symlink.
            // So we might just update the symlink cache as well and hopefully speed up subsequent requests to generate read accesses
            // for intermediate symlink dirs
            if (parentPath == resolvedExpandedPath.Path)
            {
                m_symlinkCache.TryAdd(parentPath, false);
            }

            resolvedPathAsString = resolvedExpandedPath.ExpandedPath;
            return resolvedExpandedPath.Path;
        }

        private bool ResolveAccess(
            FileAccessManifest manifest,
            ReportedFileAccess access, 
            AbsolutePath accessPath, 
            AbsolutePath resolvedPath, 
            [CanBeNull]string resolvedPathAsString,
            out ReportedFileAccess resolvedAccess)
        {
            // If they are different, this means the original path contains symlinks we need to resolve
            if (resolvedPath != accessPath)
            {
                if (access.ManifestPath == resolvedPath)
                {
                    // When the manifest path matches the path, we don't store the latter
                    resolvedAccess = access.CreateWithPath(null, access.ManifestPath);
                }
                else
                {
                    resolvedPathAsString ??= resolvedPath.ToString(m_context.PathTable);

                    // In this case we need to normalize the manifest path as well
                    // Observe the resolved path is fully resolved, and therefore if a manifest path is found, it will
                    // also be fully resolved
                    // If no manifest is found for the resolved path, the result is invalid, which is precisely what we need in resolvedManifestPath
                    manifest.TryFindManifestPathFor(resolvedPath, out AbsolutePath resolvedManifestPath, out _); 

                    resolvedAccess = access.CreateWithPath(
                        resolvedPath == resolvedManifestPath ? null : resolvedPathAsString,
                        resolvedManifestPath);
                }

                return true;
            }

            resolvedAccess = access;
            return false;
        }

        private bool TryResolveSymlinkedPath(string path, out ExpandedAbsolutePath expandedFinalPath)
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
