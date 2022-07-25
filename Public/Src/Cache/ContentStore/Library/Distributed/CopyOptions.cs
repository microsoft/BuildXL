// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using ContentStore.Grpc;

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Options for copy operations.
    /// </summary>
    public class CopyOptions
    {
        private CopyStatistics _copyStatistics;

        /// <nodoc />
        public CopyOptions(BandwidthConfiguration? bandwidthConfiguration) => BandwidthConfiguration = bandwidthConfiguration;

        /// <summary>
        /// Update the total bytes copied and delay
        /// </summary>
        public void UpdateTotalBytesCopied(CopyStatistics copyStatistics)
        {
            lock (this)
            {
                _copyStatistics = copyStatistics;
            }
        }

        /// <summary>
        /// Gets the total bytes copied and time used so far
        /// </summary>
        public CopyStatistics CopyStatistics
        {
            get
            {
                lock (this)
                {
                    return _copyStatistics;
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

    public record struct CopyStatistics(long Bytes, TimeSpan NetworkCopyDuration);
}
