// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Native.IO.Windows.FileSystemWin;

namespace BuildXL.Native.Streams.Windows
{
    /// <inheritdoc />
    public sealed class AsyncFileWin : IAsyncFile
    {
        private readonly SafeFileHandle m_handle;
        private readonly FileKind m_kind;
        private readonly string m_path;
        private readonly IOCompletionManager m_ioCompletionManager;
        private readonly bool m_ownsHandle;
        private readonly FileDesiredAccess m_access;

        /// <summary>
        /// Creates an <see cref="AsyncFileWin"/> to wrap an existing native handle compatible with Windows based systems.
        /// </summary>
        public AsyncFileWin(
            SafeFileHandle handle,
            FileDesiredAccess access,
            bool ownsHandle,
            IIOCompletionManager ioCompletionManager = null,
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
            m_ioCompletionManager = (IOCompletionManager)(ioCompletionManager ?? IOCompletionManager.Instance);
            m_ioCompletionManager.BindFileHandle(handle);
        }

        /// <inheritdoc />
        public SafeFileHandle Handle => m_handle;

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
        }

        /// <nodoc />
        public void Close()
        {
            if (m_ownsHandle)
            {
                m_handle.Dispose();
            }
        }

        /// <inheritdoc />
        public long GetCurrentLength()
        {
            Contract.Requires(Kind == FileKind.File);
            return GetFileLengthByHandle(m_handle);
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
            long fileOffset) => m_ioCompletionManager.ReadFileOverlapped(target, m_handle, pinnedBuffer, bytesToRead, fileOffset);

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
