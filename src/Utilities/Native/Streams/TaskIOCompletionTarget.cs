// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BuildXL.Utilities;

namespace BuildXL.Native.Streams
{
    /// <summary>
    /// Implementation of <see cref="IIOCompletionTarget"/> that represents the eventual completion as a <see cref="IOCompletionTask"/>.
    /// Since the task latches to the completed state, this target is only suitable for a single operation.
    /// This implementation also manages the pinning of a byte buffer for use by the related IO operation. The buffer is pinned on creation and unpinned on completion
    /// </summary>
    public sealed class TaskIOCompletionTarget : IIOCompletionTarget
    {
        private readonly TaskCompletionSource<FileAsyncIOResult> m_completion = new TaskCompletionSource<FileAsyncIOResult>();
        private GCHandle m_pinningHandle;

        private TaskIOCompletionTarget(byte[] buffer)
        {
            Contract.Requires(buffer.Length > 0);
            m_pinningHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        }

        /// <nodoc />
        ~TaskIOCompletionTarget()
        {
            if (m_pinningHandle.IsAllocated)
            {
                ExceptionUtilities.FailFast(
                    "TaskIOCompletionTarget leaked a pinned buffer; expected OnCompletion to be called",
                    new InvalidOperationException());
            }
        }

        /// <summary>
        /// Creates a new target with a new pinning handle for the given buffer.
        /// </summary>
        public static TaskIOCompletionTarget CreateAndPinBuffer(byte[] buffer)
        {
            Contract.Requires(buffer != null);
            Contract.Requires(buffer.Length > 0);
            return new TaskIOCompletionTarget(buffer);
        }

        /// <summary>
        /// Task which tracks IO completion.
        /// </summary>
        public Task<FileAsyncIOResult> IOCompletionTask
        {
            get { return m_completion.Task; }
        }

        /// <summary>
        /// Gets a pointer to the pinned buffer, valid until IO completion or disposal.
        /// </summary>
        public unsafe byte* GetPinnedBuffer()
        {
            lock (this)
            {
                Contract.Assume(m_pinningHandle.IsAllocated, "Buffer already unpinned due to completion or disposal.");
                return (byte*) m_pinningHandle.AddrOfPinnedObject();
            }
        }

        /// <inheritdoc />
        public void OnCompletion(FileAsyncIOResult asyncIOResult)
        {
            lock (this)
            {
                Contract.Assume(m_pinningHandle.IsAllocated);
                m_pinningHandle.Free();
            }

            m_completion.SetResult(asyncIOResult);
        }
    }
}
