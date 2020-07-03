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
                case BuildXL.Cache.ContentStore.Hashing.NodeDedupIdentifier.NodeAlgorithmId:
                case ChunkDedupIdentifier.ChunkAlgorithmId:
                    return new ContentHash(HashType.DedupNodeOrChunk, blobId.Bytes);
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
                case HashType.DedupNodeOrChunk:
                case HashType.Murmur:
                    return BlobIdentifier.Deserialize(contentHash.ToHex());
                default:
                    throw new ArgumentException($"ContentHash has unsupported type when converting to BlobIdentifier: {contentHash.HashType}");
            }
        }
    }
}
