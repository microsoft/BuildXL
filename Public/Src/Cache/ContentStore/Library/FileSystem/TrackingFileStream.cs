// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Cache.ContentStore.FileSystem
{
    /// <summary>
    /// Special tracking file stream that fails with more readable error message if unhandled error occurs in the finalizer of the instance and the time spent reading/writing a file.
    /// </summary>
    public class TrackingFileStream : FileStream
    {
        private long _readDurationInTicks;
        private long _writeDurationInTicks;

        /// <summary>
        /// The number of constructed instances.
        /// </summary>
        public static long Constructed;

        /// <summary>
        /// The number of properly closed instances.
        /// </summary>
        public static long ProperlyClosed;

        /// <summary>
        /// The number of leaked instances (i.e. the number of called finalizers of this type).
        /// </summary>
        public static long Leaked;

        /// <summary>
        /// The path to the the last leaked file.
        /// </summary>
        public static string? LastLeakedFilePath;

        private string? _path;

        private string Path
        {
            get => _path!;
            set
            {
                Interlocked.Increment(ref Constructed);
                _path = value;
            }
        }

        /// <summary>
        /// A total time spent reading this file.
        /// </summary>
        public TimeSpan ReadDuration => TimeSpan.FromTicks(_readDurationInTicks);

        /// <summary>
        /// A total time spent writing to this file.
        /// </summary>
        public TimeSpan WriteDuration => TimeSpan.FromTicks(_writeDurationInTicks);

        /// <inheritdoc />
        public TrackingFileStream(string path, FileMode mode)
            : base(path, mode)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream(string path, FileMode mode, FileAccess access)
            : base(path, mode, access)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream(string path, FileMode mode, FileAccess access, FileShare share)
            : base(path, mode, access, share)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize)
            : base(path, mode, access, share, bufferSize)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
            : base(path, mode, access, share, bufferSize, options)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync)
            : base(path, mode, access, share, bufferSize, useAsync)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream(SafeFileHandle handle, FileAccess access, string path)
            : base(handle, access)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream(SafeFileHandle handle, FileAccess access, int bufferSize, string path)
            : base(handle, access, bufferSize)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync, string path)
            : base(handle, access, bufferSize, isAsync)
        {
            Path = path;
        }

        /// <inheritdoc />
        public override void Close()
        {
            Interlocked.Increment(ref ProperlyClosed);
            base.Close();
        }

        /// <inheritdoc />
        public override int Read(byte[] array, int offset, int count)
        {
            var stopwatch = StopwatchSlim.Start();

            var result = base.Read(array, offset, count);

            Interlocked.Add(ref _readDurationInTicks,  stopwatch.Elapsed.Ticks);
            return result;
        }

        /// <inheritdoc />
        public override void Write(byte[] array, int offset, int count)
        {
            var stopwatch = StopwatchSlim.Start();
            base.Write(array, offset, count);
            Interlocked.Add(ref _writeDurationInTicks, stopwatch.Elapsed.Ticks);
        }

        /// <inheritdoc />
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var stopwatch = StopwatchSlim.Start();
            var result = await base.ReadAsync(buffer, offset, count, cancellationToken);
            Interlocked.Add(ref _readDurationInTicks, stopwatch.Elapsed.Ticks);
            return result;
        }

        /// <inheritdoc />
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var stopwatch = StopwatchSlim.Start();
            await base.WriteAsync(buffer, offset, count, cancellationToken);
            Interlocked.Add(ref _writeDurationInTicks, stopwatch.Elapsed.Ticks);
        }

        /// <nodoc />
        ~TrackingFileStream()
        {
            Interlocked.Increment(ref Leaked);
            // Saving the last leaked path for potential tracing purposes.
            LastLeakedFilePath = Path;

            try
            {
                // In some cases finalization of the file stream instance fails with FileStreamHandlePosition error
                // crashing the service.
                // This code is intended to show what file is caused the issue helping the team to understand the nature of the error.
                Dispose(false);
            }
            catch (IOException e)
            {
                throw new IOException($"Failed to finalize FileStream with path '{Path}'.", e);
            }

        }
    }
}
