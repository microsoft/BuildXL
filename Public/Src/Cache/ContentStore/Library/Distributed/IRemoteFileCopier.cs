// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Options for <see cref="IRemoteFileCopier{T}.CopyToAsync"/> operation.
    /// </summary>
    /// <remarks>
    /// Currently there are no options available and this class is used for progress reporting only.
    /// But we expect to have actual options or other input data like counters here.
    /// </remarks>
    public class CopyToOptions
    {
        private long _totalBytesCopied;

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

    /// <summary>
    /// A bandwidth requirements for a copy operation.
    /// </summary>
    public class BandwidthConfiguration
    {
        private const double BytesInMb = 1024 * 1024;

        /// <summary>
        /// Whether to invalidate Grpc Copy Client in case of an error.
        /// </summary>
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
        /// Gets the required megabytes per second.
        /// </summary>
        public double RequiredMegabytesPerSecond => (double)(RequiredBytes / BytesInMb) / Interval.TotalSeconds;

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{RequiredBytes / Interval.TotalSeconds}";
        }
    }

    /// <summary>
    /// Represents an interface that allows copying files from a remote source to a local path.
    /// </summary>
    public interface IRemoteFileCopier<in T>
        where T : PathBase
    {
        /// <summary>
        /// Copies a file represented by the path into the stream specified.
        /// </summary>
        Task<CopyFileResult> CopyToAsync(OperationContext context, T sourcePath, Stream destinationStream, long expectedContentSize, CopyToOptions? options);
    }

    /// <nodoc />
    public static class RemoteFileCopierExtensions
    {
        /// <inheritdoc cref="IRemoteFileCopier{T}.CopyToAsync"/>
        public static Task<CopyFileResult> CopyToAsync<T>(this IRemoteFileCopier<T> remoteFileCopier, OperationContext context, T sourcePath, Stream destinationStream, long expectedContentSize)
            where T : PathBase
        {
            return remoteFileCopier.CopyToAsync(context, sourcePath, destinationStream, expectedContentSize, options: default);
        }
    }
}
