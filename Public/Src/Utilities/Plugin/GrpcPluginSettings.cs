// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Grpc.Core;

namespace BuildXL.Plugin
{
    /// <nodoc />
    internal static class GrpcPluginSettings
    {
        /// <summary>
        /// The number of milliseconds before the request "expires" (referred to in gRPC as a "deadline").
        /// </summary>
        public const int RequestTimeoutInMilliSeconds = 30000; // 30s might be considered a little extreme at this point, but it will be monitored (and adjusted) before being used in production (OE: 9836612)
        /// <nodoc />
        public const int ConnectionTimeoutInMilliSeconds = 5000;
        /// <nodoc />
        public const string PluginRequestId = "req_id";
        /// <nodoc />
        public const string PluginRequestRetryCount = "req_retry_count";

        public const int MaxAttempts = 5;

        /// <summary>
        /// How often the client sends HTTP/2 PING frames to detect dead connections.
        /// </summary>
        public const int KeepAlivePingDelayInSeconds = 5;

        /// <summary>
        /// How long to wait for a PING acknowledgement before considering the connection dead.
        /// Total detection time = KeepAlivePingDelay + KeepAlivePingTimeout (≤ 20s).
        /// </summary>
        public const int KeepAlivePingTimeoutInSeconds = 15;

        /// <summary>
        /// Server-side: minimum allowed interval between client pings (must be ≤ client ping delay).
        /// Without this, the server rejects frequent client PINGs with a GOAWAY frame.
        /// </summary>
        public const int ServerMinPingIntervalInMilliSeconds = 5000;

        /// <nodoc />
        private static readonly ChannelOption[] s_defaultChannelOptions = new ChannelOption[] { 
            new ChannelOption(ChannelOptions.MaxSendMessageLength, -1), 
            new ChannelOption(ChannelOptions.MaxReceiveMessageLength, -1) 
        };

        /// <summary>
        /// This is for server side channel options which is not migrated to grpc.net yet
        /// </summary>
        public static IEnumerable<ChannelOption> GetChannelOptions()
        {
            List<ChannelOption> channelOptions = new List<ChannelOption>();
            channelOptions.AddRange(s_defaultChannelOptions);

            // Allow the client to send keepalive pings frequently without being rejected via GOAWAY
            channelOptions.Add(new ChannelOption("grpc.keepalive_permit_without_calls", 1));
            channelOptions.Add(new ChannelOption("grpc.http2.min_time_between_pings_ms", ServerMinPingIntervalInMilliSeconds));
            channelOptions.Add(new ChannelOption("grpc.http2.min_ping_interval_without_data_ms", ServerMinPingIntervalInMilliSeconds));

            return channelOptions;
        }
    }
}
