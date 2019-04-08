// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Interfaces.Distributed
{
    /// <summary>
    /// Options for how to treat content hashes in regards to location store.
    /// </summary>
    public enum LocationStoreOption
    {
        /// <summary>
        /// Do not register as new hash or update expiry.
        /// </summary>
        None,

        /// <summary>
        /// Update expiry only if the hash already exists in the location store.
        /// </summary>
        UpdateExpiry
    }
}
