// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
            {HashType.DedupSingleChunk, DedupSingleChunkHashInfo.Instance},
            {HashType.Dedup64K, DedupNode64KHashInfo.Instance},
            {HashType.Dedup1024K, Dedup1024KHashInfo.Instance},
            {HashType.DeprecatedVso0, VsoHashInfo.Instance},
            {HashType.Murmur, MurmurHashInfo.Instance},
        };

        /// <summary>
        ///     Create a HashInfo instance from HashType.
        /// </summary>
        public static HashInfo Find(HashType hashType)
        {
            var hit = HashInfoByType.TryGetValue(hashType, out var hashInfo);
            if (!hit) {throw new NotImplementedException($"Invalid HashType passed for HashInfoLookup: {hashType.Serialize()}, hashCode: {hashType.GetHashCode()}");}
#pragma warning disable CS8603 // Possible null reference return.
            return hashInfo;
#pragma warning restore CS8603 // Possible null reference return.
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

        /// <summary>
        ///     The maximum number of unused idle ContentHashers to be kept in reserve for future use.
        /// </summary>
        /// <remarks>
        ///     -1 (default) means the maximum number of idle hashers is unbounded.
        ///     Note: This does not limit the maximum number of ContentHashers that can be pooled.
        /// </remarks>
        public static int ContentHasherIdlePoolSize = -1;
    }
}
