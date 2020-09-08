// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Grpc.Core;

namespace BuildXL.Plugin
{
    /// <nodoc />
    internal static class GrpcPluginSettings
    {
        /// <nodoc />
        public static int RequestTimeoutInMiilliSeceonds = 3000;
        /// <nodoc />
        public static int ConnectionTimeoutInMiilliSeceonds = 5000;
        /// <nodoc />
        public const string PluginReqeustId = "req_id";
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
