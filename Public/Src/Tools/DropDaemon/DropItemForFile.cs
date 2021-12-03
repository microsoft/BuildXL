// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Tool.ServicePipDaemon;
using static BuildXL.Utilities.FormattableStringEx;

namespace Tool.DropDaemon
{
    /// <summary>
    ///    Drop item tied to a file on disk.
    /// </summary>
    public class DropItemForFile : IDropItem
    {
        private const int UnknownFileLength = 0;
        private const int MaxNonLongFileNameLength = 260;
        private const string LongFileNamePrefix = @"\\?\";

        /// <summary>
        ///     Constructor.
        /// </summary>
        /// <param name="fullDropName">
        ///     Fully qualified name of a drop.
        /// </param>
        /// <param name="fileFullPath">
        ///     Path to the target file which is to be added to drop.  The file must exist on disk at the time of invocation.
        /// </param>
        /// <param name="relativeDropPath">
        ///     Relative path under which to associate the file in the target drop.  If null, file name of the file is used.
        /// </param>
        /// <param name="fileContentInfo">
        ///     Expected content hash and file length. May be left null.
        /// </param>
        public DropItemForFile(string fullDropName, string fileFullPath, string relativeDropPath = null, FileContentInfo? fileContentInfo = null)
        {
            Contract.Requires(fileFullPath != null);

            FullyQualifiedDropName = fullDropName;
            FullFilePath = Path.GetFullPath(fileFullPath);
            if (FullFilePath.Length >= MaxNonLongFileNameLength && !FullFilePath.StartsWith(LongFileNamePrefix))
            {
                // this file has a long file name, need to add a prefix to it
                FullFilePath = $"{LongFileNamePrefix}{FullFilePath}";
            }

            RelativeDropPath = relativeDropPath ?? Path.GetFileName(FullFilePath);
            if (fileContentInfo != null)
            {
                var contentInfo = fileContentInfo.Value;
                BlobIdentifier = ContentHashToBlobIdentifier(contentInfo.Hash);
                FileLength = contentInfo.HasKnownLength ? contentInfo.Length : UnknownFileLength;
            }
            else
            {
                BlobIdentifier = null;
                FileLength = UnknownFileLength;
            }
        }

        /// <inheritdoc />
        public string FullFilePath { get; }

        /// <inheritdoc />
        public BlobIdentifier BlobIdentifier { get; }

        /// <inheritdoc />
        public long FileLength { get; }

        /// <inheritdoc />
        public string RelativeDropPath { get; }

        /// <inheritdoc />
        public string FullyQualifiedDropName { get; }
        
        /// <inheritdoc />
        public virtual FileArtifact? Artifact => null;

        /// <inheritdoc />
        public virtual Task<FileInfo> EnsureMaterialized()
        {
            if (!File.Exists(FullFilePath))
            {
                throw new DaemonException("File not found on disk: " + FullFilePath);
            }

            return Task.FromResult(new FileInfo(FullFilePath));
        }

        /// <summary>
        ///     Calculates <see cref="FileBlobDescriptor"/> from a file on disk.
        /// </summary>
        public static async Task<FileBlobDescriptor> ComputeFileDescriptorFromFileAsync(string filePath, bool chunkDedup, string relativeDropPath, CancellationToken cancellationToken)
        {
            Contract.Requires(!string.IsNullOrEmpty(filePath));
            Contract.Requires(File.Exists(filePath));

            var folderName = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);
            var fbd = await FileBlobDescriptor.CalculateAsync(folderName, chunkDedup, fileName, FileBlobType.File, cancellationToken);
            return new FileBlobDescriptor(new DropFile(relativeDropPath, fbd.FileSize, fbd.BlobIdentifier), filePath);
        }

        /// <summary>
        ///     Checks if a given <see cref="BlobIdentifier"/> matches one computed from a file on disk
        ///     (at <paramref name="filePath"/> location).  If it doesn't, throws <see cref="DaemonException"/>.
        /// </summary>
        /// <remarks>
        ///     This method should only be called from '#if DEBUG' blocks, because it's wasteful to recompute hashes all the time.
        /// </remarks>
        public static async Task ComputeAndDoubleCheckBlobIdentifierAsync(BlobIdentifier precomputed, string filePath, long expectedFileLength, bool chunkDedup, string phase, CancellationToken cancellationToken)
        {
            Contract.Requires(precomputed != null);
            Contract.Requires(!string.IsNullOrEmpty(filePath));
            Contract.Requires(File.Exists(filePath));

            var calculated = (await ComputeFileDescriptorFromFileAsync(filePath, chunkDedup, Path.GetFileName(filePath), cancellationToken)).BlobIdentifier;
            if (!precomputed.Equals(calculated))
            {
                throw new DaemonException(I($"[{phase}] Given blob identifier ({precomputed}) differs from computed one ({calculated}) for file '{filePath}'."));
            }

            var actualFileLength = new FileInfo(filePath).Length;
            if (expectedFileLength > 0 && expectedFileLength != actualFileLength)
            {
                throw new DaemonException(I($"[{phase}] Given file length ({expectedFileLength}) differs from the file size on disk({actualFileLength}) for file '{filePath}'."));
            }
        }

        /// <summary>
        ///     Deserializes <see cref="BlobIdentifier"/> from a string.
        ///
        ///     Throws <see cref="DaemonException"/> if the string cannot be parsed.
        /// </summary>
        private static BlobIdentifier DeserializeBlobIdentifierFromHash(string serializedVsoHash)
        {
            Contract.Requires(serializedVsoHash != null);

            BuildXL.Cache.ContentStore.Hashing.ContentHash contentHash;
            if (!BuildXL.Cache.ContentStore.Hashing.ContentHash.TryParse(serializedVsoHash, out contentHash))
            {
                throw new DaemonException("Could not parse content hash: " + serializedVsoHash);
            }

            return new BlobIdentifier(contentHash.ToHashByteArray());
        }

        private static BlobIdentifier ContentHashToBlobIdentifier(BuildXL.Cache.ContentStore.Hashing.ContentHash contentHash)
        {
            switch (contentHash.HashType)
            {
                case BuildXL.Cache.ContentStore.Hashing.HashType.Vso0:
                case BuildXL.Cache.ContentStore.Hashing.HashType.Dedup64K:
                case BuildXL.Cache.ContentStore.Hashing.HashType.Dedup1024K:
                case BuildXL.Cache.ContentStore.Hashing.HashType.Murmur:
                    return new BlobIdentifier(contentHash.ToHashByteArray());
                case BuildXL.Cache.ContentStore.Hashing.HashType.DedupSingleChunk:
                    return new ChunkDedupIdentifier(contentHash.ToHashByteArray()).ToBlobIdentifier();
                case BuildXL.Cache.ContentStore.Hashing.HashType.DedupNode:
                    return new NodeDedupIdentifier(contentHash.ToHashByteArray(), NodeAlgorithmId.Node64K).ToBlobIdentifier();
                default:
                    throw new ArgumentException($"ContentHash has unsupported type when converting to BlobIdentifier: {contentHash.HashType}");
            }
        }
    }
}
