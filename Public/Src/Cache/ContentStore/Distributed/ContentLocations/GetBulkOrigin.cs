// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Target location for <see cref="GetBulkLocationsResult"/>.
    /// </summary>
    public enum GetBulkOrigin
    {
        /// <summary>
        /// The locations should be obtained from a local store.
        /// </summary>
        Local,

        /// <summary>
        /// The locations should be obtained from a global remote store.
        /// </summary>
        Global
    }
}
