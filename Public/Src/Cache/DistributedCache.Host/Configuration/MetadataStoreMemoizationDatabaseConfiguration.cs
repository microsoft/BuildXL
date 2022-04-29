// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Configuration for MetadataStoreMemoizationDatabase
    /// </summary>
    public record MetadataStoreMemoizationDatabaseConfiguration
    {
        /// <summary>
        /// The maximum size over which metadata entries are stored in central storage
        /// </summary>
        public int StorageMetadataEntrySizeThreshold = int.MaxValue;
    }
}
