// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;

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

        /// <summary>
        ///  See BuildXL.Cache.MemoizationStoreAdapter.BlobCacheFactory.Config.RetentionPolicyInDays
        /// </summary>
        public TimeSpan? RetentionPolicy = null;

        /// <summary>
        /// For testing only. Disables preventing pinning, making <see cref="RetentionPolicy"/> irrelevant.
        /// </summary>
        public bool DisablePreventivePinning = false;
    }
}
