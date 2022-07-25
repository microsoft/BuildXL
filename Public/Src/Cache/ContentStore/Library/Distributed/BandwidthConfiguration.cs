// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// A bandwidth requirements for a copy operation.
    /// </summary>
    public class BandwidthConfiguration
    {
        private const double BytesInMb = 1024 * 1024;

        /// <summary>
        /// Whether to invalidate Grpc Copy Client in case of an error.
        /// </summary>
        /// <remarks>
        /// When ResourcePool is used and the connection is happening at startup the semantic of this option changes from
        /// being a connection timeout to be a timeout for getting the first bytes from the other side.
        /// </remarks>
        public bool InvalidateOnTimeoutError { get; set; } = true;

        /// <summary>
        /// Gets an optional connection timeout that can be used to reject the copy more aggressively during early copy attempts.
        /// </summary>
        public TimeSpan? ConnectionTimeout { get; set; }

        /// <summary>
        /// The interval between the copy progress is checked.
        /// </summary>
        public TimeSpan Interval { get; set; }

        /// <summary>
        /// The number of required bytes that should be copied within a given interval. Otherwise the copy would be canceled.
        /// </summary>
        public long RequiredBytes { get; set; }

        /// <summary>
        /// If true, the server will return an error response immediately if the number of pending copy operations crosses a threshold.
        /// </summary>
        public bool FailFastIfServerIsBusy { get; set; }

        /// <summary>
        /// When enabled, bandwidthchecker calculates network speed for copying
        /// </summary>
        public bool EnableNetworkCopySpeedCalculation { get; set; }

        /// <summary>
        /// Gets the required megabytes per second.
        /// </summary>
        public double RequiredMegabytesPerSecond => (double)(RequiredBytes / BytesInMb) / Interval.TotalSeconds;

        /// <inheritdoc />
        public override string ToString() => $"Throughput={RequiredBytes / Interval.TotalSeconds}, FailFast={FailFastIfServerIsBusy}";
    }
}
