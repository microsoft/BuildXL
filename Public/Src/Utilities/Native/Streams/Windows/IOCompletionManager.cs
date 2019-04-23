// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Native.IO.Windows;
using BuildXL.Utilities;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.Streams.Windows
{
    /// <inheritdoc />
    public sealed class IOCompletionManager : IIOCompletionManager
    {
        /// <summary>
        /// Global completion manager usable by all threads to request new async reads / writes.
        /// </summary>
        /// <remarks>
        /// Non-global instances are primarily for testing; an application should only need one instance even with a small number of completion threads.
        /// </remarks>
        public static readonly IOCompletionManager Instance = new IOCompletionManager(name: "default");

        private IOCompletionTraceHook m_traceHook;

        /// <inheritdoc />
        public IOCompletionTraceHook TraceHook => m_traceHook;

        /// <summary>
        /// Completion key used to indicate that a completion port is being closed.
        /// </summary>
        private static readonly IntPtr s_queueCloseCompletionKey = new IntPtr(1);

        private readonly SafeIOCompletionPortHandle m_completionPort;

        private readonly Thread[] m_completionPortWorkers;
        private readonly OverlappedPool m_overlappedPool;

        /// <summary>
        /// We track usage of the completion port with this integer field. This allows immediate disposal
        /// of the port as the last worker exits, without having to call <see cref="Thread.Join()"/> in the finalizer.
        /// </summary>
        private int m_completionPortRefCount;

        /// <summary>
        /// Creates an I/O completion port with associated worker threads.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods")]
        public IOCompletionManager(int? numberOfCompletionPortThreads = null, string name = "<unnamed>")
        {
            Contract.Requires(!numberOfCompletionPortThreads.HasValue || numberOfCompletionPortThreads > 0);

            int effectiveNumberOfCompletionPortThreads = numberOfCompletionPortThreads ?? GetDefaultNumberOfCompletionPortThreads();
            Contract.Assume(effectiveNumberOfCompletionPortThreads > 0);

            m_completionPort = FileSystemWin.CreateIOCompletionPort();
            m_completionPortRefCount = 1; // Referenced by this manager.
            m_completionPortWorkers = new Thread[effectiveNumberOfCompletionPortThreads];
            m_overlappedPool = new OverlappedPool();

            long handleValue = m_completionPort.DangerousGetHandle().ToInt64();
            ThreadStart workerEntry = CompletionWorkerThreadProc;
            for (int i = 0; i < effectiveNumberOfCompletionPortThreads; i++)
            {
                var newThread = new Thread(workerEntry)
                                {
                                    Name = I($"IOCompletionManagerWorker (port handle 0x{handleValue:X}; '{name}')"),
                                    IsBackground = true
                                };
                m_completionPortWorkers[i] = newThread;
                newThread.Start();
            }
        }

        /// <inheritdoc />
        public IOCompletionTraceHook RemoveTraceHook()
        {
            return Interlocked.Exchange(ref m_traceHook, null);
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public IOCompletionTraceHook StartTracingCompletion()
        {
            var hook = new IOCompletionTraceHook(this);
            IOCompletionTraceHook existing = Interlocked.CompareExchange(ref m_traceHook, hook, comparand: null);
            Contract.Assume(existing == null, "Trace hook instance already installed");
            return hook;
        }

        private unsafe void TraceStartIfEnabled(TaggedOverlapped* overlapped, IIOCompletionTarget target)
        {
            IOCompletionTraceHook traceHook = Volatile.Read(ref m_traceHook);
            if (traceHook != null)
            {
                traceHook.TraceStart(overlapped->GetUniqueId(), target);
            }
        }

        private unsafe void TraceCompletionIfEnabled(TaggedOverlapped* overlapped)
        {
            IOCompletionTraceHook traceHook = Volatile.Read(ref m_traceHook);
            traceHook?.TraceComplete(overlapped->GetUniqueId());
        }

        /// <summary>
        /// Computes the default number of dedicated completion port threads (not in the thread-pool) used by default to dispatch completions to the thread-pool.
        /// This grows slowly relative to available processors, since one thread can handle many completions fairly quickly but queue rate might
        /// grow a bit on bigger machines.
        /// </summary>
        private static int GetDefaultNumberOfCompletionPortThreads()
        {
            return Math.Max(Environment.ProcessorCount / 8, 1);
        }

        /// <inheritdoc />
        public void BindFileHandle(SafeFileHandle handle)
        {
            Contract.Requires(handle != null);
            Contract.Requires(!handle.IsInvalid);

            FileSystemWin.BindFileHandleToIOCompletionPort(handle, m_completionPort, completionKey: IntPtr.Zero);
        }

        /// <inheritdoc />
        public unsafe void ReadFileOverlapped(
            IIOCompletionTarget target,
            SafeFileHandle handle,
            byte* pinnedBuffer,
            int bytesToRead,
            long fileOffset)
        {
            Contract.Requires(target != null);
            Contract.Requires(handle != null && !handle.IsInvalid);
            Contract.Requires(pinnedBuffer != null);

            TaggedOverlapped* overlapped = m_overlappedPool.ReserveOverlappedWithTarget(target);
            TraceStartIfEnabled(overlapped, target);

            bool needOverlappedRelease = true;
            try
            {
                FileAsyncIOResult result = FileSystemWin.ReadFileOverlapped(
                    handle,
                    pinnedBuffer,
                    bytesToRead,
                    fileOffset,
                    (Overlapped*)overlapped);

                if (result.Status != FileAsyncIOStatus.Pending)
                {
                    Contract.Assert(
                        result.Status == FileAsyncIOStatus.Succeeded ||
                        result.Status == FileAsyncIOStatus.Failed);

                    // We could call the target directly.
                    // However, since the target may itself issue more I/O, we need to prevent unbounded stack growth.
                    // TODO: We could set a recursion-limit to allow some fraction of repeated IOs to complete synchronously, without
                    //       queueing to the threadpool. Sync completions are the common case for files cached in memory.
                    ReleaseOvelappedAndQueueCompletionNotification(overlapped, result);
                }

                // At this point overlapped is either needed (pending status)
                // or already released by ReleaseOvelappedAndQueueCompletionNotification
                needOverlappedRelease = false;
            }
            finally
            {
                if (needOverlappedRelease)
                {
                    IIOCompletionTarget releasedTarget = ReleaseOverlappedAndGetTarget(overlapped);
                    Contract.Assume(releasedTarget == target);
                }
            }
        }

        private unsafe void CompletionWorkerThreadProc()
        {
            {
                int count;
                do
                {
                    count = Volatile.Read(ref m_completionPortRefCount);
                    if (count < 1)
                    {
                        // Manager disposed before this thread started.
                        return;
                    }
                }
                while (Interlocked.CompareExchange(ref m_completionPortRefCount, count + 1, comparand: count) != count);
            }

            try
            {
                while (true)
                {
                    FileSystemWin.IOCompletionPortDequeueResult result = FileSystemWin.GetQueuedCompletionStatus(m_completionPort);

                    Contract.Assume(
                        result.Status != FileSystemWin.IOCompletionPortDequeueStatus.CompletionPortClosed,
                        "We terminate all workers before closing the port (otherwise we risk a handle-recycle race).");

                    Contract.Assert(result.Status == FileSystemWin.IOCompletionPortDequeueStatus.Succeeded);

                    if (result.DequeuedOverlapped == null)
                    {
                        // Completion port is being closed; this is a poison message.
                        Contract.Assume(result.CompletionKey == s_queueCloseCompletionKey);
                        break;
                    }

                    // The OVERLAPPED* attached to each packet is unique to each I/O request. It should be one
                    // that we allocated earlier with AllocateOverlapped. We took care to place a request identifier
                    // immediately after the OVERLAPPED, so we can find the completion target.
                    Overlapped* deqeuedOverlapped = result.DequeuedOverlapped;
                    var taggedOverlapped = (TaggedOverlapped*)deqeuedOverlapped;

                    ReleaseOvelappedAndQueueCompletionNotification(taggedOverlapped, result.CompletedIO);
                }

                DecrementCompletionPortRefCount();
            }
            catch (Exception ex)
            {
                ExceptionUtilities.FailFast("Catastrophic failure in I/O completion worker", ex);
                throw;
            }
        }

        private unsafe void ReleaseOvelappedAndQueueCompletionNotification(TaggedOverlapped* overlapped, FileAsyncIOResult result)
        {
            // We can now find the external-code callback that needs notification for this request.
            // We can't allow it to block this thread, which is dedicated to servicing the completion port.
            // Were it to block, we could starve I/O completion; blocking on other I/O completions could be a deadlock.
            // So, we guarantee that IIOCompletionTargets run on thread-pool threads - they suffer no special restrictions.
            IIOCompletionTarget target = ReleaseOverlappedAndGetTarget(overlapped);

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

        private unsafe IIOCompletionTarget ReleaseOverlappedAndGetTarget(TaggedOverlapped* overlapped)
        {
            TraceCompletionIfEnabled(overlapped);
            return m_overlappedPool.ReleaseOverlappedAndGetTarget(overlapped);
        }

        private void DecrementCompletionPortRefCount()
        {
            int countAfterRelease = Interlocked.Decrement(ref m_completionPortRefCount);
            Contract.Assume(countAfterRelease > 0);
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="IOCompletionManager"/> class.
        /// (important for the global <see cref="Instance"/>)
        /// </summary>
        ~IOCompletionManager()
        {
            DisposeInternal();
        }

        /// <summary>
        /// Disposes this manager, including its completion port and workers.
        /// This should be performed only when there is no outstanding I/O.
        /// Behavior given incomplete I/O is undefined.
        /// </summary>
        public void Dispose()
        {
            DisposeInternal();
            GC.SuppressFinalize(this);
        }

        private void DisposeInternal()
        {
            // Cleanup is delicate. We must ensure that no workers are blocked on calls to GetQueuedCompletionStatus and that
            // no workers will newly call GetQueuedCompletionStatus, before it is safe to close the completion port handle.
            // Though we can inspect the result of GetQueuedCompletionStatus to determine that a port was closed *while blocked*,
            // we only depend on that as a sanity check that this cleanup works correctly; otherwise, we could have been unlucky
            // and instead tried to newly block concurrently with handle close (thus blocking on a potentially-recycled handle value).
            // So, we co-ordinate to ensure that all workers threads have exited before the completion port is closed.

            // Send enough poison messages to the port so that all workers wake up and exit.
            // We do this even when !disposing i.e., in the finalizer; this is okay since m_completionPort is a CriticalFinalizerObject
            // and therefore will have its finalizer called after this one (FileStream also depends on this for its SafeFileHandle, for example).
            for (int i = 0; i < m_completionPortWorkers.Length; i++)
            {
                FileSystemWin.PostQueuedCompletionStatus(m_completionPort, s_queueCloseCompletionKey);
            }

            // After all threads are finished, the reference count should end up at 1 (the manager reference).
            // We may have to yield to other threads to let them finish. Since we don't want to use any
            // synchronization calls in the finializer thread, just poll and yield.
            SpinWait spinner = default(SpinWait);
            while (Volatile.Read(ref m_completionPortRefCount) > 1)
            {
                spinner.SpinOnce();
            }

            // All workers are expected to exit (or not start). The ref count will eventually reach 1.
            // Note that via these acrobatics we avoid blocking the finalizer thread on worker exit or calling anything on Thread.
            m_completionPortRefCount = 0;

            // All threads are completed and we can get rid of the pool.
            // This will prevent any new IO from starting.
            m_overlappedPool.Dispose();

            // The port can be closed as well
            m_completionPort.Dispose();
        }
    }
}
