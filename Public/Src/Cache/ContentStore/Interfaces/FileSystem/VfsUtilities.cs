// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// Defines utility methods for working with VFS
    /// </summary>
    public static class VfsUtilities
    {
        private static readonly string DirectorySeparatorCharString = Path.DirectorySeparatorChar.ToString();
        private static readonly char[] PathSplitChars = new[] { Path.DirectorySeparatorChar };
        private static readonly char[] FilePlacementInfoFileNameSplitChars = new[] { '_' };

        /// <summary>
        /// Gets whether a path is contained in another path
        /// </summary>
        public static bool IsPathWithin(this string path, string candidateParent)
        {
            if (path.Length <= candidateParent.Length || !path.StartsWith(candidateParent, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (candidateParent.EndsWith(DirectorySeparatorCharString))
            {
                return true;
            }

            return path[candidateParent.Length] == Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// Gets whether a path is contained in another path and returns the relative path from <paramref name="candidateParent"/> if <paramref name="path"/> is a subpath.
        /// </summary>
        public static bool TryGetRelativePath(this string path, string candidateParent, out string relativePath)
        {
            if (path.IsPathWithin(candidateParent))
            {
                relativePath = path.Substring(candidateParent.Length + (candidateParent.EndsWith(DirectorySeparatorCharString) ? 0 : 1));
                return true;
            }
            else
            {
                relativePath = default;
                return false;
            }
        }
    }
}
