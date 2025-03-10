// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.Blob;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    public class BlobQuotaKeeperConfig
    {
        public required TimeSpan LastAccessTimeDeletionThreshold { get; init; }
        public required List<GarbageCollectionNamespaceConfig> Namespaces { get; init; }
        public double LruEnumerationPercentileStep { get; init; } = 0.05;
        public int LruEnumerationBatchSize { get; init; } = 1000;

        /// <summary>
        /// Page size for consuming the Azure change feed. If null, it will use the Azure-defined default value.
        /// </summary>
        public int? ChangeFeedPageSize { get; init; } = null;

        /// <summary>
        /// Specifies the retention period for the change feed.
        /// </summary>
        public TimeSpan ChangeFeedRetentionPeriod { get; set; } = TimeSpan.FromDays(14);

        /// <summary>
        /// Specifies how often to create checkpoints while consuming the Azure Storage change feed.
        /// </summary>
        /// <remarks>
        /// Creating a checkpoint takes a while. We don't want to do it too often, because we are forced to stop
        /// processing events for the duration of the checkpoint creation. We'll call it fine to loose 30m of progress
        /// in a crash case.
        /// </remarks>
        public TimeSpan CheckpointCreationInterval { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Specifies how old an untracked namespace should be before we delete it. Set to null to disable the feature.
        /// </summary>
        public TimeSpan? UntrackedNamespaceDeletionThreshold { get; set; } = TimeSpan.FromDays(2);

        /// <summary>
        /// Retry policy for Azure Storage client.
        /// </summary>
        public ShardedBlobCacheTopology.BlobRetryPolicy BlobRetryPolicy { get; set; } = new();
    }

    public record GarbageCollectionNamespaceConfig(string Universe, string Namespace, double MaxSizeGb);
}
