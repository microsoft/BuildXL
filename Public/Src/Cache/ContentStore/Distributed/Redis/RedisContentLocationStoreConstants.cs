// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    internal static class RedisContentLocationStoreConstants
    {
        /// <summary>
        /// Number of queries to send to Redis at a time.
        /// </summary>
        internal const int DefaultBatchSize = 500;

        /// <summary>
        /// Bitmask used to determine location ID.
        /// </summary>
        internal const byte MaxCharBitMask = 0x80;

        /// <summary>
        /// Prefix for content location id which maps to content location data
        /// </summary>
        internal const string ContentLocationIdPrefix = "LocationId:";

        /// <summary>
        /// Prefix for content location name where content location hash maps to id
        /// </summary>
        internal const string ContentLocationKeyPrefix = "LocationKey:";

        /// <summary>
        /// Counter for assigning content location id
        /// </summary>
        internal const string MaxContentLocationId = "MaxLocationId";

        #region Distributed Eviction
        /// <summary>
        /// The default number of minimum replicas required to safe evict during distributed eviction if last-access times are in sync.
        /// </summary>
        internal const int DefaultMinReplicaCountToSafeEvict = 4;

        /// <summary>
        /// The default number of minimum replicas required to immediately evict content during distributed eviction.
        /// </summary>
        internal const int DefaultMinReplicaCountToImmediateEvict = 50;

        /// <summary>
        /// Acceptable time difference between local last access time and content tracker's last access time.
        /// </summary>
        internal static readonly TimeSpan TargetRange = TimeSpan.FromMinutes(1);

        #endregion

        #region Identity Updates
        internal const string HeartbeatName = "BackgroundIdentityUpdate";
        internal const int HeartbeatIntervalInMinutes = 1;
        #endregion

        internal const int BytesInFileSize = sizeof(long);
        internal const int BitsInFileSize = BytesInFileSize * 8;
        public static TimeSpan BatchInterval = TimeSpan.FromMinutes(1);

        public const int BatchDegreeOfParallelism = 4;
    }
}
