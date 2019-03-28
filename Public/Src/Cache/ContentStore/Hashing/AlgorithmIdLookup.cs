// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
                {HashType.DedupChunk, ChunkDedupIdentifier.ChunkAlgorithmId},
                {HashType.DedupNode, NodeDedupIdentifier.NodeAlgorithmId},

                // DedupNodeOrChunk will always end with DedupChunk or DedupNode algorithm IDs. Default to DedupChunk.
                {HashType.DedupNodeOrChunk, ChunkDedupIdentifier.ChunkAlgorithmId}
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
            bool isValid = true;
            var hashTag = contentHash[contentHash.ByteLength-1];

            switch (contentHash.HashType)
            {
                case HashType.Vso0:
                    isValid = hashTag == AlgorithmIdLookup.Find(HashType.Vso0);
                    break;
                case HashType.DedupNodeOrChunk:
                    isValid = hashTag == AlgorithmIdLookup.Find(HashType.DedupNode) || hashTag == AlgorithmIdLookup.Find(HashType.DedupChunk);
                    break;
                default:
                    throw new ArgumentException($"{contentHash.HashType} is not a tagged hash.");
            }

            return isValid;
        }
    }
}
