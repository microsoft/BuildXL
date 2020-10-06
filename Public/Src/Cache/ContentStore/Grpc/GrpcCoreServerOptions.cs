// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Grpc.Core;

#nullable enable

namespace BuildXL.Cache.ContentStore.Grpc
{
    /// <summary>
    /// Grpc options that apply only to servers
    /// </summary>
    public class GrpcCoreServerOptions : GrpcCoreOptionsCommon
    {
        /// <summary>
        /// Maximum number of concurrent incoming streams to allow on a http2 connection.
        /// </summary>
        public int? MaxConcurrentStreams { get; set; }

        /// <summary>
        /// Maximum time that a channel may have no outstanding rpcs, after which the
        /// server will close the connection. Int valued, milliseconds. INT_MAX means
        /// unlimited.
        /// </summary>
        public int? MaxConnectionIdleMs { get; set; }

        /// <summary>
        /// Maximum time that a channel may exist. Int valued, milliseconds.
        /// INT_MAX means unlimited.
        /// </summary>
        public int? MaxConnectionAgeMs { get; set; }

        /// <summary>
        /// Grace period after the channel reaches its max age. Int valued,
        /// milliseconds. INT_MAX means unlimited.
        /// </summary>
        public int? MaxConnectionAgeGraceMs { get; set; }

        /// <summary>
        /// The timeout used on servers for finishing handshaking on an incoming
        /// connection.  Defaults to 120 seconds.
        /// </summary>
        public int? ServerHandshakeTimeoutMs { get; set; }

        /// <summary>
        /// How many misbehaving pings the server can bear before sending goaway and
        /// closing the transport? (0 indicates that the server can bear an infinite
        /// number of misbehaving pings)
        /// </summary>
        public int? Http2MaxPingStrikes { get; set; }

        /// <summary>
        /// Minimum allowed time between a server receiving successive ping frames
        /// without sending any data/header frame. Int valued, milliseconds
        /// </summary>
        public int? Http2MinPingIntervalWithoutDataMs { get; set; }

        /// <inheritdoc />
        public override List<ChannelOption> IntoChannelOptions()
        {
            var options = base.IntoChannelOptions();
            ApplyIfNotNull(options, ChannelOptions.MaxConcurrentStreams, MaxConcurrentStreams);
            ApplyIfNotNull(options, "grpc.max_connection_idle_ms", MaxConnectionIdleMs);
            ApplyIfNotNull(options, "grpc.max_connection_age_ms", MaxConnectionAgeMs);
            ApplyIfNotNull(options, "grpc.max_connection_age_grace_ms", MaxConnectionAgeGraceMs);
            ApplyIfNotNull(options, "grpc.server_handshake_timeout_ms", ServerHandshakeTimeoutMs);
            ApplyIfNotNull(options, "grpc.http2.max_ping_strikes", Http2MaxPingStrikes);
            ApplyIfNotNull(options, "grpc.http2.min_ping_interval_without_data_ms", Http2MinPingIntervalWithoutDataMs);
            return options;
        }
    }
}
