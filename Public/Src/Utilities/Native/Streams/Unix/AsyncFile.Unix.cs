// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Native.IO.Unix.FileSystemUnix;

namespace BuildXL.Native.Streams.Unix
{
    /// <inheritdoc />
    public sealed class AsyncFileUnix : IAsyncFile
    {
        private readonly SafeFileHandle m_handle;
        private readonly FileKind m_kind;
        private readonly string m_path;
        private readonly bool m_ownsHandle;
        private readonly FileDesiredAccess m_access;
        private readonly FileStream m_fileStream;

        /// <summary>
        /// Creates an <see cref="AsyncFileUnix"/> to wrap an existing native handle compatible with Unix based systems.
        /// </summary>
        public AsyncFileUnix(
            SafeFileHandle handle,
            FileDesiredAccess access,
            bool ownsHandle,
            string path = null,
            FileKind kind = FileKind.File)
        {
            Contract.Requires(handle != null);
            Contract.Requires(!handle.IsInvalid);
            m_handle = handle;
            m_kind = kind;
            m_path = path;
            m_ownsHandle = ownsHandle;
            m_access = access;

            m_fileStream = new FileStream(
                m_path,
                FileMode.Open,
                FileDesiredAccessToFileAccess(access),
                FileShare.ReadWrite,
                DefaultBufferSize,
                FileOptions.Asynchronous);
        }

        /// <inheritdoc />
        public SafeFileHandle Handle => m_fileStream.SafeFileHandle;

        /// <inheritdoc />
        public string Path => m_path;

        /// <inheritdoc />
        public FileDesiredAccess Access => m_access;

        /// <inheritdoc />
        public bool CanRead => (m_access & FileDesiredAccess.GenericRead) != 0;

        /// <inheritdoc />
        public bool CanWrite => (m_access & FileDesiredAccess.GenericWrite) != 0;

        /// <inheritdoc />
        public FileKind Kind => m_kind;

        /// <nodoc />
        public void Dispose()
        {
            if (m_ownsHandle)
            {
                m_handle.Dispose();
            }

            m_fileStream.Dispose();
        }

        /// <nodoc />
        public void Close()
        {
            if (m_ownsHandle)
            {
                m_handle.Close();
            }

            m_fileStream.Close();
        }

        /// <inheritdoc />
        public long GetCurrentLength()
        {
            Contract.Requires(Kind == FileKind.File);
            return m_fileStream.Length;
        }

        /// <inheritdoc />
        public AsyncFileStream CreateReadableStream(bool closeFileOnStreamClose = false)
        {
            Contract.Requires(CanRead);
            Contract.Requires(Kind == FileKind.File);
            return new AsyncReadableFileStream(this, closeFileOnStreamClose);
        }

        /// <inheritdoc />
        public unsafe void ReadOverlapped(
            IIOCompletionTarget target,
            byte* pinnedBuffer,
            int bytesToRead,
            long fileOffset)
        {
            FileAsyncIOResult result;
            if (GetCurrentLength() == 0 || fileOffset >= GetCurrentLength())
            {
                result = new FileAsyncIOResult(FileAsyncIOStatus.Failed, bytesTransferred: 0, error: NativeIOConstants.ErrorHandleEof);
            }
            else
            {
                lock(m_fileStream)
                {
                    try
                    {
                        byte[] buffer = new byte[bytesToRead];
                        m_fileStream.Seek(fileOffset, SeekOrigin.Begin);
                        int bytesRead = m_fileStream.Read(buffer, 0, bytesToRead);

                        for (int i = 0; i < bytesRead; i++)
                        {
                            pinnedBuffer[i] = buffer[i];
                        }

                        result = new FileAsyncIOResult(FileAsyncIOStatus.Succeeded, bytesTransferred: bytesRead, error: NativeIOConstants.ErrorSuccess);
                    }
                    catch (Exception ex)
                    {
                        int errorCode = (int)NativeErrorCodeForException(ex);
                        result = new FileAsyncIOResult(FileAsyncIOStatus.Failed, bytesTransferred: 0, error: errorCode);
                    }
                }
            }

            QueueCompletionNotification(target, result);
        }

        private unsafe void QueueCompletionNotification(IIOCompletionTarget target, FileAsyncIOResult result)
        {
            var notificationArgs = new IOCompletionNotificationArgs
            {
                Result = result,
                Target = target,
            };

            ThreadPool.QueueUserWorkItem(
                state =>
                {
                    var stateArgs = (IOCompletionNotificationArgs)state;
                    stateArgs.Target.OnCompletion(stateArgs.Result);
                },
                notificationArgs);
        }

        /// <inheritdoc />
        public unsafe Task<FileAsyncIOResult> ReadAsync(
            byte[] buffer,
            int bytesToRead,
            long fileOffset)
        {
            Contract.Requires(buffer != null);
            Contract.Requires(bytesToRead > 0);
            Contract.Requires(bytesToRead <= buffer.Length);

            TaskIOCompletionTarget target = TaskIOCompletionTarget.CreateAndPinBuffer(buffer);
            ReadOverlapped(target, target.GetPinnedBuffer(), bytesToRead, fileOffset);
            return target.IOCompletionTask;
        }

        /// <inheritdoc />
        public unsafe void WriteOverlapped(
            IIOCompletionTarget target,
            byte* pinnedBuffer,
            int bytesToWrite,
            long fileOffset)
        {
            Analysis.IgnoreArgument(target);
            Analysis.IgnoreArgument((IntPtr)pinnedBuffer);
            Analysis.IgnoreArgument(bytesToWrite);
            Analysis.IgnoreArgument(fileOffset);
            Analysis.IgnoreArgument(this);
            throw new NotImplementedException();
        }
    }
}
