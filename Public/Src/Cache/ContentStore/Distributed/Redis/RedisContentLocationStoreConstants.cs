// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// Acceptable time difference between local last access time and content tracker's last access time.
        /// </summary>
        internal static readonly TimeSpan TargetRange = TimeSpan.FromMinutes(10);

        internal const int BytesInFileSize = sizeof(long);
    }
}
