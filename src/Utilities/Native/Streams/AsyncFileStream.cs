// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.Streams
{
    /// <summary>
    /// High-level stream operation as requested by some caller. It is an error for a caller to request a new operation
    /// when there is an operation in progress (not <see cref="StreamOperation.None"/>).
    /// </summary>
    public enum StreamOperation
    {
        /// <summary>
        /// No operation. A new operation may be started.
        /// </summary>
        None,

        /// <nodoc />
        Read,

        /// <nodoc />
        Write,

        /// <nodoc />
        Seek,

        /// <nodoc />
        Flush,

        /// <nodoc />
        Dispose,
    }

    /// <summary>
    /// Internal background operation, independent from the current <see cref="StreamOperation"/>.
    /// There may be a background operation even with <see cref="StreamOperation.None"/> set,
    /// such as if a read just finished and emptied a buffer (we refill the buffer in the background).
    /// </summary>
    public enum StreamBackgroundOperation
    {
        /// <summary>
        /// No background operation; another can start.
        /// </summary>
        None,

        /// <summary>
        /// Buffer fill (read).
        /// </summary>
        Fill,

        /// <summary>
        /// Buffer flush (write).
        /// </summary>
        Flush,
    }

    /// <summary>
    /// Usability of the underlying stream for background (unbuffered) operations.
    /// </summary>
    public enum StreamUsability
    {
        /// <summary>
        /// Stream is open and usable.
        /// </summary>
        Usable,

        /// <summary>
        /// Terminal, unusable state for a stream which has encountered a read or write error.
        /// When this occurs, further background operations can no longer be started.
        /// </summary>
        Broken,

        /// <summary>
        /// Terminal, unusable state for a stream which has gracefully reached end-of-file (for reads).
        /// When this occurs, further background operations can no longer be started.
        /// </summary>
        EndOfFileReached,
    }

    /// <summary>
    /// An <see cref="AsyncFileStream"/> is a <see cref="Stream"/> that satisfies read and write operations using async I/O operations.
    /// Since <see cref="Stream"/> has the concept of a current position, this class is the combination of an underlying <see cref="IAsyncFile"/>
    /// (which does not have a current position; each read or write is issued with a start offset), a current stream position, and a buffer
    /// that tracks that current position.
    ///
    /// Async read and write operations on this stream block the calling thread if and only if the kernel chooses to complete the request
    /// synchronously. This is a stronger guarantee than the BCL <see cref="FileStream"/>, since it blocks in user-mode subject to the particulars
    /// of buffer state and buffer sizes.
    ///
    /// Since this stream co-ordinates async I/O with a buffer and a stateful file position,
    /// it is an error to issue concurrent read / write / flush / seek requests.
    ///
    /// Note that unlike the BCL <see cref="FileStream"/>, this stream does not guarantee that reads or writes return the complete number of
    /// bytes requested (which is consistent with the NT kernel and basic <see cref="Stream"/> guarantees).
    ///
    /// This stream implements read / write-ahead, in which empty (full) buffers are filled or flushed asynchronously. This means that
    /// a caller performing interleaved compute and I/O can simply assume that buffer-sized reads / writes will not lead to I/O stalls
    /// (rather than needing to overlap compute and I/O explicitly).
    ///
    /// This stream is not suitable for write-sharing (i.e., co-coordinating with concurrent writers to the same file). Due to read-ahead,
    /// and write-ahead, it is not possible for a caller to synchronize reads and writes with the concurrent writer. For example, this stream
    /// will latch to an end-of-file status after a single kernel indication, even if some writer extends the file.
    /// </summary>
    public abstract class AsyncFileStream : Stream, IIOCompletionTarget, IDisposable
    {
        private const int BufferSizeDefault = 64 * 1024;

        /// <summary>
        /// Default buffer size
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2211:NonConstantFieldsShouldNotBeVisible", Justification = "Type used in tests")]
        public static int DefaultBufferSize = BufferSizeDefault;

        private readonly Task<BackgroundOperationSlot> m_completedBackgroundOperationTask;

        /// <summary>
        /// Lock which must be held while attempting to transition the current <see cref="StreamOperation"/>.
        /// This lock is released during async phases of an operation; instead the (async?) operation owner that made the transition
        /// holds onto an <see cref="StreamOperationToken"/>.
        /// </summary>
        private readonly object m_operationLock = new object();

        /// <summary>
        /// Lock which must be held to serialize the starting and completion of background operations, i.e., actual I/O.
        /// </summary>
        private readonly object m_backgroundOperationLock = new object();

        /// <summary>
        /// Buffer for read or write operations. All reads and writes are satisfied via this buffer.
        /// Async IO operations issued to the kernel fill or flush the buffer.
        /// </summary>
        protected readonly FileBuffer internalBuffer;

        /// <summary>
        /// Backing file.
        /// </summary>
        public IAsyncFile File => m_file;

        /// <summary>
        /// Default buffer size of 64K. Reads and writes are issued at this granularity.
        /// </summary>
        /// <remarks>
        /// Experimentally, a 64K buffer size (or some multiple thereof) is required for high throughput on NT,
        /// particularly if there are many concurrent requests on the same volume.
        /// </remarks>
        public int BufferSize => BufferSizeDefault;

        private StreamOperation m_currentOperation = StreamOperation.None;
        private StreamBackgroundOperation m_currentBackgroundOperation = StreamBackgroundOperation.None;
        private StreamUsability m_usability;
        private IOException m_brokenStreamException;
        private TaskCompletionSource<BackgroundOperationSlot> m_currentBackgroundOperationCompletionSource;
        private readonly bool m_ownsFile;
        private readonly IAsyncFile m_file;

        /// <summary>
        /// Position in the file corresponding to what callers have read or written up to (the buffer may be ahead or behind this).
        /// </summary>
        private long m_position;

        /// <summary>
        /// Position in the file corresponding to read / write buffer. This position is used to start new background reads / fills.
        /// </summary>
        private long m_bufferPosition;

        /// <summary>
        /// Cached file length. File length is queried from underlying storage at most once per stream. See remarks about write sharing.
        /// </summary>
        private long m_cachedLength = -1;

        /// <nodoc />
        internal AsyncFileStream(IAsyncFile file, bool ownsFile, int bufferSize = BufferSizeDefault)
        {
            Contract.Requires(file != null);

            m_completedBackgroundOperationTask = Task.FromResult(new BackgroundOperationSlot(this));

            internalBuffer = new FileBuffer(bufferSize);
            m_ownsFile = ownsFile;
            m_file = file;
        }

        /// <summary>
        /// <see cref="Stream.CanSeek"/>
        /// </summary>
        public override bool CanSeek => true;

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                using (StreamOperationToken op = StartNewOperation(StreamOperation.Dispose))
                {
                    BackgroundOperationSlot slot = op.WaitForBackgroundOperationSlot();
                    if (slot.Usability != StreamUsability.Broken)
                    {
                        FlushOrDiscardBufferAsync(slot).GetAwaiter().GetResult();
                    }

                    if (m_ownsFile)
                    {
                        m_file.Close();
                    }

                    if (slot.Usability == StreamUsability.Broken)
                    {
                        throw slot.ThrowExceptionForBrokenStream();
                    }
                }
            }

            // TODO: Consider FailFast for the !disposing (finalizer) case.
            internalBuffer.Dispose();
        }

        /// <summary>
        /// <see cref="Stream.Flush"/>
        /// </summary>
        public override void Flush()
        {
            using (StreamOperationToken op = StartNewOperation(StreamOperation.Flush))
            {
                BackgroundOperationSlot slot = op.WaitForBackgroundOperationSlot();
                if (slot.Usability == StreamUsability.Broken)
                {
                    throw slot.ThrowExceptionForBrokenStream();
                }

                // TODO: Shouldn't drop the read buffer unless seeking to a new position.
                FlushOrDiscardBufferAndResetPositionAsync(op, slot, m_position).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// <see cref="Stream.Length"/>
        /// </summary>
        public override long Length
        {
            get
            {
                long length = Volatile.Read(ref m_cachedLength);

                if (length == -1)
                {
                    Interlocked.CompareExchange(ref m_cachedLength, m_file.GetCurrentLength(), comparand: -1);
                    length = m_cachedLength;
                }

                return length;
            }
        }

        /// <summary>
        /// <see cref="Stream.Position"/>
        /// </summary>
        public override long Position
        {
            get
            {
                return Volatile.Read(ref m_position);
            }

            set
            {
                Contract.Assume(value >= 0);
                Seek(value, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// <see cref="Stream.Seek(long, SeekOrigin)"/>
        /// </summary>
        public override long Seek(long offset, SeekOrigin origin)
        {
            using (StreamOperationToken token = StartNewOperation(StreamOperation.Seek))
            {
                BackgroundOperationSlot slot = token.WaitForBackgroundOperationSlot();
                if (slot.Usability == StreamUsability.Broken)
                {
                    throw slot.ThrowExceptionForBrokenStream();
                }

                long offsetFromStart;
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        Contract.Assume(offset >= 0, "Attempted to seek to a negative offset");
                        offsetFromStart = offset;
                        break;
                    case SeekOrigin.Current:
                        Contract.Assume(m_position >= offset, "Attempted to seek (relative to current) to a negative offset");
                        offsetFromStart = m_position + offset;
                        break;
                    case SeekOrigin.End:
                        throw new NotSupportedException("Seeking relative to stream end is not supported");
                    default:
                        throw Contract.AssertFailure("Unknwon SeekOrigin");
                }

                if (m_position != offsetFromStart)
                {
                    FlushOrDiscardBufferAndResetPositionAsync(token, slot, offsetFromStart).GetAwaiter().GetResult();
                }

                return offsetFromStart;
            }
        }

        /// <summary>
        /// Inheritor-provided implementation for a 'flush or discard' operation.
        /// A read-only stream discards while a write-only stream flushes.
        /// </summary>
        protected abstract Task FlushOrDiscardBufferAsync(BackgroundOperationSlot slot);

        private async Task FlushOrDiscardBufferAndResetPositionAsync(StreamOperationToken token, BackgroundOperationSlot slot, long newPosition)
        {
            Contract.Requires(slot.Usability != StreamUsability.Broken);
            Analysis.IgnoreArgument(token);

            m_position = newPosition;
            await FlushOrDiscardBufferAsync(slot);
            lock (m_backgroundOperationLock)
            {
                // Buffer is now empty, and so its position should be in sync with the virtual position (recall how both begin at zero on stream open).
                m_bufferPosition = m_position;

                // Buffer position changed, so an end-of-file indication is no longer valid.
                if (m_usability == StreamUsability.EndOfFileReached)
                {
                    m_usability = StreamUsability.Usable;
                }
                else
                {
                    Contract.Assume(m_usability == StreamUsability.Usable, "m_usability == slot.Usability is not Broken");
                }
            }
        }

        /// <summary>
        /// <see cref="Stream.SetLength(long)"/>
        /// </summary>
        public override void SetLength(long value) => throw new NotSupportedException("Changing stream length is not supported. Write to the stream instead.");

        /// <summary>
        /// Synchronous write. This is discouraged; <see cref="Stream.WriteAsync(byte[],int,int)"/> should be used when possible.
        /// (equivalent to blocking on <see cref="Stream.WriteAsync(byte[],int,int)"/>).
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count).Wait();

        /// <summary>
        /// Synchronous read. This is discouraged; <see cref="Stream.ReadAsync(byte[],int,int)"/> should be used when possible.
        /// (equivalent to blocking on <see cref="Stream.ReadAsync(byte[],int,int)"/>).
        /// </summary>
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).Result;

        /// <inheritdoc />
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) =>
            throw new NotSupportedException("Old-style Begin / End operations are not supported.");

        /// <inheritdoc />
        public override int EndRead(IAsyncResult asyncResult) => throw new NotSupportedException("Old-style Begin / End operations are not supported.");

        /// <inheritdoc />
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state) =>
            throw new NotSupportedException("Old-style Begin / End operations are not supported.");

        /// <inheritdoc />
        public override void EndWrite(IAsyncResult asyncResult) => throw new NotSupportedException("Old-style Begin / End operations are not supported.");

        /// <summary>
        /// Attempts to transition into a new operation. This is atomic from <see cref="StreamOperation.None"/>.
        /// </summary>
        protected StreamOperationToken StartNewOperation(StreamOperation next)
        {
            Contract.Requires(next != StreamOperation.None);

            lock (m_operationLock)
            {
                if (m_currentOperation != StreamOperation.None)
                {
                    Contract.Assume(
                        false,
                        I($"Attempted to start a {next:G} operation, but a {m_currentOperation:G} operation is already in progress on the same stream"));
                }

                m_currentOperation = next;
            }

            return new StreamOperationToken(this);
        }

        /// <summary>
        /// Attempts to complete the current operation. This is atomic to <see cref="StreamOperation.None"/>.
        /// </summary>
        private void CompleteOperation(StreamOperationToken token)
        {
            Analysis.IgnoreArgument(token);

            lock (m_operationLock)
            {
                if (m_currentOperation == StreamOperation.None)
                {
                    Contract.Assume(false, "Attempted to complete an operation, but no operation was in progress");
                }

                // Dispose is terminal.
                if (m_currentOperation != StreamOperation.Dispose)
                {
                    m_currentOperation = StreamOperation.None;
                }
            }
        }

        /// <summary>
        /// Adjusts the current stream position based on having read or written data in the buffer.
        /// </summary>
        protected void AdvancePosition(StreamOperationToken token, int advance)
        {
            Contract.Requires(advance >= 0);
            Analysis.IgnoreArgument(token);

            m_position += advance;
            Contract.Assume(m_position >= 0);
        }

        /// <summary>
        /// Starts a background operation. This causes the <see cref="internalBuffer"/> to fill or flush.
        /// It must be ensured that no other background operation will be in progress, and that the stream is still usable after it.
        /// - First start an operation with <see cref="StartNewOperation"/>; this reserves the right to start the next background operation.
        /// - Then, wait for the current background operation, if any. We've established another one will not follow it due to the prior point.
        /// - Then, respond to the current stream usability (EOF, broken, etc.) as updated by the prior operation.
        /// </summary>
        private unsafe void StartBackgroundOperation(BackgroundOperationSlot slot, StreamBackgroundOperation nextOperation)
        {
            Contract.Requires(nextOperation != StreamBackgroundOperation.None);
            Analysis.IgnoreArgument(slot);

            lock (m_backgroundOperationLock)
            {
                if (m_usability != StreamUsability.Usable)
                {
                    Contract.Assume(false, "Attempting to start a background operation on an unusable stream: " + m_usability.ToString("G"));
                }

                Contract.Assume(
                    m_currentBackgroundOperation == StreamBackgroundOperation.None,
                    "Background operation already in progress; wait on it first?");
                Contract.Assume(m_currentBackgroundOperationCompletionSource == null);
                m_currentBackgroundOperation = nextOperation;
            }

            // Now actually start the async operation.
            // Note that the callback to 'this' (IIOCompletionTarget) can happen on this same stack
            byte* pinnedBuffer;
            int operationLength;
            switch (nextOperation)
            {
                case StreamBackgroundOperation.Fill:
                    internalBuffer.LockForFill(out pinnedBuffer, out operationLength);
                    m_file.ReadOverlapped(this, pinnedBuffer, operationLength, m_bufferPosition);
                    break;
                case StreamBackgroundOperation.Flush:
                    internalBuffer.LockForFlush(out pinnedBuffer, out operationLength);
                    m_file.WriteOverlapped(this, pinnedBuffer, operationLength, m_bufferPosition);
                    break;
                default:
                    throw Contract.AssertFailure("Unhandled StreamBackgroundOperation");
            }
        }

        /// <summary>
        /// Callback for the completion of a background operation's IO.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        void IIOCompletionTarget.OnCompletion(FileAsyncIOResult result)
        {
            // Note that this may be called on the same stack as StartBackgroundOperation (sync completion),
            Contract.Assume(result.Status != FileAsyncIOStatus.Pending);

            bool failed = result.Status == FileAsyncIOStatus.Failed;
            if (!failed)
            {
                // Note, that FileAsyncIOResult constructor can't enforce this check because in some other cases
                // 0 transfered bytes is ok for a successful IO operation.
                Contract.Assert(result.BytesTransferred > 0, "Zero bytes transferred is a failure indication (otherwise can be confused with EOF)");
            }

            TaskCompletionSource<BackgroundOperationSlot> completionSource;
            lock (m_backgroundOperationLock)
            {
                // Capture the completion source to use outside of the lock.
                completionSource = m_currentBackgroundOperationCompletionSource;

                switch (m_currentBackgroundOperation)
                {
                    case StreamBackgroundOperation.Fill:
                        m_bufferPosition += result.BytesTransferred;
                        internalBuffer.FinishFillAndUnlock(numberOfBytesFilled: result.BytesTransferred);
                        break;
                    case StreamBackgroundOperation.Flush:
                        m_bufferPosition += result.BytesTransferred;
                        internalBuffer.FinishFlushAndUnlock(numberOfBytesFlushed: result.BytesTransferred);
                        break;
                    case StreamBackgroundOperation.None:
                        Contract.Assume(false, "Unexpected I/O completion (no background operation in progress)");
                        throw new InvalidOperationException("Unreachable");
                    default:
                        throw Contract.AssertFailure("Unhandled StreamBackgroundOperation");
                }

                if (failed)
                {
                    StreamUsability newUsability;
                    if (result.ErrorIndicatesEndOfFile)
                    {
                        newUsability = StreamUsability.EndOfFileReached;
                    }
                    else
                    {
                        newUsability = StreamUsability.Broken;

                        m_brokenStreamException = new IOException(
                            "An error occurred while reading or writing to a file stream. The stream will no longer be usable.",
                            new NativeWin32Exception(result.Error));
                    }

                    m_usability = newUsability;
                }
                else
                {
                    Contract.Assume(m_usability == StreamUsability.Usable);
                }

                m_currentBackgroundOperation = StreamBackgroundOperation.None;
                m_currentBackgroundOperationCompletionSource = null;
            }

            // Since the lock is no longer held, it is safe to resume any waiters (note that they may run on this stack).
            if (completionSource != null)
            {
                completionSource.SetResult(new BackgroundOperationSlot(this));
            }
        }

        /// <summary>
        /// Waits for the current background operation to complete, if any.
        /// </summary>
        protected Task<BackgroundOperationSlot> WaitForBackgroundOperationSlotAsync(StreamOperationToken token)
        {
            Analysis.IgnoreArgument(token);

            lock (m_backgroundOperationLock)
            {
                if (m_currentBackgroundOperation == StreamBackgroundOperation.None)
                {
                    return m_completedBackgroundOperationTask;
                }

                if (m_currentBackgroundOperationCompletionSource == null)
                {
                    m_currentBackgroundOperationCompletionSource = new TaskCompletionSource<BackgroundOperationSlot>();
                }

                return m_currentBackgroundOperationCompletionSource.Task;
            }
        }

        /// <summary>
        /// Waits for the current background operation to complete, if any.
        /// </summary>
        protected BackgroundOperationSlot WaitForBackgroundOperationSlot(StreamOperationToken token)
        {
            return WaitForBackgroundOperationSlotAsync(token).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Token representing an outstanding operation. This acts like an async-capable lock; further operations
        /// cannot start until this operation is finished by being diposed (consider a <c>using</c> block).
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        protected readonly struct StreamOperationToken : IDisposable
        {
            private readonly AsyncFileStream m_stream;

            internal StreamOperationToken(AsyncFileStream stream)
            {
                Contract.Requires(stream != null);
                m_stream = stream;
            }

            /// <summary>
            /// Waits for the current background operation to complete, if any.
            /// </summary>
            public Task<BackgroundOperationSlot> WaitForBackgroundOperationSlotAsync() => m_stream.WaitForBackgroundOperationSlotAsync(this);

            /// <summary>
            /// Waits for the current background operation to complete, if any.
            /// </summary>
            public BackgroundOperationSlot WaitForBackgroundOperationSlot() => m_stream.WaitForBackgroundOperationSlot(this);

            /// <summary>
            /// Adjusts the current stream position based on having read or written data in the buffer.
            /// </summary>
            public void AdvancePosition(int advance) => m_stream.AdvancePosition(this, advance);

            /// <summary>
            /// Completes this outstanding operation.
            /// </summary>
            public void Dispose() => m_stream.CompleteOperation(this);
        }

        /// <summary>
        /// Represents the availability to run a background operation; this availablity is established with e.g.
        /// <see cref="StreamOperationToken.WaitForBackgroundOperationSlotAsync"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        protected readonly struct BackgroundOperationSlot
        {
            private readonly AsyncFileStream m_stream;

            internal BackgroundOperationSlot(AsyncFileStream stream)
            {
                Contract.Requires(stream != null);
                m_stream = stream;
            }

            /// <summary>
            /// Starts a background operation. No background operations may be in progress;
            /// ensure that is not the case by waiting for background operation completion if one may be in progress.
            /// </summary>
            public void StartBackgroundOperation(StreamBackgroundOperation nextOperation) => m_stream.StartBackgroundOperation(this, nextOperation);

            /// <summary>
            /// Indicates usability of the stream. This field should be read only when there are no outstanding background operations,
            /// since usability is affected by the completion of background operations; so, this is exposed on the <see cref="BackgroundOperationSlot"/>
            /// obtained by an operation by waiting on background operation completion.
            /// </summary>
            public StreamUsability Usability => m_stream.m_usability;

            /// <summary>
            /// Throws the stored exception for a stream that is in the broken state (IO error other than EOF).
            /// </summary>
            public IOException ThrowExceptionForBrokenStream()
            {
                Contract.Requires(Usability == StreamUsability.Broken);
                Contract.Assume(m_stream.m_brokenStreamException != null);
                throw m_stream.m_brokenStreamException;
            }
        }
    }
}
