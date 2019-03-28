// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Factory that creates instances of HashInfo.
    /// </summary>
    public static class HashInfoLookup
    {
        private static readonly IReadOnlyDictionary<HashType, HashInfo> HashInfoByType = new Dictionary<HashType, HashInfo>
            {
                {HashType.SHA1, SHA1HashInfo.Instance},
                {HashType.SHA256, SHA256HashInfo.Instance},
                {HashType.MD5, MD5HashInfo.Instance},
                {HashType.Vso0, VsoHashInfo.Instance},
                {HashType.DedupChunk, DedupChunkHashInfo.Instance},
                {HashType.DedupNode, DedupNodeHashInfo.Instance},
                {HashType.DedupNodeOrChunk, DedupNodeOrChunkHashInfo.Instance},
                {HashType.DeprecatedVso0, VsoHashInfo.Instance}
            };

        /// <summary>
        ///     Create a HashInfo instance from HashType.
        /// </summary>
        public static HashInfo Find(HashType hashType)
        {
            Contract.Assert(HashInfoByType.ContainsKey(hashType));
            return HashInfoByType[hashType];
        }

        /// <summary>
        ///     Return HashInfo for all known hashes.
        /// </summary>
        public static IEnumerable<HashInfo> All()
        {
            return HashInfoByType.Values;
        }

        /// <summary>
        ///     Construct content hashers for all known hash types.
        /// </summary>
        public static Dictionary<HashType, IContentHasher> CreateAll()
        {
            return HashInfoByType.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.CreateContentHasher());
        }
    }
}
