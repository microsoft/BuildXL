// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Storage;
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

        /// <summary>
        ///     Constructor.
        /// </summary>
        /// <param name="fileFullPath">
        ///     Path to the target file which is to be added to drop.  The file must exist on disk at the time of invocation.
        /// </param>
        /// <param name="relativeDropPath">
        ///     Relative path under which to associate the file in the target drop.  If null, file name of the file is used.
        /// </param>
        /// <param name="fileContentInfo">
        ///     Expected content hash and file length. May be left null.
        /// </param>
        public DropItemForFile(string fileFullPath, string relativeDropPath = null, FileContentInfo? fileContentInfo = null)
        {
            Contract.Requires(fileFullPath != null);

            FullFilePath = Path.GetFullPath(fileFullPath);
            RelativeDropPath = relativeDropPath ?? Path.GetFileName(FullFilePath);
            if (fileContentInfo != null)
            {
                var contentInfo = fileContentInfo.Value;

                if (contentInfo.Hash.HashType.Equals(BuildXL.Cache.ContentStore.Hashing.HashType.DedupChunk))
                {
                    BlobIdentifier = new ChunkDedupIdentifier(contentInfo.Hash.ToHashByteArray()).ToBlobIdentifier();
                }
                else
                {
                    BlobIdentifier = new BlobIdentifier(contentInfo.Hash.ToHashByteArray());
                }

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
            Contract.Ensures(Contract.Result<BlobIdentifier>() != null);

            BuildXL.Cache.ContentStore.Hashing.ContentHash contentHash;
            if (!BuildXL.Cache.ContentStore.Hashing.ContentHash.TryParse(serializedVsoHash, out contentHash))
            {
                throw new DaemonException("Could not parse content hash: " + serializedVsoHash);
            }

            return new BlobIdentifier(contentHash.ToHashByteArray());
        }
    }
}
