// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Hashing;

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
        public readonly ContentHash ContentHash;

        /// <summary>
        /// The original last-access time.
        /// </summary>
        public readonly DateTime LastAccessTime;

        /// <summary>
        /// Number of replicas content exists at in the datacenter.
        /// </summary>
        public readonly long ReplicaCount;

        /// <summary>
        /// Whether or not the content is evictable as determined by the datacenter.
        /// </summary>
        public readonly bool SafeToEvict;

        /// <summary>
        /// The effective last access time of the content
        /// </summary>
        public readonly DateTime? EffectiveLastAccessTime;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentHashWithLastAccessTimeAndReplicaCount"/> struct.
        /// </summary>
        public ContentHashWithLastAccessTimeAndReplicaCount(ContentHash contentHash, DateTime lastAccessTime, long replicaCount = 1, bool safeToEvict = false, DateTime? effectiveLastAccessTime = null)
        {
            ContentHash = contentHash;
            LastAccessTime = lastAccessTime;
            ReplicaCount = replicaCount;
            SafeToEvict = safeToEvict;
            EffectiveLastAccessTime = effectiveLastAccessTime;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={ContentHash} LastAccessTime={LastAccessTime} EffectiveLastAccessTime={EffectiveLastAccessTime} ReplicaCount={ReplicaCount}]";
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
