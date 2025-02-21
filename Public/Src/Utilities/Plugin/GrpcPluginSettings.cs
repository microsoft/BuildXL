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
        public static int RequestTimeoutInMilliSeconds = 30000; // 30s might be considered a little extreme at this point, but it will be monitored (and adjusted) before being used in production (OE: 9836612)
        /// <nodoc />
        public static int ConnectionTimeoutInMilliSeconds = 5000;
        /// <nodoc />
        public const string PluginRequestId = "req_id";
        /// <nodoc />
        public const string PluginRequestRetryCount = "req_retry_count";

        /// <nodoc />
        private static readonly ChannelOption[] s_defaultChannelOptions = new ChannelOption[] { 
            new ChannelOption(ChannelOptions.MaxSendMessageLength, -1), 
            new ChannelOption(ChannelOptions.MaxReceiveMessageLength, -1) 
        };

        /// <nodoc />
        public static IEnumerable<ChannelOption> GetChannelOptions()
        {
            List<ChannelOption> channelOptions = new List<ChannelOption>();
            channelOptions.AddRange(s_defaultChannelOptions);

            return channelOptions;
        }
    }
}
