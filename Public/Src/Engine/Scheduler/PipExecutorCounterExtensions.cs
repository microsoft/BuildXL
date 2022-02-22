// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Scheduler
{
    /// <nodoc />
    public static class PipExecutorCounterExtensions
    {
        /// <summary>
        /// Returns a 'frontier pip' variant of cache miss counter.
        /// </summary>
        public static PipExecutorCounter ToFrontierPipCacheMissCounter(this PipExecutorCounter counter)
        {
            switch (counter)
            {
                case PipExecutorCounter.CacheMissesForDescriptorsDueToStrongFingerprints:
                    return PipExecutorCounter.CacheMissesForDescriptorsDueToStrongFingerprints_Frontier;

                case PipExecutorCounter.CacheMissesForDescriptorsDueToWeakFingerprints:
                    return PipExecutorCounter.CacheMissesForDescriptorsDueToWeakFingerprints_Frontier;

                case PipExecutorCounter.CacheMissesForDescriptorsDueToAugmentedWeakFingerprints:
                    return PipExecutorCounter.CacheMissesForDescriptorsDueToAugmentedWeakFingerprints_Frontier;

                case PipExecutorCounter.CacheMissesForDescriptorsDueToArtificialMissOptions:
                    return PipExecutorCounter.CacheMissesForDescriptorsDueToArtificialMissOptions_Frontier;

                case PipExecutorCounter.CacheMissesForCacheEntry:
                    return PipExecutorCounter.CacheMissesForCacheEntry_Frontier;

                case PipExecutorCounter.CacheMissesDueToInvalidDescriptors:
                    return PipExecutorCounter.CacheMissesDueToInvalidDescriptors_Frontier;

                case PipExecutorCounter.CacheMissesForProcessMetadata:
                    return PipExecutorCounter.CacheMissesForProcessMetadata_Frontier;

                case PipExecutorCounter.CacheMissesForProcessMetadataFromHistoricMetadata:
                    return PipExecutorCounter.CacheMissesForProcessMetadataFromHistoricMetadata_Frontier;

                case PipExecutorCounter.CacheMissesForProcessOutputContent:
                    return PipExecutorCounter.CacheMissesForProcessOutputContent_Frontier;

                case PipExecutorCounter.CacheMissesForProcessConfiguredUncacheable:
                    return PipExecutorCounter.CacheMissesForProcessConfiguredUncacheable_Frontier;

                default:
                    throw new ArgumentException($"Cannot find a corresponding counter for '{counter}'");
            }
        }
    }
}
