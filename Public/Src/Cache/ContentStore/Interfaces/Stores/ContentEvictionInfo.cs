// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using static BuildXL.Cache.ContentStore.UtilitiesCore.Internal.CollectionUtilities;

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
        public ReplicaRank Rank { get; }

        /// <summary>
        /// Age of the content based on the last access time.
        /// </summary>
        /// <remarks>
        /// The last access time is the file last access time or the "distributed" last access time obtained from local location store.
        /// </remarks>
        public TimeSpan Age { get; }

        /// <summary>
        /// Age of content based on the local last access time
        /// </summary>
        public TimeSpan LocalAge { get; }

        /// <summary>
        /// An effective age of the content that is computed based on content importance or other metrics like content evictability.
        /// </summary>
        public TimeSpan EffectiveAge { get; }

        /// <summary>
        /// Age used by full sort eviction (it is the minimum of effective age (<see cref="EffectiveAge"/>) and distributed age (<see cref="Age"/>))
        /// </summary>
        public TimeSpan FullSortAge => EffectiveAge < Age ? EffectiveAge : Age;

        /// <nodoc />
        public ContentEvictionInfo(
            ContentHash contentHash,
            TimeSpan age,
            TimeSpan localAge,
            TimeSpan effectiveAge,
            int replicaCount,
            long size,
            ReplicaRank rank)
        {
            ContentHash = contentHash;
            Age = age;
            LocalAge = localAge;
            EffectiveAge = effectiveAge;
            ReplicaCount = replicaCount;
            Size = size;
            Rank = rank;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={ContentHash.ToShortString()} Age={Age} EffectiveAge={EffectiveAge} LocalAge={LocalAge} Cost={ReplicaCount}*{Size} Rank={Rank}]";
        }

        /// <summary>
        /// Compares two ages returning the eviction order (oldest/greatest age first). If reverse=true,
        /// the opposite order is returned.
        /// </summary>
        public static OrderResult OrderAges(TimeSpan age1, TimeSpan age2, bool reverse)
        {
            return Order(age1, age2, greatestFirst: !reverse);
        }

        /// <nodoc />
        public static readonly IComparer<ContentEvictionInfo> FullSortAgeOnlyComparer = Comparer<ContentEvictionInfo>.Create((c1, c2) => (int)OrderAges(c1.FullSortAge, c2.FullSortAge, reverse: false));

        /// <nodoc />
        public static readonly IComparer<ContentEvictionInfo> ReverseFullSortAgeOnlyComparer = Comparer<ContentEvictionInfo>.Create((c1, c2) => (int)OrderAges(c1.FullSortAge, c2.FullSortAge, reverse: true));

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

            /// <nodoc />
            public static readonly IComparer<ContentEvictionInfo> ReverseInstance = Comparer<ContentEvictionInfo>.Create((c1, c2) => Instance.Compare(c2, c1));

            /// <inheritdoc />
            public int Compare(ContentEvictionInfo x, ContentEvictionInfo y)
            {
                if (!CollectionUtilities.IsCompareEquals(x.EffectiveAge, y.EffectiveAge, out var compareResult, greatestFirst: true)
                    || !CollectionUtilities.IsCompareEquals((int)x.Rank, (int)y.Rank, out compareResult)
                    || !CollectionUtilities.IsCompareEquals(x.Cost, y.Cost, out compareResult, greatestFirst: true))
                {
                    return compareResult;
                }

                return 0;
            }
        }
    }
}
