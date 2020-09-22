// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

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

        /// <nodoc />
        public TimeSpan ConnectionEstablishmentTimeout { get; set; } = ContentStore.Grpc.GrpcConstants.DefaultTimeout;

        /// <nodoc />
        public TimeSpan DisconnectionTimeout { get; set; } = ContentStore.Grpc.GrpcConstants.DefaultTimeout;

        /// <nodoc />
        public TimeSpan ConnectionTimeout { get; set; } = ContentStore.Grpc.GrpcConstants.DefaultTimeout;

        /// <nodoc />
        public TimeSpan OperationDeadline { get; set; } = TimeSpan.FromHours(2);
    }
}
