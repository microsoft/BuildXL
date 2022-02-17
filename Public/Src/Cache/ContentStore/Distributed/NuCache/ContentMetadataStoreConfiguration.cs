// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Configuration for remote client content metadata store
    /// </summary
    public record ClientContentMetadataStoreConfiguration
    {
        /// <summary>
        /// The amount of time to wait for an operation to complete
        /// </summary>
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(20);

        /// <summary>
        /// Whether the server-side supports blob operations
        /// </summary>
        public bool AreBlobsSupported { get; set; } = true;

        /// <summary>
        /// Minimum wait time between retries
        /// </summary>
        public TimeSpan RetryMinimumWaitTime { get; set; } = TimeSpan.FromMilliseconds(5);

        /// <summary>
        /// Maximum wait time between retries
        /// </summary>
        public TimeSpan RetryMaximumWaitTime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Average wait time factor between retries
        /// </summary>
        public TimeSpan RetryDelta { get; set; } = TimeSpan.FromSeconds(1);
    }
}
