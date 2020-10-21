// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using static BuildXL.Utilities.ConfigurationHelper;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Configuration for <see cref="GrpcCopyClientCache"/>
    /// </summary>
    public class GrpcCopyClientCacheConfiguration
    {
        /// <summary>
        /// Resource pool versions
        /// </summary>
        public enum PoolVersion
        {
            /// <summary>
            /// Do not cache instances
            /// </summary>
            Disabled,
            /// <summary>
            /// Use <see cref="ResourcePool{TKey, TObject}"/>
            /// </summary>
            V1,
            /// <summary>
            /// Use <see cref="ResourcePoolV2{TKey, TObject}"/>
            /// </summary>
            V2
        }

        /// <summary>
        /// Which resource pool version to use
        /// </summary>
        public PoolVersion ResourcePoolVersion { get; set; } = PoolVersion.V1;

        /// <summary>
        /// Configuration for the resource pool
        /// </summary>
        public ResourcePoolConfiguration ResourcePoolConfiguration { get; set; } = new ResourcePoolConfiguration();

        /// <summary>
        /// Configuration for the cached <see cref="GrpcCopyClientConfiguration"/>
        /// </summary>
        public GrpcCopyClientConfiguration GrpcCopyClientConfiguration { get; set; } = new GrpcCopyClientConfiguration();

        /// <nodoc />
        public static GrpcCopyClientCacheConfiguration FromDistributedContentSettings(DistributedContentSettings dcs)
        {
            var grpcCopyClientConfiguration = GrpcCopyClientConfiguration.FromDistributedContentSettings(dcs);

            var resourcePoolConfiguration = ResourcePoolConfiguration.FromDistributedContentSettings(dcs);

            var grpcCopyClientCacheConfiguration = new GrpcCopyClientCacheConfiguration()
                                                   {
                                                       ResourcePoolConfiguration = resourcePoolConfiguration,
                                                       GrpcCopyClientConfiguration = grpcCopyClientConfiguration,
                                                   };

            ApplyIfNotNull(dcs.GrpcCopyClientCacheResourcePoolVersion, v => grpcCopyClientCacheConfiguration.ResourcePoolVersion = (PoolVersion)v);

            return grpcCopyClientCacheConfiguration;
        }
    }
}
