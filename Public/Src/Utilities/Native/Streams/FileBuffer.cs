// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;

namespace BuildXL.Native.Streams
{
    /// <summary>
    /// Buffer for operatins on an <see cref="AsyncFileStream" />. This class manages the contents and pinning of a backing byte buffer.
    /// Since this buffer is targetted at async operation in which buffer filling / flushing (actual I/O requests) are fully separate from
    /// stream reads and writes, it tracks a <see cref="BufferState" /> (the buffer may be locked during a background operation to fill or flush it;
    /// it is either a read buffer or a write buffer if not locked) and a <see cref="BufferLockReason" /> (fill or flush, when locked).
    ///
    /// This class is not thread safe.
    /// </summary>
    public sealed class FileBuffer : IDisposable
    {
        /// <summary>
        /// Overall state. This state indicates if the buffer is presently usable for reading or writing.
        /// </summary>
        public enum BufferState
        {
            /// <summary>
            /// Empty buffer.
            /// </summary>
            Empty,

            /// <summary>
            /// Buffer contains readable bytes.
            /// </summary>
            Read,

            /// <summary>
            /// Buffer contains written bytes.
            /// </summary>
            Write,

            /// <summary>
            /// Buffer is locked, and not readable or writable until an associated flush or fill operation completes.
            /// </summary>
            Locked,
        }

        /// <summary>
        /// Completion status of a read or write operation.
        /// </summary>
        public enum BufferOperationStatus
        {
            /// <summary>
            /// Operation completed, without filling or exhausting the buffer.
            /// </summary>
            CapacityRemaining,

            /// <summary>
            /// Buffer is empty following a read; refill required to get more bytes.
            /// </summary>
            ReadExhausted,

            /// <summary>
            /// Write buffer is full, or a read cannot proceed yet (flush required to become a read buffer).
            /// </summary>
            FlushRequired,
        }

        /// <summary>
        /// Reason for being in the <see cref="BufferState.Locked"/> state.
        /// </summary>
        private enum BufferLockReason
        {
            /// <summary>
            /// Not locked.
            /// </summary>
            None,

            /// <summary>
            /// Fill (background read from disk)
            /// </summary>
            Fill,

            /// <summary>
            /// Flush (background write to disk)
            /// </summary>
            Flush,
        }

        private GCHandle m_lockedStateGCHandle;
        private byte[] Buffer => m_pooledBufferWrapper.Instance;
        private readonly PooledObjectWrapper<byte[]> m_pooledBufferWrapper;
        private readonly int m_bufferSize;
        private BufferState m_currentBufferState;
        private BufferLockReason m_lockReason;
        private int m_bufferFilledSize;
        private int m_bufferPosition;

        /// <nodoc />
        public FileBuffer(int bufferSize)
        {
            Contract.Requires(bufferSize > 0);
            m_pooledBufferWrapper = Pools.ByteArrayPool.GetInstance(bufferSize);
            m_bufferSize = bufferSize;
        }

        /// <summary>
        /// Current state. See <see cref="BufferState"/>
        /// </summary>
        public BufferState State
        {
            get { return m_currentBufferState; }
        }

        /// <summary>
        /// Buffer size
        /// </summary>
        public int Capacity
        {
            get { return m_bufferSize; }
        }

        /// <summary>
        /// Attempts to read bytes. This may require a flush (if presently a write buffer) or a fill
        /// (if no readable bytes are available), as indicated by the returned status.
        /// </summary>
        public BufferOperationStatus Read(byte[] buffer, int offset, int count, out int bytesRead)
        {
            Contract.Requires(State != BufferState.Locked);

            if (count == 0)
            {
                bytesRead = 0;
                return BufferOperationStatus.CapacityRemaining;
            }

            if (m_currentBufferState == BufferState.Write)
            {
                bytesRead = 0;
                return BufferOperationStatus.FlushRequired;
            }

            if (m_currentBufferState == BufferState.Empty)
            {
                bytesRead = 0;
                return BufferOperationStatus.ReadExhausted;
            }

            Contract.Assert(m_currentBufferState == BufferState.Read);

            int maxReadable = m_bufferFilledSize - m_bufferPosition;
            int bytesToCopy = Math.Min(count, maxReadable);
            Contract.Assume(bytesToCopy > 0, "BufferType should have been empty.");
            System.Buffer.BlockCopy(Buffer, m_bufferPosition, buffer, offset, bytesToCopy);
            m_bufferPosition += bytesToCopy;
            Contract.Assert(m_bufferPosition <= m_bufferFilledSize);

            bytesRead = bytesToCopy;
            if (m_bufferPosition == m_bufferFilledSize)
            {
                Discard();
                return BufferOperationStatus.ReadExhausted;
            }
            else
            {
                return BufferOperationStatus.CapacityRemaining;
            }
        }

