// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Configuration for <see cref="EphemeralCacheFactory"/>.
    /// </summary>
    public sealed class EphemeralCacheConfig : BlobCacheConfig
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

        /// <summary>
        /// Indicates whether to delete local content on close
        /// </summary>
        [DefaultValue(false)]
        public bool DeleteOnClose { get; set; } = false;

        #region RPC Client Config

        /// <summary>
        /// Specifies that content operations should go to local CAS only when content server
        /// exposes a distributed cache.
        /// </summary>
        [DefaultValue(false)]
        public bool UseContentServerLocalCas { get; set; } = false;

        /// <summary>
        /// Whether the cache will communicate with a server in a separate process via GRPC.
        /// </summary>
        [DefaultValue(false)]
        public bool UseContentServer { get; set; }

        /// <summary>
        /// Name of one of the named caches owned by CASaaS.
        /// </summary>
        [DefaultValue("Default")]
        public string CacheName { get; set; }

        /// <summary>
        /// How many seconds each call should wait for a CASaaS connection before retrying.
        /// </summary>
        [DefaultValue(5)]
        public uint ConnectionRetryIntervalSeconds { get; set; }

        /// <summary>
        /// How many times each call should retry connecting to CASaaS before timing out.
        /// </summary>
        [DefaultValue(120)]
        public uint ConnectionRetryCount { get; set; }

        /// <summary>
        /// A custom scenario to connect to for the CAS service.
        /// </summary>
        [DefaultValue(null)]
        public string ScenarioName { get; set; }

        /// <summary>
        /// A custom scenario to connect to for the CAS service. If set, overrides GrpcPortFileName.
        /// </summary>
        [DefaultValue(0)]
        public uint GrpcPort { get; set; }

        /// <summary>
        /// Custom name of the memory-mapped file to read from to find the GRPC port used for the CAS service.
        /// </summary>
        [DefaultValue(null)]
        public string GrpcPortFileName { get; set; }

        /// <nodoc />
        [DefaultValue(false)]
        public bool GrpcTraceOperationStarted { get; set; }

        /// <nodoc />
        [DefaultValue(null)]
        public GrpcEnvironmentOptions GrpcEnvironmentOptions { get; set; }

        /// <nodoc />
        [DefaultValue(null)]
        public GrpcCoreClientOptions GrpcCoreClientOptions { get; set; }

        #endregion

        /// <nodoc />
        public EphemeralCacheConfig()
        {
            CacheId = new CacheId("EphemeralCache");
        }
    }
}
