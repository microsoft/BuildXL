// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.Streams;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Processes.Internal
{
    internal delegate bool StreamDataReceived(string data);

    internal sealed unsafe class AsyncPipeReader : IDisposable, IIOCompletionTarget
    {
        private readonly object m_lock = new object();
        private State m_state = State.Initialized;

        private Queue<string> m_messageQueue = new Queue<string>();
        private readonly StreamDataReceived m_userCallBack;
        private bool m_bLastCarriageReturn;

        private readonly IAsyncFile m_file;

        private readonly int m_byteBufferSize;
        private readonly PooledObjectWrapper<byte[]> m_pooledByteBufferWrapper;
        private byte[] ByteBuffer => m_pooledByteBufferWrapper.Instance;
        private byte* m_byteBufferPtr;
        private GCHandle m_byteBufferPin;

        private readonly PooledObjectWrapper<char[]> m_pooledCharBufferWrapper;
        private char[] CharBuffer => m_pooledCharBufferWrapper.Instance;
        private readonly Decoder m_decoder;
        private readonly PooledObjectWrapper<StringBuilder> m_pooledStringBuilderWrapper;
        private StringBuilder StringBuilderInstace => m_pooledStringBuilderWrapper.Instance;

        private TaskCompletionSource<bool> m_completion;

        private enum State
        {
            Initialized,
            Reading,

            /// <summary>
            /// State for co-ordinated cleanup; we can't abandon pinned buffers or the underlying file while a read is in progress.
            /// </summary>
            Stopping,

            Stopped,
        }

        /// <summary>
        /// Creates a new AsyncStreamReader for the given stream.  The
        /// character encoding is set by encoding and the buffer size,
        /// in number of 16-bit characters, is set by bufferSize.
        /// </summary>
        public AsyncPipeReader(
            IAsyncFile file,
            StreamDataReceived callback,
            Encoding encoding,
            int bufferSize)
        {
            Contract.Requires(file != null);
            Contract.Requires(file.CanRead);
            Contract.Requires(file.Kind == FileKind.Pipe);
            Contract.Requires(encoding != null);
            Contract.Requires(bufferSize > 128);
            m_file = file;
            m_userCallBack = callback;
            m_decoder = encoding.GetDecoder();
            m_pooledByteBufferWrapper = Pools.GetByteArray(bufferSize);
            m_byteBufferSize = bufferSize;
            m_byteBufferPin = GCHandle.Alloc(ByteBuffer, GCHandleType.Pinned);
            m_byteBufferPtr = (byte*)m_byteBufferPin.AddrOfPinnedObject();

            var maxCharsPerBuffer = encoding.GetMaxCharCount(bufferSize);
            m_pooledCharBufferWrapper = Pools.GetCharArray(maxCharsPerBuffer);
            m_pooledStringBuilderWrapper = Pools.GetStringBuilder();
            StringBuilderInstace.Clear();
            StringBuilderInstace.EnsureCapacity(maxCharsPerBuffer * 2);
        }

        public void Dispose()
        {
            bool waitForCompletion = false;
            lock (m_lock)
            {
                switch (m_state)
                {
                    case State.Initialized:
                        // No background reads are happening, so stop right away.
                        OnStopped();
                        break;
                    case State.Stopped:
                        // Nothing to do.
                        break;
                    default:
                        // Allow an existing read to finish, but don't start another one.
                        m_state = State.Stopping;
                        waitForCompletion = true;
                        break;
                }
            }

            // We guarantee that outstanding I/O has completed when Dispose returns.
            // This is consistent with AsyncFileStream, and is required for allowing
            // safe disposal of the backing IOCompletionManager (which cannot dispose with outstanding IO).
            if (waitForCompletion)
            {
                WaitUntilEofAsync(ignoreCancelation: true).Wait();
            }

            lock (m_lock)
            {
                var state = m_state;
                Contract.Assert(state == State.Stopped || state == State.Stopping, "After disposal AsyncPipeReader must be in Stopping or Stopped state");
            }

            m_pooledCharBufferWrapper.Dispose();
            m_pooledByteBufferWrapper.Dispose();
            m_pooledStringBuilderWrapper.Dispose();
        }

        private void OnStopped()
        {
            m_state = State.Stopped;
            m_byteBufferPtr = null;
            m_byteBufferPin.Free();
            m_file.Close();
        }

        /// <summary>
        /// User calls BeginRead to start the asynchronous read
        /// </summary>
        internal void BeginReadLine()
        {
            lock (m_lock)
            {
                if (m_state == State.Initialized)
                {
                    m_state = State.Reading;
                }
                else
                {
                    Contract.Assume(m_state != State.Stopping && m_state != State.Stopped, "Disposed");
                }
            }

            // We start reading outside of the lock, since the read may complete synchronously.
            // File offset is ignored since we're reading a pipe.
            m_file.ReadOverlapped(this, m_byteBufferPtr, m_byteBufferSize, fileOffset: 0);
        }

        /// <summary>
        /// This is the async callback function. Only one thread could/should call this.
        /// </summary>
        void IIOCompletionTarget.OnCompletion(FileAsyncIOResult asyncIOResult)
        {
            Contract.Assume(asyncIOResult.Status != FileAsyncIOStatus.Pending);

            bool cancel = false;
            lock (m_lock)
            {
                switch (m_state)
                {
                    case State.Initialized:
                        Contract.Assume(false, "Read completion can only happen once in the Reading state");
                        throw new InvalidOperationException("Unreachable");
                    case State.Reading:
                        break;
                    case State.Stopping:
                        // If Stopping, we had to defer reaching Stopped until an outstanding read completed - which is now.
                        // We throw away remaining data in the pipe (call WaitForEofAsync if all data is needed before dispose).
                        // Any current waiters for WaitForEofAsync will also resume (though possibly with an exception, due to this pre-EOF cancelation).
                        OnStopped();

                        // Signaling cancelation needs to happen outside of the lock (otherwise continuations on the completion source
                        // may run synchronously, and have the lock by accident).
                        cancel = true;
                        break;
                    case State.Stopped:
                        Contract.Assume(false, "The Stopped state should not be reached while a read is outstanding");
                        throw new InvalidOperationException("Unreachable");
                    default:
                        throw Contract.AssertFailure("Unhandled state");
                }
            }

            if (cancel)
            {
                SignalCompletion(reachedEof: false);
                return;
            }

            int byteLen;
            if (asyncIOResult.Status == FileAsyncIOStatus.Failed)
            {
                // Treat failures as EOF.
                // TODO: This is a bad thing to do, but is what the original AsyncStreamReader was doing.
                byteLen = 0;
            }
            else
            {
                Contract.Assume(!asyncIOResult.ErrorIndicatesEndOfFile, "End-of-file appears as a failure status");
                byteLen = asyncIOResult.BytesTransferred;
                Contract.Assume(byteLen > 0);
            }

            if (byteLen == 0)
            {
                // We're at EOF, we won't call this function again from here on.
                lock (m_lock)
                {
                    if (StringBuilderInstace.Length != 0)
                    {
                        m_messageQueue.Enqueue(StringBuilderInstace.ToString());
                        StringBuilderInstace.Length = 0;
                    }

                    m_messageQueue.Enqueue(null);
                }

                try
                {
                    // UserCallback could throw, but we should still signal EOF
                    try
                    {
                        FlushMessageQueue();
                    }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                    catch
                    {
                        // We are in an invalid state (maybe the process got killed or crashed).
                        // As the comment above says the could throw.
                        // Make sure we are catching the exception without crashing.
                    }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                }
                finally
                {
                    lock (m_lock)
                    {
                        // No more reads or callbacks will occur, so enter the Stopped state and clean up.
                        OnStopped();
                    }

                    m_messageQueue = null;
                    SignalCompletion(reachedEof: true);
                }
            }
            else
            {
                int charLen = m_decoder.GetChars(ByteBuffer, 0, byteLen, CharBuffer, 0);
                GetLinesFromCharBuffers(charLen);

                // File offset is ignored since we're reading a pipe.
                m_file.ReadOverlapped(this, m_byteBufferPtr, m_byteBufferSize, fileOffset: 0);
            }
        }

        private void SignalCompletion(bool reachedEof)
        {
            TaskCompletionSource<bool> existingCompletion = Interlocked.Exchange(
                ref m_completion,
                reachedEof ? BoolTask.TrueCompletionSource : BoolTask.FalseCompletionSource);

            if (existingCompletion != null)
            {
                existingCompletion.SetResult(reachedEof);
            }
        }

        private void GetLinesFromCharBuffers(int len)
        {
            // skip a beginning '\n' character of new block if last block ended with '\r'
            var i = m_bLastCarriageReturn && len > 0 && CharBuffer[0] == '\n' ? 1 : 0;
            m_bLastCarriageReturn = false;

            while (i < len)
            {
                // Note the following common line feed chars:
                // \n - UNIX   \r\n - DOS   \r - Mac
                var eolPosition = Array.FindIndex(CharBuffer, i, len - i, c => c == '\r' || c == '\n');
                if (eolPosition == -1)
                {
                    // The line end was not found, remember the buffer to use next
                    StringBuilderInstace.Append(CharBuffer, i, len - i);
                    break;
                }

                if (StringBuilderInstace.Length > 0)
                {
                    // If there is a remainder from the previous buffer, put them together
                    StringBuilderInstace.Append(CharBuffer, i, eolPosition - i);
                    lock (m_lock)
                    {
                        m_messageQueue.Enqueue(StringBuilderInstace.ToString());
                    }

                    StringBuilderInstace.Clear();
                }
                else
                {
                    // Use the newly found string
                    lock (m_lock)
                    {
                        m_messageQueue.Enqueue(new string(CharBuffer, i, eolPosition - i));
                    }
                }

                i = eolPosition + 1;

                // If the last character of the buffer is a CR, remember to skip LF at the start of next buffer
                // or remember that we saw it if this is the last character of the buffer
                if (CharBuffer[eolPosition] == '\r')
                {
                    if (i < len && CharBuffer[i] == '\n')
                    {
                        i++;
                    }
                    else if (i == len)
                    {
                        m_bLastCarriageReturn = true;
                    }
                }
            }

            FlushMessageQueue();
        }

        private void FlushMessageQueue()
        {
            while (true)
            {
                // When we call BeginReadLine, we also need to flush the queue
                // So there could be a race between the ReadBuffer and BeginReadLine
                // We need to take lock before DeQueue.
                lock (m_lock)
                {
                    var state = m_state;
                    if (state == State.Stopped || state == State.Stopping)
                    {
                        // May have switched to stopping/stopped state after entering this method.
                        // In that case, don't flush message queue and just return
                        return;
                    }

                    if (m_messageQueue.Count == 0)
                    {
                        return;
                    }

                    string s = m_messageQueue.Dequeue();
                    bool? ret = m_userCallBack?.Invoke(s);
                    if (ret.HasValue && !ret.Value)
                    {
                        // This allows for the callback to indicate an error state and break the
                        // processing loop.
                        // Error encountered. Stop processing.
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Wait until we hit EOF. This is called from DetouredProcess.WaitForExit
        /// We will lose some information if we don't do this.
        /// </summary>
        internal Task WaitUntilEofAsync(bool ignoreCancelation = false)
        {
            TaskCompletionSource<bool> completion = Volatile.Read(ref m_completion);

            Task<bool> task;
            if (completion == null)
            {
                Interlocked.CompareExchange(ref m_completion, new TaskCompletionSource<bool>(), comparand: null);
                task = Volatile.Read(ref m_completion).Task;
            }
            else
            {
                task = completion.Task;
            }

            if (ignoreCancelation)
            {
                return task;
            }
            else
            {
                return task.ContinueWith(
                    t =>
                    {
                        bool reachedEof = t.Result;
                        if (!reachedEof)
                        {
                            throw new BuildXLException("Async reading of a pipe was canceled (did not read all data)");
                        }
                    });
            }
        }
    }
}
