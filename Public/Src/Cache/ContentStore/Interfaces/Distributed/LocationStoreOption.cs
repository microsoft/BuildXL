// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
