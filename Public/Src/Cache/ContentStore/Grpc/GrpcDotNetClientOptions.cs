// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

#nullable enable

namespace BuildXL.Cache.ContentStore.Grpc
{
    /// <summary>
    /// Configuration options used by Grpc.Net clients.
    /// </summary>
    public record GrpcDotNetClientOptions
    {
        public bool KeepAliveEnabled { get; init; } = true;

        public TimeSpan KeepAlivePingDelay { get; init; } = TimeSpan.FromMinutes(5);
        public TimeSpan KeepAlivePingTimeout { get; init; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// A timeout passed to SocketsHttpHandler that is used by Grpc.Net implementation.
        /// </summary>
        public TimeSpan? ConnectionTimeout { get; init; }

        /// <summary>
        /// Gets or sets the type of decompression method used by the handler for automatic decompression of the HTTP content response.
        /// </summary>
        /// <remarks>
        /// This property contains a string representation of System.Net.DecompressionMethods enumeration.
        /// </remarks>
        public string? DecompressionMethods { get; init; }

        /// <summary>
        /// See SocketsHttpHandler.PooledConnectionIdleTimeout
        /// </summary>
        public TimeSpan? PooledConnectionIdleTimeout { get; init; }

        /// <summary>
        /// See SocketsHttpHandler.PooledConnectionLifetime
        /// </summary>
        public TimeSpan? PooledConnectionLifetime { get; init; }

        public int MaxSendMessageSize { get; init; } = int.MaxValue;

        public int MaxReceiveMessageSize { get; set; } = int.MaxValue;

        /// <summary>
        /// If not null, specifies min verbosity level from grpc layer that will be sent to the cache telemetry.
        /// </summary>
        /// <remarks>
        /// This value should match the values from Microsoft.Extensions.Logging.LogLevel enum.
        /// </remarks>
        public int? MinLogLevelVerbosity { get; init; }

        public static GrpcDotNetClientOptions Default { get; } = new GrpcDotNetClientOptions() { MinLogLevelVerbosity = 4 }; // Using error logging severity by default.
    }

}
