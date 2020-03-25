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
        /// Gets or sets the maximum number of simultaneous external file IO operations.
        /// </summary>
        public int MaxIOOperations { get; set; } = 512;
    }
}
