// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;

namespace ContentStoreTest.Distributed.ContentLocation
{
    internal static class PathUtilities
    {
        public static AbsolutePath GetContentPath(string rootPath, ContentHash contentHash)
        {
            return GetContentPath(new AbsolutePath(rootPath), contentHash);
        }

        public static AbsolutePath GetContentPath(AbsolutePath rootPath, ContentHash contentHash)
        {
            string hash = contentHash.ToHex();
            return rootPath / "Shared" / contentHash.HashType.ToString() / hash.Substring(0, 3) / (hash + ".blob");
        }

        public static string GetRootPath(AbsolutePath contentPath)
        {
            int indexOfShared = contentPath.Path.IndexOf("Shared", StringComparison.OrdinalIgnoreCase);
            string rootPath = indexOfShared > -1 ? contentPath.Path.Substring(0, indexOfShared) : contentPath.Path;

            // Trim trailing slash character to normalize path with what would be returned by AbsolutePath.ToString()
            return rootPath.TrimEnd('\\');
        }
    }
}
