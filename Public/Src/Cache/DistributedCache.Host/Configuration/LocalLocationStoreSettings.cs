// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

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

        public BlobContentLocationRegistrySettings BlobContentLocationRegistrySettings { get; }
    }

    public record BlobContentLocationRegistrySettings
    {
        public TimeSpanSetting StorageInteractionTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public string ContainerName { get; set; } = "contentlocations";

        public string FolderName { get; set; } = "partitions";

        public string PartitionCheckpointManifestFileName { get; set; } = "manifest.json";

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
        public TimeSpanSetting PartitionsUpdateInterval { get; set; } = TimeSpan.FromMinutes(5);
    }
}
