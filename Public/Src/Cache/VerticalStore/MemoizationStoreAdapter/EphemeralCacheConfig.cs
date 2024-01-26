// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Configuration for <see cref="EphemeralCacheFactory"/>.
    /// </summary>
    public sealed class EphemeraCacheConfig : BlobCacheConfig
    {
        /// <nodoc />
        public string CacheRootPath { get; set; }

        /// <nodoc />
        public string LeaderMachineName { get; set; }

        /// <summary>
        /// The replication domain of the ephemeral cache. When the cache is build-wide (flag is false), workers can
        /// get cache hits from other workers in the same build. When it's datacenter-wide, workers can get cache hits
        /// from any other machine in the "datacenter".
        ///
        /// The datacenter mode requires a storage account that all machines can access, as well as the ability to
        /// perform P2P copies between machines even across different builds.
        /// </summary>
        public bool DatacenterWide { get; set; } = false;

        /// <nodoc />
        public uint CacheSizeMb { get; set; }

        /// <nodoc />
        public EphemeraCacheConfig()
        {
            CacheId = new CacheId("EphemeralCache");
        }
    }
}
