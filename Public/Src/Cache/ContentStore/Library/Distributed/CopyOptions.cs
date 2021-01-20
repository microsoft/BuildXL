// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using ContentStore.Grpc;

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Options for copy operations.
    /// </summary>
    public class CopyOptions
    {
        private long _totalBytesCopied;

        /// <nodoc />
        public CopyOptions(BandwidthConfiguration? bandwidthConfiguration) => BandwidthConfiguration = bandwidthConfiguration;

        /// <summary>
        /// Update the total bytes copied.
        /// </summary>
        public void UpdateTotalBytesCopied(long position)
        {
            lock (this)
            {
                _totalBytesCopied = position;
            }
        }

        /// <summary>
        /// Gets the total bytes copied so far.
        /// </summary>
        public long TotalBytesCopied
        {
            get
            {
                lock (this)
                {
                    return _totalBytesCopied;
                }
            }
        }

        /// <summary>
        /// Bandwidth requirements for the current copy attempt.
        /// </summary>
        public BandwidthConfiguration? BandwidthConfiguration { get; set; }

        /// <summary>
        /// Requested compression algorithm to use for the current copy attempt.
        /// </summary>
        /// <remarks>
        /// Server and/or client may decide to ignore the hint.
        /// </remarks>
        public CopyCompression CompressionHint { get; set; }
    }
}
