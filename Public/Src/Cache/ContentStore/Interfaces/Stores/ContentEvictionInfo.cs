// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    /// A wrapper for content hashes used by eviction logic.
    /// </summary>
    public readonly struct ContentEvictionInfo
    {
        /// <nodoc />
        public ContentHash ContentHash { get; }

        /// <nodoc/>
        public int ReplicaCount { get; }

        /// <nodoc/>
        public long Size { get; }

        /// <summary>
        /// Cost of the content that affects an eviction order for the content within the same age bucket.
        /// </summary>
        public long Cost => Size * ReplicaCount;

        /// <summary>
        /// Indicates whether this replica is considered important and thus retention should be prioritized
        /// </summary>
        public bool IsImportantReplica { get; }

        /// <summary>
        /// Age of the content based on the last access time.
        /// </summary>
        /// <remarks>
        /// The last access time is the file last access time or the "distributed" last access time obtained from local location store.
        /// </remarks>
        public TimeSpan Age { get; }

        /// <summary>
        /// An effective age of the content that is computed based on content importance or other metrics like content evictability.
        /// </summary>
        public TimeSpan EffectiveAge { get; }

        /// <nodoc />
        public ContentEvictionInfo(
            ContentHash contentHash,
            TimeSpan age,
            TimeSpan effectiveAge,
            int replicaCount,
            long size,
            bool isImportantReplica)
        {
            ContentHash = contentHash;
            Age = age;
            EffectiveAge = effectiveAge;
            ReplicaCount = replicaCount;
            Size = size;
            IsImportantReplica = isImportantReplica;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={ContentHash.ToShortString()} Age={Age} EffectiveAge={EffectiveAge} Cost={ReplicaCount}*{Size} IsImportantReplica={IsImportantReplica}]";
        }

        /// <summary>
        /// Object comparer for <see cref="ContentEvictionInfo"/>.
        /// </summary>
        /// <remarks>
        /// The comparison is done by EffectiveAge, then by Importance and then by Cost.
        /// When 'UseTieredDistributedEviction' configuration option is true, then EffectiveAge property is "rounded" to buckets boundaries.
        /// For instance, all the content that is younger then 30 minutes old would have an EffectiveAge equals to 30min.
        /// This allows us to sort by EffectiveAge and if it is the same, sort by some other properties (like importance).
        /// </remarks>
        public class AgeBucketingPrecedenceComparer : IComparer<ContentEvictionInfo>
        {
            /// <nodoc />
            public static readonly AgeBucketingPrecedenceComparer Instance = new AgeBucketingPrecedenceComparer();

            /// <inheritdoc />
            public int Compare(ContentEvictionInfo x, ContentEvictionInfo y)
            {
                if (!CollectionUtilities.IsCompareEquals(x.EffectiveAge, y.EffectiveAge, out var compareResult, greatestFirst: true)
                    || !CollectionUtilities.IsCompareEquals(x.IsImportantReplica, y.IsImportantReplica, out compareResult)
                    || !CollectionUtilities.IsCompareEquals(x.Cost, y.Cost, out compareResult, greatestFirst: true))
                {
                    return compareResult;
                }

                return 0;
            }
        }
    }
}
