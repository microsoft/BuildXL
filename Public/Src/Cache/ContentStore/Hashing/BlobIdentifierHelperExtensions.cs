// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// BlobIdentifier helpers
    /// </summary>
    public static class BlobIdentifierHelperExtensions
    {
        /// <summary>
        /// Creates a <see cref="BlobIdentifier"/> from the stream provided
        /// </summary>
        /// <param name="blob">The content stream against which the identifier should be created</param>
        /// <returns>
        /// A <see cref="BlobIdentifier"/> instance representing the unique identifier for binary the content.
        /// </returns>
        public static BlobIdentifier CalculateBlobIdentifier(this Stream blob)
        {
            blob.Position = 0;
            var result = VsoHash.CalculateBlobIdentifier(blob);
            blob.Position = 0;
            return result;
        }

        /// <summary>
        /// Creates a <see cref="BlobIdentifierWithBlocks"/> from the stream provided
        /// </summary>
        /// <param name="blob">The content stream against which the identifier should be created</param>
        /// <returns>
        /// A <see cref="BlobIdentifierWithBlocks"/> instance representing the unique identifier for binary the content.
        /// </returns>
        public static BlobIdentifierWithBlocks CalculateBlobIdentifierWithBlocks(this Stream blob)
        {
            blob.Position = 0;
            var result = VsoHash.CalculateBlobIdentifierWithBlocks(blob);
            blob.Position = 0;
            return result;
        }

        /// <summary>
        /// Creates a <see cref="BlobIdentifierWithBlocks"/> from the stream provided
        /// </summary>
        /// <param name="blob">The content stream against which the identifier should be created</param>
        /// <returns>
        /// A <see cref="BlobIdentifierWithBlocks"/> instance representing the unique identifier for binary the content.
        /// </returns>
        public static async Task<BlobIdentifierWithBlocks> CalculateBlobIdentifierWithBlocksAsync(this Stream blob)
        {
            blob.Position = 0;
            var result = await VsoHash.CalculateBlobIdentifierWithBlocksAsync(blob).ConfigureAwait(false);
            blob.Position = 0;
            return result;
        }

        /// <summary>
        /// Creates a <see cref="BlobIdentifierWithBlocks"/> from the bytes provided
        /// </summary>
        /// <param name="blob">The content against which the identifier should be created</param>
        /// <returns>
        /// A <see cref="BlobIdentifierWithBlocks"/> instance representing the unique identifier for binary the content.
        /// </returns>
        public static BlobIdentifierWithBlocks CalculateBlobIdentifierWithBlocks(this byte[] blob)
        {
            using (var stream = new MemoryStream(blob))
            {
                return VsoHash.CalculateBlobIdentifierWithBlocks(stream);
            }
        }

        /// <summary>
        /// Creates a <see cref="BlobIdentifier"/> from the stream provided
        /// </summary>
        /// <param name="blob">The content bytes against which the identifier should be created.</param>
        /// <returns>
        /// A <see cref="BlobIdentifier"/> instance representing the unique identifier for binary the content.
        /// </returns>
        public static BlobIdentifier CalculateBlobIdentifier(this byte[] blob)
        {
            return VsoHash.CalculateBlobIdentifier(blob);
        }

        /// <summary>
        /// Converts a ContentStore ContentHash to a BlobIdentifier.
        /// </summary>
        public static BlobIdentifier ToBlobIdentifier(this ContentHash contentHash)
        {
            switch (contentHash.HashType)
            {
                case HashType.Vso0:
                case HashType.Dedup64K:
                case HashType.Dedup1024K:
                case HashType.Murmur:
                    return BlobIdentifier.Deserialize(contentHash.ToHex());
                default:
                    throw new ArgumentException($"ContentHash has unsupported type when converting to BlobIdentifier: {contentHash.HashType}");
            }
        }

        /// <summary>
        /// Converts a BlobIdentifier to its corresponding ContentHash.
        /// </summary>
        public static ContentHash ToContentHash(this BlobIdentifier blobId)
        {
            switch(blobId.AlgorithmId)
            {
                case VsoHash.VsoAlgorithmId:
                    return new ContentHash(HashType.Vso0, blobId.Bytes);
                case ChunkDedupIdentifier.ChunkAlgorithmId:
                    return new ContentHash(HashType.Dedup64K, blobId.Bytes); // TODO: Chunk size optimization
                case (byte)NodeAlgorithmId.Node64K:
                    return new ContentHash(HashType.Dedup64K, blobId.Bytes);
                case (byte)NodeAlgorithmId.Node1024K:
                    return new ContentHash(HashType.Dedup1024K, blobId.Bytes);
                case MurmurHashInfo.MurmurAlgorithmId:
                    return new ContentHash(HashType.Murmur, blobId.Bytes);
                default:
                    throw new ArgumentException($"BlobIdentifier has an unrecognized AlgorithmId: {blobId.AlgorithmId}");
            }
        }
    }
}
