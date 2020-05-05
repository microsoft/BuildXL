// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Wrapper for functions associated with distributed pinning. Used as a flag.
    /// </summary>
    // Obsolete but left with no attribute to avoid breaking the clients. The type is used in the public surface.
    public class DistributedEvictionSettings
    {
        /// <summary>
        /// Distributed store used in a next-gen distributed eviction logic based on a local location store.
        /// </summary>
        public readonly IDistributedLocationStore? DistributedStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="DistributedEvictionSettings"/> class.
        /// </summary>
        public DistributedEvictionSettings(IDistributedLocationStore distributedStore)
        {
            DistributedStore = distributedStore;
        }
    }
}
