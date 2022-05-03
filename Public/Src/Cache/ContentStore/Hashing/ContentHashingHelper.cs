// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing.FileSystemHelpers;

#pragma warning disable CS3001 // CLS
#pragma warning disable CS3002
#pragma warning disable CS3003

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Helper utilities for hashing files using memory mapped files.
    /// </summary>
    /// <remarks>
    /// Some underlying functionality the utilities relies on are only available in .NET CORE
    /// (namely, HashAlgorithm.TryComputeHash is only available in .NET Framework ).
    /// </remarks>
    public static class ContentHashingHelper
    {
        /// <summary>
        /// The max file size when the memory mapped files can be used.
        /// </summary>
        public const int MaxSupportedSizeForMemoryMappedFileHashing = int.MaxValue - 1;

        /// <summary>
        /// Open a given <paramref name="absoluteFilePath"/> for hashing.
        /// </summary>
        public static FileStream OpenForHashing(string absoluteFilePath)
        {
            return OpenForHashing(
                absoluteFilePath,
                tuple => new FileStream(tuple.path, tuple.mode, tuple.fileAccess, tuple.fileShare, tuple.bufferSize, tuple.options));
        }

        /// <summary>
        /// A core method that opens a file by calling a <paramref name="openFileStream"/> callback and passes the options most optimal for file hashing.
        /// </summary>
        /// <remarks>
        /// This helper function is needed mostly because the cache code uses a file system abstraction layer instead of using the files directly.
        /// So in order to avoid code duplication the actual logic that opens the stream is abstracted away via a callback.
        /// </remarks>
        public static FileStream OpenForHashing(
            string absoluteFilePath,
            Func<(string path, FileMode mode, FileAccess fileAccess, FileShare fileShare, int bufferSize, FileOptions options), FileStream> openFileStream)
        {
            // Passing '1' as the buffer size to avoid excessive buffer allocations by the file stream
            // because we're not going to use it anyways.
            // And passing 'SequentialScan' options to potentially speed up the file access operations.
            // It may not help because per Raymond Chen's blog post ("How do FILE_FLAG_SEQUENTIAL_SCAN and FILE_FLAG_RANDOM_ACCESS affect how the operating system treats my file?")
            // it seems that memory-mapped file access does not use the cache manager.
            // But performance testing showed some minor (~5%) gains when this flag is passed. The difference is small and maybe just a measurement error
            // but the flag will definitely not hurt the performance.
            // And it is possible that different versions of the OS may respect this and give us better performance.

            // Allow shared reads, but we can't allow shared deletes
            // because when the file is used via memory mapped file even when FileShare.Delete flag is passed
            // the file deletion will fail with UnauthorizedAccessException.
            return openFileStream((absoluteFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan));
        }

        /// <summary>
        /// Calculate content hash of content in a byte array.
        /// </summary>
        public static ContentHash CalculateBytesHash(this byte[] content, HashType hashType)
        {
            return HashInfoLookup.GetContentHasher(hashType).GetContentHash(content);
        }

        /// <summary>
        /// Calculate content hash of content in a byte array.
        /// </summary>
        public static ContentHash CalculateBytesHash(this ArraySegment<byte> content, HashType hashType)
        {
            return HashInfoLookup.GetContentHasher(hashType).GetContentHash(content.Array!, content.Offset, content.Count);
        }

#if NETCOREAPP
        /// <summary>
        /// Calculate content hash of content in a byte array.
        /// </summary>
        public static ContentHash CalculateHash(this ReadOnlySpan<byte> content, HashType hashType)
        {
            return HashInfoLookup.GetContentHasher(hashType).GetContentHash(content);
        }

        /// <summary>
        /// Returns a <see cref="ContentHash" /> of the file at the given absolute path.
        /// </summary>
        /// <remarks>
        /// Using memory mapped files have some caveats:
        /// 1. This operation is synchronous.
        ///    For SSD drives it is fine, because the IO is very fast making this operation effectively CPU bound.
        ///    But we may decide to use <see cref="FileStream"/>-based version on HDD.
        ///    Or the client may decide to wrap this call into <see cref="Task.Run(System.Action)"/> to not block the current thread.
        /// 2. While the hashing is pending the file can't be deleted. Even if <see cref="FileShare.Delete"/> flag is passed to the <see cref="FileStream"/> constructor
        ///    the file is "locked" for deletion.
        /// </remarks>
        public static ContentHash HashFile(string absoluteFilePath, HashType hashType)
        {
            Contract.Requires(Path.IsPathRooted(absoluteFilePath), "File path must be absolute");

            using var fileStream = OpenForHashing(absoluteFilePath);
            return HashFile(fileStream, hashType);
        }

        /// <summary>
        /// Hashes a given <paramref name="fileStream"/> with a given <paramref name="hashType"/>.
        /// </summary>
        public static ContentHash HashFile(this FileStream fileStream, HashType hashType)
        {
            // Special casing the empty files.
            // Accessing 'Length' property only when the file is seakable.
            if (fileStream.CanSeek && fileStream.Length == 0)
            {
                return HashInfoLookup.Find(hashType).EmptyHash;
            }

            var hasher = HashInfoLookup.GetContentHasher(hashType);

            if (fileStream.Length > MaxSupportedSizeForMemoryMappedFileHashing)
            {
                // Currently we don't support files large then 2Gb, because we can't get the span of bytes from such a large files
                // (the span's length is int).
                // We may change this in the future, but for now just falling back to the old behavior.
                // Using an async method and blocking it, but this is an extremely rare case, so we don't want to introduce asynchrony just for it.
                return hasher.GetContentHashAsync(fileStream).GetAwaiter().GetResult();
            }

            using var memoryMappedFileHandle = MemoryMappedFileHandle.CreateReadOnly(fileStream, leaveOpen: true);
            return hasher.GetContentHash(memoryMappedFileHandle.Content);
        }
#endif // NETCOREAPP
    }
}
