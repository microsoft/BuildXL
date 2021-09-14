// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Target location for <see cref="GetBulkLocationsResult"/>.
    /// </summary>
    public enum GetBulkOrigin
    {
        /// <summary>
        /// The locations should be obtained from the Local Location Store (LLS).
        /// </summary>
        Local,

        /// <summary>
        /// The locations should be obtained from a global remote store (redis).
        /// </summary>
        Global,

        /// <summary>
        /// The locations should be obtained from the ColdStorage consistent hashing system.
        /// </summary>
        ColdStorage
    }
}
