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

        /// <summary>
        /// When enabled, if a content place operation fails (blob not found or hash mismatch), the associated
        /// content hash list entry (fingerprint) will be deleted from the metadata store, giving subsequent
        /// builds a clean cache miss so the content can be re-produced.
        /// </summary>
        public bool EnableContentRecoveryOnPlaceFailure = false;
    }
}
