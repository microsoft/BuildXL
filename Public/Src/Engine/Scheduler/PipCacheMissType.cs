// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Mutually exclusive reasons for pip cache misses.
    /// These are 1:1 correlated with cache miss counters <see cref="PipExecutorCounter"/>. If those counters
    /// are deprecated, then this can become the main enum for cache misses. 
    /// </summary>
    public enum PipCacheMissType : byte
    {
        /// <summary>
        /// default
        /// </summary>
        Invalid,

        /// <summary>
        /// Number of times a process pip cache descriptor was not usable due to mismatched strong fingerprints
        /// </summary>
        MissForDescriptorsDueToStrongFingerprints = PipExecutorCounter.CacheMissesForDescriptorsDueToStrongFingerprints,

        /// <summary>
        /// Number of times a process pip cache entry was not found (no prior execution information).
        /// </summary>
        MissForDescriptorsDueToWeakFingerprints = PipExecutorCounter.CacheMissesForDescriptorsDueToWeakFingerprints,

        /// <summary>
        /// Number of times a process pip cache entry was not found (no prior execution information).
        /// </summary>
        CacheMissesForDescriptorsDueToAugmentedWeakFingerprints = PipExecutorCounter.CacheMissesForDescriptorsDueToAugmentedWeakFingerprints,

        /// <summary>
        /// Number of times a process pip was forced to be a cache miss (despite finding a descriptor) due to artifial cache miss injection.
        /// </summary>
        MissForDescriptorsDueToArtificialMissOptions = PipExecutorCounter.CacheMissesForDescriptorsDueToArtificialMissOptions,

        /// <summary>
        /// Numter of times strong fingerprint match was found but the corresponding <see cref="BuildXL.Engine.Cache.Fingerprints.CacheEntry"/> was not retrievable
        /// </summary>
        MissForCacheEntry = PipExecutorCounter.CacheMissesForCacheEntry,

        /// <summary>
        /// Number of times a process pip cache descriptor was found, but was invalid
        /// </summary>
        MissDueToInvalidDescriptors = PipExecutorCounter.CacheMissesDueToInvalidDescriptors,

        /// <summary>
        /// Number of times a process pip cache descriptor was found but the metadata was not retrievable
        /// </summary>
        MissForProcessMetadata = PipExecutorCounter.CacheMissesForProcessMetadata,

        /// <summary>
        /// Number of times a process pip cache descriptor was found but the metadata was not retrievable
        /// </summary>
        MissForProcessMetadataFromHistoricMetadata = PipExecutorCounter.CacheMissesForProcessMetadataFromHistoricMetadata,

        /// <summary>
        /// Number of times a process pip cache descriptor was found, but the referenced output content was not available when needed.
        /// The cache descriptor has been counted as a part of <see cref="PipExecutorCounter.CacheHitsForProcessPipDescriptors"/>.
        /// </summary>
        MissForProcessOutputContent = PipExecutorCounter.CacheMissesForProcessOutputContent,

        /// <summary>
        /// Number of times a process pip cache descriptor was found, and the output content was available when needed.
        /// The cache descriptor has been counted as a part of <see cref="PipExecutorCounter.CacheHitsForProcessPipDescriptors"/>.
        /// </summary>
        Hit = PipExecutorCounter.CacheHitsForProcessPipDescriptors,

        /// <summary>
        /// Number of times a process pip was a miss due to being configured to always miss on cache lookup.
        /// </summary>
        MissForProcessConfiguredUncacheable = PipExecutorCounter.CacheMissesForProcessConfiguredUncacheable,
    }
}
