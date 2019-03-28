// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// </summary>
        public int PinCacheReplicaCreditRetentionMinutes { get; set; } = 30;

        /// <summary>
        /// Gets or sets the decay applied for replicas to <see cref="PinCacheReplicaCreditRetentionMinutes"/>. Must be between 0 and 0.9.
        /// For each replica 1...n, with decay d, the additional retention is depreciated by d^n (i.e. only  <see cref="PinCacheReplicaCreditRetentionMinutes"/> * d^n is added to the total retention
        /// based on the replica).
        /// </summary>
        public double PinCacheReplicaCreditRetentionDecay { get; set; } = 0.75;

        /// <summary>
        /// Gets or sets a value indicating whether pin caching should be used
        /// </summary>
        public bool UsePinCache { get; set; }
    }
}
