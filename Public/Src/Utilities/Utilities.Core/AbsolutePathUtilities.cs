// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Core
{
    /// <nodoc/>
    public static class AbsolutePathUtilities
    {
        /// <summary>
        /// Filters the given directories so only paths that are not nested within any other path are returned
        /// </summary>
        public static ISet<AbsolutePath> CollapseDirectories(ICollection<AbsolutePath> directories, PathTable pathTable)
        {
            return CollapseDirectories(directories, pathTable, out _);
        }

        /// <summary>
        /// <see cref="CollapseDirectories(ICollection{AbsolutePath}, PathTable)"/>, and additionaly returns a mapping from the original directories to their collapsed parent
        /// directory (or self).
        /// </summary>
        public static ISet<AbsolutePath> CollapseDirectories(ICollection<AbsolutePath> directories, PathTable pathTable, out IDictionary<AbsolutePath, AbsolutePath> originalToCollapsedMapping)
        {
            var dedupPaths = new HashSet<AbsolutePath>();
            originalToCollapsedMapping = new Dictionary<AbsolutePath, AbsolutePath>(directories.Count);

            foreach (var directory in directories)
            {
                var skip = false;
                var parentDirectory = directory.GetParent(pathTable);
                if (parentDirectory.IsValid)
                {
                    foreach (var parent in pathTable.EnumerateHierarchyBottomUp(parentDirectory.Value))
                    {
                        var parentAsPath = new AbsolutePath(parent);
                        if (directories.Contains(parentAsPath))
                        {
                            originalToCollapsedMapping.Add(directory, parentAsPath);
                            skip = true;
                            break;
                        }
                    }
                }
                if (!skip)
                {
                    dedupPaths.Add(directory);
                    originalToCollapsedMapping.Add(directory, directory);
                }
            }

            return dedupPaths;
        }
    }
}
