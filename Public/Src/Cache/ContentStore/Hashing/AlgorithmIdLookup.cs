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
        private static readonly IReadOnlyDictionary<HashType, byte> AlgorithmIdByHashType = new Dictionary<HashType, byte>
            {
                {HashType.Vso0, VsoHash.VsoAlgorithmId},
                {HashType.DedupSingleChunk, ChunkDedupIdentifier.ChunkAlgorithmId},
                {HashType.DedupNode, (byte)NodeAlgorithmId.Node64K},
                {HashType.Dedup64K, (byte)NodeAlgorithmId.Node64K},
                {HashType.Dedup1024K, (byte)NodeAlgorithmId.Node1024K},
                {HashType.Murmur, MurmurHashInfo.MurmurAlgorithmId}
            };

        /// <summary>
        ///     Retrieve algorithm id.
        /// </summary>
        public static byte Find(HashType hashType)
        {
            Contract.Assert(AlgorithmIdByHashType.ContainsKey(hashType));
            return AlgorithmIdByHashType[hashType];
        }
    }

    /// <nodoc />
    public static class AlgorithmIdHelpers
    {
        /// <nodoc />
        public static bool IsHashTagValid(ContentHash contentHash)
        {
            var hashTag = contentHash[contentHash.ByteLength-1];

            return contentHash.HashType switch
            {
                HashType.Vso0 => hashTag == AlgorithmIdLookup.Find(HashType.Vso0),
                HashType.Dedup64K => hashTag == AlgorithmIdLookup.Find(HashType.DedupNode) ||
                                     hashTag == AlgorithmIdLookup.Find(HashType.DedupSingleChunk),
                HashType.Dedup1024K => hashTag == AlgorithmIdLookup.Find(HashType.Dedup1024K) ||
                                       hashTag == AlgorithmIdLookup.Find(HashType.DedupSingleChunk),
                HashType.Murmur => hashTag == AlgorithmIdLookup.Find(HashType.Murmur),
                _ => throw new ArgumentException($"{contentHash.HashType} is not a tagged hash.")
            };
        }
    }
}
