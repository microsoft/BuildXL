// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// Contains settings for better pinning logic.
    /// </summary>
    /// <remarks><para>Default settings are highly approximate.</para></remarks>
    public class PinConfiguration
    {
        /// <summary>
        /// Gets or sets the maximum acceptable risk for a successful pin.
        /// </summary>
        public double PinRisk { get; set; } = 1.0E-5;

        /// <summary>
        /// Minimum number of records to proceed with a pin without record verification
        /// </summary>
        public int? PinMinUnverifiedCount { get; set; }

        /// <summary>
        /// Gets or sets the presumed risk of a machine being inaccessible.
        /// </summary>
        public double MachineRisk { get; set; } = 0.02;

        /// <summary>
        /// Gets or sets the presumed risk of a file being gone from a machine even though the content location record says it should be there.
        /// </summary>
        public double FileRisk { get; set; } = 0.05;

        /// <summary>
        /// Gets or sets the maximum number of simultaneous external file IO operations.
        /// </summary>
        public int MaxIOOperations { get; set; } = 512;

        /// <summary>
        /// Gets or sets the starting retention time for content hash entries in the pin cache.
        ///
        /// This number is equivalent to the amount of time we are willing to hold a pin with a single replica, and how
        /// much "credit" we get per each additional replica.
        /// </summary>
        public int PinCachePerReplicaRetentionCreditMinutes { get; set; } = 30;

        /// <summary>
        /// Gets or sets the decay applied for replicas to <see cref="PinCachePerReplicaRetentionCreditMinutes"/>. Must be between 0 and 0.9.
        /// For each replica 1...n, with decay d, the additional retention is depreciated by d^n (i.e. only  <see cref="PinCachePerReplicaRetentionCreditMinutes"/> * d^n is added to the total retention
        /// based on the replica).
        /// </summary>
        public double PinCacheReplicaCreditRetentionFactor { get; set; } = 0.75;

        /// <summary>
        /// Gets or sets a value indicating whether pin caching should be used
        /// </summary>
        public bool IsPinCachingEnabled { get; set; }
    }
}
