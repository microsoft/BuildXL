// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Grpc.Core;

#nullable enable

namespace BuildXL.Cache.ContentStore.Grpc
{
    /// <summary>
    /// Grpc options that apply to both clients and servers
    /// </summary>
    public class GrpcCoreOptionsCommon
    {
        /// <summary>
        /// Amount to read ahead on individual streams. Defaults to 64kb, larger
        /// values can help throughput on high-latency connections.
        /// NOTE: at some point we'd like to auto-tune this, and this parameter
        /// will become a no-op. Int valued, bytes.
        /// </summary>
        public int? Http2LookaheadBytes { get; set; }

        /// <summary>
        /// How big a frame are we willing to receive via HTTP2.
        /// Min 16384, max 16777215. Larger values give lower CPU usage for large
        /// messages, but more head of line blocking for small messages.
        /// </summary>
        public int? Http2MaxFrameSize { get; set; }

        /// <summary>
        /// Minimum time between sending successive ping frames without receiving any
        /// data/header frame, Int valued, milliseconds.
        /// </summary>
        public int? Http2MinTimeBetweenPingsMs { get; set; }

        /// <summary>
        /// How many pings can we send before needing to send a
        /// data/header frame? (0 indicates that an infinite number of
        /// pings can be sent without sending a data frame or header frame)
        /// </summary>
        public int? Http2MaxPingsWithoutData { get; set; }

        /// <summary>
        /// How much data are we willing to queue up per stream if
        /// GRPC_WRITE_BUFFER_HINT is set? This is an upper bound
        /// </summary>
        public int? Http2WriteBufferSize { get; set; }

        /// <summary>
        /// After a duration of this time the client/server pings its peer to see if the
        /// transport is still alive. Int valued, milliseconds.
        /// </summary>
        public int? KeepaliveTimeMs { get; set; }

        /// <summary>
        /// After waiting for a duration of this time, if the keepalive ping sender does
        /// not receive the ping ack, it will close the transport. Int valued,
        /// milliseconds.
        /// </summary>
        public int? KeepaliveTimeoutMs { get; set; }

        /// <summary>
        /// Is it permissible to send keepalive pings without any outstanding streams.
        /// Int valued, 0(false)/1(true).
        /// </summary>
        public int? KeepalivePermitWithoutCalls { get; set; }

        /// <summary>
        /// Enable SSL encryption on GRPC
        /// </summary>
        public bool EncryptionEnabled { get; set; } = false;

        #region POSIX Only

        /// <summary>
        /// Channel arg (integer) setting how large a slice to try and read from the
        /// wire each time recvmsg (or equivalent) is called
        /// </summary>
        public int? ExperimentalTcpReadChunkSize { get; set; }

        /// <summary>
        /// Note this is not a "channel arg" key. This is the default slice size to use
        /// when trying to read from the wire if the GRPC_ARG_TCP_READ_CHUNK_SIZE
        /// channel arg is unspecified.
        /// </summary>
        public int? ExperimentalTcpMinReadChunkSize { get; set; }

        /// <summary>
        /// TCP TX Zerocopy enable state: zero is disabled, non-zero is enabled. By
        /// default, it is disabled.
        /// </summary>
        public int? ExperimentalTcpMaxReadChunkSize { get; set; }

        /// <summary>
        /// TCP TX Zerocopy enable state: zero is disabled, non-zero is enabled. By
        /// default, it is disabled.
        /// </summary>
        public int? ExperimentalTcpTxZerocopyEnabled { get; set; }

        /// <summary>
        /// TCP TX Zerocopy send threshold: only zerocopy if >= this many bytes sent. By
        /// default, this is set to 16KB.
        /// </summary>
        public int? ExperimentalTcpTxZerocopySendBytesThreshold { get; set; }

        /// <summary>
        /// TCP TX Zerocopy max simultaneous sends: limit for maximum number of pending
        /// calls to tcp_write() using zerocopy. A tcp_write() is considered pending
        /// until the kernel performs the zerocopy-done callback for all sendmsg() calls
        /// issued by the tcp_write(). By default, this is set to 4.
        /// </summary>
        public int? ExperimentalTcpTxZerocopyMaxSimultaneousSends { get; set; }

        #endregion

        /// <summary>
        /// Translates the struct into a list of gRPC Core options that can be used to initialize a
        /// <see cref="Channel"/>.
        /// </summary>
        /// <remarks>
        /// The name translation and categorization into client-side vs server-side options comes from the source code
        /// here: https://github.com/grpc/grpc/blob/master/include/grpc/impl/codegen/grpc_types.h#L142
        /// </remarks>
        public virtual List<ChannelOption> IntoChannelOptions()
        {
            // TODO: perhaps use attributes to denote the option -> name translation?
            var options = new List<ChannelOption>();
            ApplyIfNotNull(options, "grpc.http2.lookahead_bytes", Http2LookaheadBytes);
            ApplyIfNotNull(options, "grpc.http2.max_frame_size", Http2MaxFrameSize);
            ApplyIfNotNull(options, "grpc.http2.min_time_between_pings_ms", Http2MinTimeBetweenPingsMs);
            ApplyIfNotNull(options, "grpc.http2.max_pings_without_data", Http2MaxPingsWithoutData);
            ApplyIfNotNull(options, "grpc.http2.write_buffer_size", Http2WriteBufferSize);
            ApplyIfNotNull(options, "grpc.keepalive_time_ms", KeepaliveTimeMs);
            ApplyIfNotNull(options, "grpc.keepalive_timeout_ms", KeepaliveTimeoutMs);
            ApplyIfNotNull(options, "grpc.keepalive_permit_without_calls", KeepalivePermitWithoutCalls);
            ApplyIfNotNull(options, "grpc.experimental.tcp_read_chunk_size", ExperimentalTcpReadChunkSize);
            ApplyIfNotNull(options, "grpc.experimental.tcp_min_read_chunk_size", ExperimentalTcpMinReadChunkSize);
            ApplyIfNotNull(options, "grpc.experimental.tcp_max_read_chunk_size", ExperimentalTcpMaxReadChunkSize);
            ApplyIfNotNull(options, "grpc.experimental.tcp_tx_zerocopy_enabled", ExperimentalTcpTxZerocopyEnabled);
            ApplyIfNotNull(options, "grpc.experimental.tcp_tx_zerocopy_send_bytes_threshold", ExperimentalTcpTxZerocopySendBytesThreshold);
            ApplyIfNotNull(options, "grpc.experimental.tcp_tx_zerocopy_max_simultaneous_sends", ExperimentalTcpTxZerocopyMaxSimultaneousSends);
            return options;
        }

        /// <nodoc />
        protected static void ApplyIfNotNull(List<ChannelOption> options, string name, int? value)
        {
            if (value != null)
            {
                options.Add(new ChannelOption(name, value.Value));
            }
        }
    }
}
