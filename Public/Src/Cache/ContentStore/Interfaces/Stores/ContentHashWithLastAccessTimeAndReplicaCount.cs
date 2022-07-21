// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Time;

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    /// A wrapper for content hashes and its last-access time.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ContentHashWithLastAccessTimeAndReplicaCount
    {
        /// <summary>
        /// Content hash.
        /// </summary>
        public readonly ContentHash ContentHash => EvictionInfo.ContentHash;

        /// <summary>
        /// The original last-access time.
        /// </summary>
        public readonly DateTime LastAccessTime => EvictionInfo.TimestampUtc - EvictionInfo.Age;

        /// <summary>
        /// Number of replicas content exists at in the datacenter.
        /// </summary>
        public long ReplicaCount => EvictionInfo.ReplicaCount;

        /// <summary>
        /// Whether or not the content is evictable as determined by the datacenter.
        /// </summary>
        public readonly bool SafeToEvict;

        /// <summary>
        /// The effective last access time of the content
        /// </summary>
        public readonly DateTime? EffectiveLastAccessTime => EvictionInfo.TimestampUtc - EvictionInfo.EffectiveAge;

        /// <summary>
        /// The eviction info
        /// </summary>
        public readonly ContentEvictionInfo EvictionInfo;

        /// <nodoc />
        public TimeSpan Age(IClock clock) => clock.UtcNow - LastAccessTime;

        /// <nodoc />
        public TimeSpan? EffectiveAge(IClock clock) => EffectiveLastAccessTime == null ? (TimeSpan?)null : clock.UtcNow - EffectiveLastAccessTime.Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentHashWithLastAccessTimeAndReplicaCount"/> struct.
        /// </summary>
        public ContentHashWithLastAccessTimeAndReplicaCount(
            ContentHash contentHash,
            DateTime lastAccessTime,
            int replicaCount = 1,
            bool safeToEvict = false,
            DateTime? effectiveLastAccessTime = null)
            : this(CreateInfo(contentHash, lastAccessTime, replicaCount, effectiveLastAccessTime))
        {
            SafeToEvict = safeToEvict;
        }

        private static ContentEvictionInfo CreateInfo(ContentHash contentHash, DateTime lastAccessTime, int replicaCount, DateTime? effectiveLastAccessTime)
        {
            var now = DateTime.UtcNow;
            return new ContentEvictionInfo(
                contentHash,
                age: now - lastAccessTime,
                localAge: now - lastAccessTime,
                effectiveAge: now - (effectiveLastAccessTime ?? lastAccessTime),
                replicaCount: replicaCount,
                size: -1,
                rank: ReplicaRank.None,
                timestampUtc: now);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentHashWithLastAccessTimeAndReplicaCount"/> struct.
        /// </summary>
        public ContentHashWithLastAccessTimeAndReplicaCount(ContentEvictionInfo evictionInfo)
        {
            EvictionInfo = evictionInfo;
            SafeToEvict = true;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={ContentHash.ToShortString()} LastAccessTime={LastAccessTime} EffectiveLastAccessTime={EffectiveLastAccessTime} ReplicaCount={ReplicaCount}]";
        }

        /// <summary>
        /// Object comprarer for last-access times.
        /// </summary>
        public class ByLastAccessTime : IComparer<ContentHashWithLastAccessTimeAndReplicaCount>
        {
            private readonly int _replicaCreditInMinutes;

            /// <summary>
            /// Initializes a new instance of the <see cref="ByLastAccessTime"/> class.
            /// </summary>
            public ByLastAccessTime(int replicaCreditInMinutes)
            {
                _replicaCreditInMinutes = replicaCreditInMinutes;
            }

            /// <summary>
            /// Returns the effective last-access time.
            /// </summary>
            public DateTime GetEffectiveLastAccessTime(ContentHashWithLastAccessTimeAndReplicaCount hashInfo)
            {
                if (hashInfo.EffectiveLastAccessTime != null)
                {
                    return hashInfo.EffectiveLastAccessTime.Value;
                }

                if (hashInfo.ReplicaCount <= 0)
                {
                    return DateTime.MinValue;
                }

                if (hashInfo.ReplicaCount == 1)
                {
                    return hashInfo.LastAccessTime;
                }

                var totalCredit = TimeSpan.FromMinutes(_replicaCreditInMinutes * (hashInfo.ReplicaCount - 1));
                return hashInfo.LastAccessTime.Subtract(totalCredit);
            }

            /// <inheritdoc />
            public int Compare(ContentHashWithLastAccessTimeAndReplicaCount x, ContentHashWithLastAccessTimeAndReplicaCount y)
            {
                var xLastAccessTime = GetEffectiveLastAccessTime(x);
                var yLastAccessTime = GetEffectiveLastAccessTime(y);
                return xLastAccessTime.CompareTo(yLastAccessTime);
            }
        }
    }
}
