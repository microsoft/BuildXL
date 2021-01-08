// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.Host.Configuration;
using static BuildXL.Utilities.ConfigurationHelper;

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

        /// <summary>
        /// Include domain name in machine location.
        /// </summary>
        public bool UseDomainName { get; set; }

        /// <nodoc />
        public static GrpcFileCopierConfiguration FromDistributedContentSettings(DistributedContentSettings dcs, int grpcPort)
        {
            var grpcCopyClientCacheConfiguration = GrpcCopyClientCacheConfiguration.FromDistributedContentSettings(dcs);

            var grpcFileCopierConfiguration = new GrpcFileCopierConfiguration()
                                              {
                                                  GrpcPort = (int)grpcPort,
                                                  GrpcCopyClientCacheConfiguration = grpcCopyClientCacheConfiguration,
                                                  JunctionsByDirectory = dcs.AlternateDriveMap
                                              };

            ApplyIfNotNull(
                dcs.GrpcFileCopierGrpcCopyClientInvalidationPolicy,
                v =>
                {
                    if (!Enum.TryParse<GrpcFileCopierConfiguration.ClientInvalidationPolicy>(v, out var parsed))
                    {
                        throw new ArgumentException(
                            $"Failed to parse `{nameof(dcs.GrpcFileCopierGrpcCopyClientInvalidationPolicy)}` setting with value `{dcs.GrpcFileCopierGrpcCopyClientInvalidationPolicy}` into type `{nameof(GrpcFileCopierConfiguration.ClientInvalidationPolicy)}`");
                    }

                    grpcFileCopierConfiguration.GrpcCopyClientInvalidationPolicy = parsed;
                });

            grpcFileCopierConfiguration.UseUniversalLocations = dcs.UseUniversalLocations;
            grpcFileCopierConfiguration.UseDomainName = dcs.UseDomainName;

            return grpcFileCopierConfiguration;
        }
    }
}
