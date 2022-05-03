// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.UtilitiesCore;

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
            return HashInfoLookup.GetContentHasher(hashType).GetContentHash(content);
        }

        /// <summary>
        ///     Calculate content hash of content in a file.
        /// </summary>
        /// <exception cref="FileNotFoundException">Throws if the file <paramref name="path"/> is not on disk.</exception>
#if NETCOREAPP
        public static Task<ContentHash> CalculateHashAsync(this IAbsFileSystem fileSystem, AbsolutePath path, HashType hashType)
        {
            return Task.FromResult(fileSystem.OpenForHashing(path).ToFileStream().HashFile(hashType));
        }
#else
        public static async Task<ContentHash> CalculateHashAsync(this IAbsFileSystem fileSystem, AbsolutePath path, HashType hashType)
        {
            using var stream = fileSystem.Open(
                path, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete, FileOptions.SequentialScan, HashStreamBufferSize);
            return await stream.CalculateHashAsync(hashType);
        }
#endif

        /// <summary>
        /// Opens a file for hashing purposes.
        /// </summary>
        public static StreamWithLength OpenForHashing(this IAbsFileSystem fileSystem, AbsolutePath path)
        {
            // Using a helper from the hashing layer that will pass the right options required for file hashing.
            return ContentHashingHelper.OpenForHashing(path.Path,
                tuple => fileSystem.Open(
                    path,
                    tuple.fileAccess,
                    tuple.mode,
                    tuple.fileShare,
                    tuple.options,
#if NETCOREAPP
                    tuple.bufferSize).ToFileStream());
#else
                    FileSystemDefaults.DefaultFileStreamBufferSize).ToFileStream());
#endif
        }

        /// <summary>
        ///     Calculate content hash of content in a stream.
        /// </summary>
        public static Task<ContentHash> CalculateHashAsync(this StreamWithLength stream, HashType hashType)
        {
#if NETCOREAPP
            if (stream.Stream is FileStream fileStream)
            {
                return Task.FromResult(fileStream.HashFile(hashType));
            }
#endif // NETCOREAPP

            var hasher = HashInfoLookup.GetContentHasher(hashType);
            return hasher.GetContentHashAsync(stream);
        }

        /// <summary>
        /// Whether <paramref name="contentHash"/> is the empty hash.
        /// </summary>
        public static bool IsEmptyHash(this ContentHash contentHash)
        {
            return contentHash == HashInfoLookup.Find(contentHash.HashType).EmptyHash;
        }

        /// <summary>
        /// Casts <see cref="StreamWithLength"/> to <see cref="FileStream"/>.
        /// </summary>
        public static FileStream ToFileStream(this StreamWithLength streamWithLength) => (FileStream)streamWithLength.Stream;
        
        /// <nodoc />
        public static bool IsZero(this ContentHash contentHash)
        {
            return contentHash == HashInfoLookup.Find(contentHash.HashType).Zero;
        }
    }
}
