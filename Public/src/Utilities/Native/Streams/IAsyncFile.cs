// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Native.Streams
{
    /// <summary>
    /// Type of file-like object represented by the underlying native handle.
    /// </summary>
    public enum FileKind
    {
        /// <summary>
        /// Seekable file.
        /// </summary>
        File,

        /// <summary>
        /// Non-seekable pipe.
        /// </summary>
        Pipe,
    }

    /// <summary>
    /// Represents a file opened for async (overlapped) I/O.
    /// An <see cref="IAsyncFile"/> can support concurrent read / write operations. It does not have a 'current position'
    /// unlike a synchronous file handle; instead each operation stands on its own and specifies the file offset at which the
    /// operation should take place. Consequently, no buffering is provided (each operation maps directly to a request to the kernel).
    ///
    /// Multiple <see cref="AsyncFileStream"/> instances can be created on top of a single <see cref="IAsyncFile"/>. Each stream maintains its own
    /// current position and read/write buffer, and so supports only a single outstanding operation.
    /// </summary>
    public interface IAsyncFile : IDisposable
    {
        /// <summary>
        /// Underlying file handle.
        /// </summary>
        SafeFileHandle Handle { get; }

        /// <summary>
        /// Path used to open the file. This may be null (unknown) if a path was not provided to the constructor (just an existing handle).
        /// </summary>
        string Path { get; }
        
        /// <summary>
        /// Access used to open the underlying native handle.
        /// </summary>
        FileDesiredAccess Access { get; }

        /// <summary>
        /// Indicates if the native handle was opened with read access.
        /// </summary>
        bool CanRead { get; }
        
        /// <summary>
        /// Indicates if the native handle was opened with write access.
        /// </summary>
        bool CanWrite { get; }

        /// <summary>
        /// Type of file-like object represented by the underlying native handle.
        /// </summary>
        FileKind Kind { get; }

        /// <nodoc />
        void Close();

        /// <summary>
        /// Queries the current length. This value may change during execution if the file is being written.
        /// This is not allowed for <see cref="FileKind.Pipe"/>, since pipes do not have a defined length.
        /// </summary>
        long GetCurrentLength();

        /// <summary>
        /// Creates an <see cref="AsyncFileStream"/> for read-only access. The stream is initially positioned at file offset 0.
        /// This is not allowed for <see cref="FileKind.Pipe"/>, since pipes are non-seekable (cannot support independent stream offsets).
        /// </summary>
        AsyncFileStream CreateReadableStream(bool closeFileOnStreamClose = false);

        /// <summary>
        /// See <see cref="IIOCompletionManager.ReadFileOverlapped"/>.
        /// Offset is ignored for <see cref="FileKind.Pipe"/> files.
        /// </summary>
        unsafe void ReadOverlapped(IIOCompletionTarget target, byte* pinnedBuffer, int bytesToRead, long fileOffset);

        /// <summary>
        /// Reads up to the specified number of bytes into the provided buffer.
        /// Offset is ignored for <see cref="FileKind.Pipe"/> files.
        /// </summary>
        unsafe Task<FileAsyncIOResult> ReadAsync(byte[] buffer, int bytesToRead, long fileOffset);

        /// <summary>
        /// Writes out the buffer in overlapped fashion
        /// </summary>
        unsafe void WriteOverlapped(IIOCompletionTarget target, byte* pinnedBuffer, int bytesToWrite, long fileOffset);
    }
}