        /// <summary>
        /// Attempts to write bytes. This may require a flush (if presently full and a write buffer).
        /// If presently a read buffer, the read buffer contents are silently discarded.
        /// </summary>
        public BufferOperationStatus Write(byte[] buffer, int offset, int count, out int bytesWritten)
        {
            Contract.Requires(State != BufferState.Locked);

            if (count == 0)
            {
                bytesWritten = 0;
                return BufferOperationStatus.CapacityRemaining;
            }

            if (m_currentBufferState == BufferState.Read)
            {
                // TODO: Counter?
                Discard();
            }

            Contract.Assert(m_currentBufferState == BufferState.Write || m_currentBufferState == BufferState.Empty);
            m_currentBufferState = BufferState.Write;

            if (m_bufferPosition == m_bufferSize)
            {
                bytesWritten = 0;
                return BufferOperationStatus.FlushRequired;
            }

            int maxWritable = m_bufferSize - m_bufferPosition;
            Contract.Assert(maxWritable > 0);
            int bytesToCopy = Math.Min(count, maxWritable);
            System.Buffer.BlockCopy(buffer, offset, Buffer, m_bufferPosition, bytesToCopy);
            m_bufferPosition += bytesToCopy;
            Contract.Assert(m_bufferPosition <= m_bufferSize);

            bytesWritten = bytesToCopy;
            return (m_bufferPosition == m_bufferSize) ? BufferOperationStatus.FlushRequired : BufferOperationStatus.CapacityRemaining;
        }

        private void Lock(BufferLockReason lockReason)
        {
            Contract.Requires(lockReason != BufferLockReason.None);
            Contract.Requires(State == BufferState.Empty || State == BufferState.Write);

            m_lockReason = lockReason;
            m_currentBufferState = BufferState.Locked;

            Contract.Assume(!m_lockedStateGCHandle.IsAllocated);
            m_lockedStateGCHandle = GCHandle.Alloc(Buffer, GCHandleType.Pinned);
        }

        private void Unlock(BufferState newState)
        {
            Contract.Requires(State == BufferState.Locked);
            Contract.Requires(newState != BufferState.Locked);

            m_lockReason = BufferLockReason.None;
            m_currentBufferState = newState;
            m_lockedStateGCHandle.Free();
        }

        /// <summary>
        /// Transitions the buffer to the <see cref="BufferState.Locked"/> state, and returns a buffer pointer
        /// for a native fill operation. <see cref="FinishFillAndUnlock"/> should be called to complete this.
        /// </summary>
        public unsafe void LockForFill(out byte* pinnedBuffer, out int pinnedBufferLength)
        {
            Contract.Requires(State == BufferState.Empty);

            Lock(BufferLockReason.Fill);
            pinnedBuffer = (byte*)m_lockedStateGCHandle.AddrOfPinnedObject();
            pinnedBufferLength = m_bufferSize;
        }

        /// <summary>
        /// Transitions the buffer to the <see cref="BufferState.Locked"/> state, and returns a buffer pointer
        /// for a native fill operation. <see cref="FinishFlushAndUnlock"/> should be called to complete this
        /// and enter <see cref="BufferState.Read"/>.
        /// </summary>
        public unsafe void LockForFlush(out byte* pinnedBuffer, out int bytesToFlush)
        {
            Contract.Requires(State == BufferState.Write);

            Lock(BufferLockReason.Flush);
            pinnedBuffer = (byte*)m_lockedStateGCHandle.AddrOfPinnedObject();
            bytesToFlush = m_bufferPosition;
        }

        /// <summary>
        /// Finishes a flush operation. This transitions the buffer out of the <see cref="BufferState.Locked"/> state.
        /// </summary>
        public void FinishFlushAndUnlock(int numberOfBytesFlushed)
        {
            Contract.Requires(State == BufferState.Locked);
            Contract.Requires(numberOfBytesFlushed >= 0);

            Contract.Assume(m_lockReason == BufferLockReason.Flush);

            bool entirelyFlushed = numberOfBytesFlushed == m_bufferPosition;
            Contract.Assume(numberOfBytesFlushed <= m_bufferPosition, "Too many bytes flushed; number of bytes to flush is indicated by LockForFlush");
            if (entirelyFlushed)
            {
                m_bufferFilledSize = 0;
                m_bufferPosition = 0;
            }

            // TODO: Must handle partial flushes for writable AsyncFileStreams to work.
            Contract.Assume(entirelyFlushed);

            Unlock(entirelyFlushed ? BufferState.Empty : BufferState.Write);
        }

        /// <summary>
        /// Finishes a fill operation. This transitions the buffer out of the <see cref="BufferState.Locked"/> state.
        /// </summary>
        public void FinishFillAndUnlock(int numberOfBytesFilled)
        {
            Contract.Requires(State == BufferState.Locked);
            Contract.Requires(numberOfBytesFilled >= 0 && numberOfBytesFilled <= Capacity);

            Contract.Assume(m_lockReason == BufferLockReason.Fill);

            m_bufferFilledSize = numberOfBytesFilled;
            m_bufferPosition = 0;
            Unlock(numberOfBytesFilled == 0 ? BufferState.Empty : BufferState.Read);
        }

        /// <summary>
        /// Discards the current buffer, if doing so does not cause data loss (i.e., there are not un-flushed written bytes in the buffer).
        /// </summary>
        public void Discard()
        {
            Contract.Requires(State == BufferState.Read || State == BufferState.Empty);

            m_bufferFilledSize = 0;
            m_bufferPosition = 0;
            m_currentBufferState = BufferState.Empty;
        }

        /// <summary>
        /// Dispose the current FileBuffer.  All pooled resources should be returned.
        /// </summary>
        public void Dispose()
        {
            m_pooledBufferWrapper.Dispose();
        }
    }
}
