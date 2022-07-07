// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
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
        /// Time interval for nagle RegisterLocation operations against global store
        /// </summary>
        public TimeSpanSetting? GlobalRegisterNagleInterval { get; set; }

        /// <summary>
        /// Degree of parallelism for nagle RegisterLocation operations against global store
        /// </summary>
        public int GlobalRegisterNagleParallelism { get; set; } = 1;

        /// <summary>
        /// Controls delay for RegisterLocation operation to allow for throttling
        /// </summary>
        public TimeSpanSetting? RegisterLocationDelay { get; set; }

        /// <summary>
        /// Controls delay for GetBulk operation to allow for throttling
        /// </summary>
        public TimeSpanSetting? GlobalGetBulkLocationDelay { get; set; }

        /// <summary>
        /// Gets whether BlobContentLocationRegistry is enabled
        /// </summary>
        public bool EnableBlobContentLocationRegistry { get; set; }

        public BlobContentLocationRegistryConfiguration BlobContentLocationRegistrySettings { get; set; } = new BlobContentLocationRegistryConfiguration();
    }

    public record BlobContentLocationRegistryConfiguration()
        : BlobFolderStorageConfiguration(ContainerName: "contentlocations", FolderName: "partitions")
    {
        public string PartitionCheckpointManifestFileName { get; set; } = "manifest.v2.json";

        /// <summary>
        /// Indicates whether partitions are updated in the background on a timer loop
        /// </summary>
        public bool UpdateInBackground { get; set; } = true;

        /// <summary>
        /// Delay after submitting content update for each partition
        /// </summary>
        public TimeSpanSetting PerPartitionDelayInterval { get; set; } = TimeSpan.FromSeconds(0.5);

        /// <summary>
        /// Interval between updates of partitions output blob
        /// </summary>
        public TimeSpanSetting PartitionsUpdateInterval { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Gets whether partitions should be processed into output blobs (i.e. containing sst files and content listings)
        /// </summary>
        public bool ProcessPartitions { get; set; } = true;

        /// <summary>
        /// Maximum number of diff sst snapshots for a particular partition allowed before using full sst snapshot instead.
        /// </summary>
        public int MaxSnapshotChainLength { get; set; } = 5;

        /// <summary>
        /// Maximum number of diff sst snapshots for a particular partition allowed before using full sst snapshot instead.
        /// </summary>
        public int MaxRetainedSnapshots => Math.Max(1, (MaxSnapshotChainLength * 2));

        /// <summary>
        /// Maximum parallelism for sst file download
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = 4;

        /// <summary>
        /// Gets whether the local database should be updated with sst files
        /// </summary>
        public bool UpdateDatabase { get; set; } = false;

        /// <summary>
        /// The number of partitions to create. Changing this number causes partition to be recomputed
        /// </summary>
        public int PartitionCount { get; set; } = 256;
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
