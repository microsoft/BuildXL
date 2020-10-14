// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Service.Grpc;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// Configuration class for <see cref="GrpcFileCopier"/>
    /// </summary>
    public class GrpcFileCopierConfiguration
    {
        /// <nodoc />
        public enum ClientInvalidationPolicy
        {
            /// <nodoc />
            Disabled,

            /// <nodoc />
            OnEveryError,

            /// <nodoc />
            OnConnectivityErrors,
        }

        /// <summary>
        /// Port to connect to on other machines
        /// </summary>
        public int GrpcPort { get; set; } = GrpcConstants.DefaultGrpcPort;

        /// <summary>
        /// Whether to invalidate Grpc clients when certain problematic issues happen
        /// </summary>
        public ClientInvalidationPolicy GrpcCopyClientInvalidationPolicy { get; set; } = ClientInvalidationPolicy.Disabled;

        /// <summary>
        /// Configuration for the internal <see cref="GrpcCopyClientCacheConfiguration"/> used to cache connections
        /// </summary>
        public GrpcCopyClientCacheConfiguration GrpcCopyClientCacheConfiguration { get; set; } = new GrpcCopyClientCacheConfiguration();

        /// <summary>
        /// Specifies alternative path mapping used when computing machine location
        /// </summary>
        public IReadOnlyDictionary<string, string> JunctionsByDirectory { get; set; }

        /// <summary>
        /// Indicates whether machine locations should use universal format (i.e. uri of form 'grpc://{machineName}:{port}/') which
        /// allows communication across machines of different platforms
        /// </summary>
        public bool UseUniversalLocations { get; set; }
    }
}
