// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Mutually exclusive reasons for pip cache misses.
    /// </summary>
    public enum PipCacheMissType : byte
    {
        /// <summary>
        /// Default
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// Number of times a process pip cache descriptor was not usable due to mismatched strong fingerprints
        /// </summary>
        MissForDescriptorsDueToStrongFingerprints,

        /// <summary>
        /// Number of times a process pip cache entry was not found (no prior execution information).
        /// </summary>
        MissForDescriptorsDueToWeakFingerprints,

        /// <summary>
        /// Number of times a process pip cache entry was not found (no prior execution information).
        /// </summary>
        MissForDescriptorsDueToAugmentedWeakFingerprints,

        /// <summary>
        /// Number of times a process pip was forced to be a cache miss (despite finding a descriptor) due to artifial cache miss injection.
        /// </summary>
        MissForDescriptorsDueToArtificialMissOptions,

        /// <summary>
        /// Numter of times strong fingerprint match was found but the corresponding <see cref="BuildXL.Engine.Cache.Fingerprints.CacheEntry"/> was not retrievable
        /// </summary>
        MissForCacheEntry,

        /// <summary>
        /// Number of times a process pip cache descriptor was found, but was invalid
        /// </summary>
        MissDueToInvalidDescriptors,

        /// <summary>
        /// Number of times a process pip cache descriptor was found but the metadata was not retrievable
        /// </summary>
        MissForProcessMetadata,

        /// <summary>
        /// Number of times a process pip cache descriptor was found but the metadata was not retrievable
        /// </summary>
        MissForProcessMetadataFromHistoricMetadata,

        /// <summary>
        /// Number of times a process pip cache descriptor was found, but the referenced output content was not available when needed.
        /// The cache descriptor has been counted as a part of <see cref="PipExecutorCounter.CacheHitsForProcessPipDescriptors"/>.
        /// </summary>
        MissForProcessOutputContent,

        /// <summary>
        /// Number of times a process pip cache descriptor was found, and the output content was available when needed.
        /// The cache descriptor has been counted as a part of <see cref="PipExecutorCounter.CacheHitsForProcessPipDescriptors"/>.
        /// </summary>
        Hit,

        /// <summary>
        /// Number of times a process pip was a miss due to being configured to always miss on cache lookup.
        /// </summary>
        MissForProcessConfiguredUncacheable,
    }

    /// <summary>
    /// Extensions for <see cref="PipCacheMissType"/>.
    /// </summary>
    public static class PipCacheMissTypeExtensions
    {
        /// <summary>
        /// Gets the counter associated with the cache miss type.
        /// </summary>
        public static PipExecutorCounter ToCounter(this PipCacheMissType type) => type switch
        {
            PipCacheMissType.MissForDescriptorsDueToStrongFingerprints => PipExecutorCounter.CacheMissesForDescriptorsDueToStrongFingerprints,
            PipCacheMissType.MissForDescriptorsDueToWeakFingerprints => PipExecutorCounter.CacheMissesForDescriptorsDueToWeakFingerprints,
            PipCacheMissType.MissForDescriptorsDueToAugmentedWeakFingerprints => PipExecutorCounter.CacheMissesForDescriptorsDueToAugmentedWeakFingerprints,
            PipCacheMissType.MissForDescriptorsDueToArtificialMissOptions => PipExecutorCounter.CacheMissesForDescriptorsDueToArtificialMissOptions,
            PipCacheMissType.MissForCacheEntry => PipExecutorCounter.CacheMissesForCacheEntry,
            PipCacheMissType.MissDueToInvalidDescriptors => PipExecutorCounter.CacheMissesDueToInvalidDescriptors,
            PipCacheMissType.MissForProcessMetadata => PipExecutorCounter.CacheMissesForProcessMetadata,
            PipCacheMissType.MissForProcessMetadataFromHistoricMetadata => PipExecutorCounter.CacheMissesForProcessMetadataFromHistoricMetadata,
            PipCacheMissType.MissForProcessOutputContent => PipExecutorCounter.CacheMissesForProcessOutputContent,
            PipCacheMissType.Hit => PipExecutorCounter.CacheHitsForProcessPipDescriptors,
            PipCacheMissType.MissForProcessConfiguredUncacheable => PipExecutorCounter.CacheMissesForProcessConfiguredUncacheable,
            _ => throw new ArgumentException($"Invalid or unknown {nameof(PipCacheMissType)}: {type}")
        };

        /// <summary>
        /// Gets all valid cache miss types indicating cache misses.
        /// </summary>
        public static readonly IEnumerable<PipCacheMissType> AllCacheMisses = Enum.GetValues(typeof(PipCacheMissType))
            .Cast<PipCacheMissType>()
            .Where(e => e != PipCacheMissType.Invalid && e != PipCacheMissType.Hit)
            .ToArray();
    }
}
