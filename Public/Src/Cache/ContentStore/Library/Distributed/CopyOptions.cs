// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Options for copy operations.
    /// </summary>
    /// <remarks>
    /// Currently there are no options available and this class is used for progress reporting only.
    /// But we expect to have actual options or other input data like counters here.
    /// </remarks>
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
        /// A bandwidth requirements for the current copy attempt.
        /// </summary>
        public BandwidthConfiguration? BandwidthConfiguration { get; set; }
    }
}
