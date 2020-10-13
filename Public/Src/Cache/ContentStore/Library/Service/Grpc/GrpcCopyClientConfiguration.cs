// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Grpc;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Configuration class for <see cref="GrpcCopyClient"/>
    /// </summary>
    public class GrpcCopyClientConfiguration
    {
        /// <nodoc />
        public static GrpcCopyClientConfiguration WithGzipCompression(bool useGzipCompression)
        {
            return new GrpcCopyClientConfiguration()
            {
                UseGzipCompression = useGzipCompression,
            };
        }

        /// <nodoc />
        public int ClientBufferSizeBytes { get; set; } = ContentStore.Grpc.GrpcConstants.DefaultBufferSizeBytes;

        /// <summary>
        /// Whether to allow using Gzip compression for copies
        /// </summary>
        public bool UseGzipCompression { get; set; }

        /// <summary>
        /// Whether to force connection establishment on startup
        /// </summary>
        /// <remarks>
        /// This does not work with ResourcePool V1
        /// </remarks>
        public bool ConnectOnStartup { get; set; }

        /// <summary>
        /// A timeout that Grpc Client is allowed to wait when closing the connection with another side.
        /// </summary>
        /// <remarks>
        /// We noticed that in some cases closing the channel may hang, so this timeout is protecting us from waiting indefinitely in application shutdown.
        /// </remarks>
        public TimeSpan DisconnectionTimeout { get; set; } = ContentStore.Grpc.GrpcConstants.DefaultTimeout;

        /// <summary>
        /// A timeout to establish a connection.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = ContentStore.Grpc.GrpcConstants.DefaultTimeout;

        /// <summary>
        /// When the connection is established in StartupAsync method, this configuration determines "time to first byte" timeout.
        /// </summary>
        public TimeSpan TimeToFirstByteTimeout { get; set; } = ContentStore.Grpc.GrpcConstants.DefaultTimeout;

        /// <nodoc />
        public TimeSpan OperationDeadline { get; set; } = TimeSpan.FromHours(2);

        /// <nodoc />
        public GrpcCoreClientOptions? GrpcCoreClientOptions { get; set; }

        /// <nodoc />
        public BandwidthChecker.Configuration BandwidthCheckerConfiguration { get; set; } = BandwidthChecker.Configuration.Disabled;
    }
}
