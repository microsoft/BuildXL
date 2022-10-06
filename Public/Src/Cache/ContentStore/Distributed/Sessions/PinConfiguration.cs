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
        /// Minimum number of records to proceed with a pin without record verification
        /// </summary>
        public int PinMinUnverifiedCount { get; set; } = 3;

        /// <summary>
        /// If true, then only local location information is used for pinning the content without copying it locally if the number of locations is grater then <see cref="PinMinUnverifiedCount"/>.
        /// </summary>
        public bool UseLocalLocationsOnlyOnUnverifiedPin { get; set; }

        /// <summary>
        /// Trigger an asynchronous copy when the number of locations is between <see cref="PinMinUnverifiedCount"/> and <code>PinMinUnverifiedCount + AsyncCopyOnPinThreshold</code>.
        /// </summary>
        public int AsyncCopyOnPinThreshold { get; set; } = 0;

        /// <summary>
        /// Gets or sets the maximum number of simultaneous external file IO operations.
        /// </summary>
        public int MaxIOOperations { get; set; } = 512;
    }
}
