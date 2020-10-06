// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Grpc.Core;

#nullable enable

namespace BuildXL.Cache.ContentStore.Grpc
{
    /// <summary>
    /// Grpc options that apply only to clients
    /// </summary>
    public class GrpcCoreClientOptions : GrpcCoreOptionsCommon
    {
        /// <summary>
        /// Timeout after the last RPC finishes on the client channel at which the
        /// channel goes back into IDLE state. Int valued, milliseconds. INT_MAX means
        /// unlimited. The default value is 30 minutes and the min value is 1 second.
        /// </summary>
        public int? ClientIdleTimeoutMs { get; set; }

        /// <summary>
        /// The minimum time between subsequent connection attempts, in ms
        /// </summary>
        public int? MinReconnectBackoffMs { get; set; }

        /// <summary>
        /// The maximum time between subsequent connection attempts, in ms
        /// </summary>
        public int? MaxReconnectBackoffMs { get; set; }

        /// <summary>
        /// The time between the first and second connection attempts, in ms
        /// </summary>
        public int? InitialReconnectBackoffMs { get; set; }

        /// <inheritdoc />
        public override List<ChannelOption> IntoChannelOptions()
        {
            var options = base.IntoChannelOptions();
            ApplyIfNotNull(options, "grpc.client_idle_timeout_ms", ClientIdleTimeoutMs);
            ApplyIfNotNull(options, "grpc.min_reconnect_backoff_ms", MinReconnectBackoffMs);
            ApplyIfNotNull(options, "grpc.max_reconnect_backoff_ms", MaxReconnectBackoffMs);
            ApplyIfNotNull(options, "grpc.initial_reconnect_backoff_ms", InitialReconnectBackoffMs);
            return options;
        }
    }
}
