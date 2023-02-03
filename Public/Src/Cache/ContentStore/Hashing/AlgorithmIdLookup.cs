// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Mapping for hash types and their respective algorithm IDs.
    /// </summary>
    public static class AlgorithmIdLookup
    {
        /// <summary>
        ///     Retrieve algorithm id.
        /// </summary>
        public static byte Find(HashType hashType)
        {
            return hashType switch
            {
                HashType.Vso0 => (byte)AlgorithmId.File,
                HashType.DedupSingleChunk => (byte)AlgorithmId.Chunk,
                HashType.DedupNode => (byte)AlgorithmId.Node,
                HashType.Dedup64K => (byte)AlgorithmId.Node,
                HashType.Dedup1024K => (byte)AlgorithmId.Node,
                HashType.Murmur => MurmurHashInfo.MurmurAlgorithmId,
                _ => throw new ArgumentException($"{hashType} is not a tagged hash.")
            };
        }
    }

    /// <nodoc />
    public static class AlgorithmIdHelpers
    {
        /// <nodoc />
        public static bool IsHashTagValid(ContentHash contentHash)
        {
            byte hashTag = contentHash[contentHash.ByteLength-1];

            return contentHash.HashType switch
            {
                HashType.Vso0 => hashTag == (byte)AlgorithmId.File,
                HashType.Dedup64K => hashTag == (byte)AlgorithmId.Node ||
                                     hashTag == (byte)AlgorithmId.Chunk,
                HashType.Dedup1024K => hashTag == (byte)AlgorithmId.Node ||
                                       hashTag == (byte)AlgorithmId.Chunk,
                HashType.Murmur => hashTag == (byte)MurmurHashInfo.MurmurAlgorithmId,
                _ => throw new ArgumentException($"{contentHash.HashType} is not a tagged hash.")
            };
        }
    }
}
