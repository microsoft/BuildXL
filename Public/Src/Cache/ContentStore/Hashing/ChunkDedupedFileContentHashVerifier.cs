// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Provides functionality for content verification of chunk deduped files.  
    /// </summary>
    public static class ChunkDedupedFileContentHashVerifier
    {
        private static readonly IContentHasher ChunkHasher = DedupSingleChunkHashInfo.Instance.CreateContentHasher();

        /// <summary>
        /// Verifies the content of a given chunk deduped file.
        /// </summary>
        /// <param name="filePath">
        /// The path to the file.
        /// </param>
        /// <param name="expectedChunks">
        /// The file's expected chunks.
        /// </param>
        /// <param name="expectedHash">
        /// The expected hash of the file.
        /// </param>
        /// <param name="cancellationToken">
        /// A cancellation token.
        /// </param>
        /// <returns>
        /// true if the expected hash matches the file's content hash; false, otherwise.
        /// </returns>
        public static async Task<bool> VerifyFileAsync(string filePath, IList<ChunkInfo> expectedChunks, ChunkDedupedFileContentHash expectedHash, CancellationToken cancellationToken)
        {
            using (var fileStream = FileStreamUtility.OpenFileStreamForAsync(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                return await VerifyStreamAsync(fileStream, expectedChunks, expectedHash, cancellationToken);
            }
        }

        internal static async Task<bool> VerifyStreamAsync(Stream stream, IList<ChunkInfo> expectedChunks, ChunkDedupedFileContentHash expectedHash, CancellationToken cancellationToken)
        {
            ulong totalBytesChunked = 0;
            var producedChunks = new List<ChunkInfo>(expectedChunks.Count);
            var maxChunkSize = expectedChunks.Max((chunk) => chunk.Size);
            var buffer = new byte[maxChunkSize];

            foreach (var currentChunk in expectedChunks)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, (int)currentChunk.Size, cancellationToken);
                if (bytesRead != currentChunk.Size)
                {
                    return false;
                }

                byte[] chunkHash = ChunkHasher.GetContentHash(
                    buffer,
                    0,
                    bytesRead).ToHashByteArray();

                if (!chunkHash.SequenceEqual(currentChunk.Hash))
                {
                    // Hash mismatch
                    return false;
                }

                producedChunks.Add(new ChunkInfo(
                    totalBytesChunked,
                    currentChunk.Size,
                    chunkHash));

                totalBytesChunked += (ulong)bytesRead;
            }

            if (stream.ReadByte() != -1)
            {
                // File content is longer
                return false;
            }

            var node = DedupNode.Create(producedChunks);
            var hashBytesExcludingAlgorithm = node.Hash.Take(DedupSingleChunkHashInfo.Length).ToArray();
            var actualHash = new ChunkDedupedFileContentHash(hashBytesExcludingAlgorithm);

            return expectedHash == actualHash;
        }
    }
}
