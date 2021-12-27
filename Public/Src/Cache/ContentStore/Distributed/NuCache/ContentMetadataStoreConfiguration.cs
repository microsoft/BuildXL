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
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Whether the server-side supports blob operations
        /// </summary>
        public bool AreBlobsSupported { get; set; } = true;

    }
}
