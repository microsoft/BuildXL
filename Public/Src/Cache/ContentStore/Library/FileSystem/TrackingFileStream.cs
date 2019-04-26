// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Security.AccessControl;
using System.Threading;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Cache.ContentStore.FileSystem
{
    /// <summary>
    /// Special tracking file stream that fails with more readable error message if unhandled error occurs in the finalizer of the instance.
    /// </summary>
    internal class TrackingFileStream : FileStream
    {
        public static long Constructed;
        public static long ProperlyClosed;
        public static long Leaked;

        private string _path;

        private string Path
        {
            get => _path;
            set
            {
                Interlocked.Increment(ref Constructed);
                _path = value;
            }
        }

        /// <inheritdoc />
        public TrackingFileStream([NotNull] string path, FileMode mode)
            : base(path, mode)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream([NotNull] string path, FileMode mode, FileAccess access)
            : base(path, mode, access)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream([NotNull] string path, FileMode mode, FileAccess access, FileShare share)
            : base(path, mode, access, share)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream([NotNull] string path, FileMode mode, FileAccess access, FileShare share, int bufferSize)
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
        public TrackingFileStream([NotNull] string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync)
            : base(path, mode, access, share, bufferSize, useAsync)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream(string path, FileMode mode, FileSystemRights rights, FileShare share, int bufferSize, FileOptions options, FileSecurity fileSecurity)
            : base(path, mode, rights, share, bufferSize, options, fileSecurity)
        {
        }

        /// <inheritdoc />
        public TrackingFileStream(string path, FileMode mode, FileSystemRights rights, FileShare share, int bufferSize, FileOptions options)
            : base(path, mode, rights, share, bufferSize, options)
        {
            Path = path;
        }

        /// <inheritdoc />
        [Obsolete]
        public TrackingFileStream(IntPtr handle, FileAccess access)
            : base(handle, access)
        {
        }

        /// <inheritdoc />
        [Obsolete]
        public TrackingFileStream(IntPtr handle, FileAccess access, bool ownsHandle)
            : base(handle, access, ownsHandle)
        {
        }

        /// <inheritdoc />
        [Obsolete]
        public TrackingFileStream(IntPtr handle, FileAccess access, bool ownsHandle, int bufferSize)
            : base(handle, access, ownsHandle, bufferSize)
        {
        }

        /// <inheritdoc />
        [Obsolete]
        public TrackingFileStream(IntPtr handle, FileAccess access, bool ownsHandle, int bufferSize, bool isAsync)
            : base(handle, access, ownsHandle, bufferSize, isAsync)
        {
        }

        /// <inheritdoc />
        public TrackingFileStream([NotNull] SafeFileHandle handle, FileAccess access, string path)
            : base(handle, access)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream([NotNull] SafeFileHandle handle, FileAccess access, int bufferSize, string path)
            : base(handle, access, bufferSize)
        {
            Path = path;
        }

        /// <inheritdoc />
        public TrackingFileStream([NotNull] SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync, string path)
            : base(handle, access, bufferSize, isAsync)
        {
            Path = path;
        }

        /// <inheritdoc />
        public override void Close()
        {
            Interlocked.Increment(ref ProperlyClosed);
            GC.SuppressFinalize(this);
        }

        ~TrackingFileStream()
        {
            Interlocked.Increment(ref Leaked);

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
