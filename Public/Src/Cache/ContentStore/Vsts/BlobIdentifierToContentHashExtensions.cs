// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    /// Extension methods to convert ContentStore types to VSO Types.
    /// </summary>
    public static class BlobIdentifierToContentHashExtensions
    {
        /// <summary>
        /// Converts a VSO BlobIdentifier to a ContentHash.
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

        /// <summary>
        /// Converts a ContentStore ContentHash to a VSO Blob Identifier.
        /// </summary>
        public static BlobIdentifier ToBlobIdentifier(in this ContentHash contentHash)
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
    }
}
