// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Configuration type for <see cref="IContentMetadataStore"/> family of types.
    /// </summary>
    public record ContentMetadataStoreConfiguration
    {
       
    }

    /// <summary>
    /// Configuration for remote client content metadata store
    /// </summary
    public record ClientContentMetadataStoreConfiguration(int Port) : ContentMetadataStoreConfiguration
    {
        /// <summary>
        /// The amount of time to wait for a connection to be established
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>
        /// The amount of time to wait for an operation to complete
        /// </summary>
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Configuration for in-memory shared content metadata store
    /// </summary>
    public record MemoryContentMetadataStoreConfiguration : ContentMetadataStoreConfiguration
    {
        /// <nodoc />
        public MemoryContentMetadataStoreConfiguration(IContentMetadataStore store)
        {
            Store = store;
        }

        /// <summary>
        /// In-memory shared content metadata store
        /// </summary>
        public IContentMetadataStore Store { get; }
    }
}
