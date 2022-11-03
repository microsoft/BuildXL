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
        /// Whether the grpc connection pooling is enabled.
        /// </summary>
        public bool ResourcePoolEnabled { get; set; } = true;

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

            ApplyIfNotNull(dcs.GrpcCopyClientResourcePoolEnabled, v => grpcCopyClientCacheConfiguration.ResourcePoolEnabled = v);

            return grpcCopyClientCacheConfiguration;
        }
    }
}
