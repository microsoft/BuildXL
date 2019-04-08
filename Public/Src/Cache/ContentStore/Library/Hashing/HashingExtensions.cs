// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Convenience extensions for hashing content in various forms.
    /// </summary>
    public static class HashingExtensions
    {
        /// <summary>
        ///     Default buffer size for streams used by hashers.
        /// </summary>
        public const int HashStreamBufferSize = 64 * 1024;

        /// <summary>
        ///     Calculate content hash of content in a byte array.
        /// </summary>
        public static ContentHash CalculateHash(this byte[] content, HashType hashType)
        {
            using (var hasher = HashInfoLookup.Find(hashType).CreateContentHasher())
            {
                return hasher.GetContentHash(content);
            }
        }

        /// <summary>
        ///     Calculate content hash of content in a file.
        /// </summary>
        /// <exception cref="FileNotFoundException">Throws if the file <paramref name="path"/> is not on disk.</exception>
        public static async Task<ContentHash> CalculateHashAsync(this IAbsFileSystem fileSystem, AbsolutePath path, HashType hashType)
        {
            using (var stream = await fileSystem.OpenSafeAsync(
                path, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete, FileOptions.SequentialScan, HashStreamBufferSize))
            {
                return await stream.CalculateHashAsync(hashType);
            }
        }

        /// <summary>
        ///     Calculate content hash of content in a stream.
        /// </summary>
        public static async Task<ContentHash> CalculateHashAsync(this Stream stream, HashType hashType)
        {
            using (var hasher = HashInfoLookup.Find(hashType).CreateContentHasher())
            {
                return await hasher.GetContentHashAsync(stream);
            }
        }

        /// <summary>
        /// Whether <paramref name="contentHash"/> is the empty hash.
        /// </summary>
        public static bool IsEmptyHash(this ContentHash contentHash)
        {
            return contentHash == HashInfoLookup.Find(contentHash.HashType).EmptyHash;
        }
    }
}
