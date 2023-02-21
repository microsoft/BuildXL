// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;

#nullable disable
#nullable enable annotations

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Pass through settings used by local location store. Settings specified here
    /// can appear directly in configuration file and be used in LocalLocationStore without
    /// the need to do explicit passing through configuration layers.
    /// </summary>
    public record LocalLocationStoreSettings
    {
        /// <summary>
        /// The number of eviction partitions. Used together with <see cref="EvictionPartitionInterval"/>.
        /// Time is split into ranges of duration <see cref="EvictionPartitionInterval"/>, during which
        /// only one partition (hash range) is preferred for eviction during a time range.
        /// </summary>
        public int EvictionPartitionCount { get; set; } = 8;

        /// <summary>
        /// The length of the time interval where a particular eviction partition is preferred.
        /// When set to zero, use of preferred eviction partition is disabled.
        /// Used together with <see cref="EvictionPartitionCount"/>.
        /// </summary>
        public TimeSpanSetting EvictionPartitionInterval { get; set; }

        /// <summary>
        /// The fraction of content which should use the preferred eviction partition. For instance,
        /// suppose the total amount of content is 1000 items equally divided amount 4 partitions
        /// (with <see cref="EvictionPartitionCount"/> == 4). Also, let <see cref="EvictionPartitionFraction"/> be 0.5.
        /// First only the content in the preferred eviction partition among the first 1000 * 0.5 = 500 items will be
        /// returned when getting hashes in eviction order. Next, the remaining content will be returned in eviction order.
        /// </summary>
        public double EvictionPartitionFraction { get; set; } = 0.5;

        /// <summary>
        /// The number of buckets to offset by for important replicas. This effectively makes important replicas look younger.
        /// </summary>
        public int ImportantReplicaBucketOffset { get; set; } = 2;

        /// <summary>
        /// Time interval for nagle RegisterLocation operations against global store
        /// </summary>
        public TimeSpanSetting? GlobalRegisterNagleInterval { get; set; }

        /// <summary>
        /// Degree of parallelism for nagle RegisterLocation operations against global store
        /// </summary>
        public int GlobalRegisterNagleParallelism { get; set; } = 1;

        /// <summary>
        /// Batch size for nagle RegisterLocation operations against global store
        /// </summary>
        public int GlobalRegisterNagleBatchSize { get; set; } = 100;

        /// <summary>
        /// Controls delay for RegisterLocation operation to allow for throttling
        /// </summary>
        public TimeSpanSetting? RegisterLocationDelay { get; set; }

        /// <summary>
        /// Controls delay for GetBulk operation to allow for throttling
        /// </summary>
        public TimeSpanSetting? GlobalGetBulkLocationDelay { get; set; }
    }

    [DataContract]
    public record BlobFolderStorageConfiguration(string ContainerName = "", string FolderName = "")
    {
        [JsonIgnore]
        public AzureBlobStorageCredentials? Credentials { get; set; }

        public RetryPolicyConfiguration RetryPolicy { get; set; } = DefaultRetryPolicy;

        public string ContainerName { get; set; } = ContainerName;

        public string FolderName { get; set; } = FolderName;

        public TimeSpanSetting StorageInteractionTimeout { get; set; } = TimeSpan.FromMinutes(1);

        public static RetryPolicyConfiguration DefaultRetryPolicy { get; } = new RetryPolicyConfiguration()
        {
            RetryPolicy = StandardRetryPolicy.ExponentialSpread,
            MinimumRetryWindow = TimeSpan.FromMilliseconds(1),
            MaximumRetryWindow = TimeSpan.FromSeconds(30),
            WindowJitter = 1.0,
        };
    }
}
