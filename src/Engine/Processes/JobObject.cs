// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Native.Processes;
using BuildXL.Native.Processes.Windows;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Microsoft.Win32.SafeHandles;
using SafeIOCompletionPortHandle = BuildXL.Native.IO.Windows.SafeIOCompletionPortHandle;
#if !FEATURE_SAFE_PROCESS_HANDLE
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
#endif

namespace BuildXL.Processes
{
    /// <summary>
    /// Wraps a native Job object
    /// </summary>
    /// <remarks>
    /// This implementation provides async waits for job completion (i.e., when a job transitions from non-empty to empty).
    /// Job completion is defined here as a permanently latching event; a job is allowed to complete exactly once (and so adding
    /// processes to the job is disallowed after that first completion). Therefore, the following usages are safe
    /// - Adding a single initial process to the job and allowing that process to spawn children (they atomically inherit membership)
    /// - Adding multiple suspended processes to the job and then resuming them (none can exit prematurely).
    /// </remarks>
    public unsafe class JobObject : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Nested jobs are only supported on Win8/Server2012 or higher.
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/hh448388(v=vs.85).aspx
        /// </summary>
        public static readonly bool OSSupportsNestedJobs = Native.Processes.ProcessUtilities.OSSupportsNestedJobs();

        private static readonly object s_syncRoot = new object();
        private static readonly Lazy<CompletionPortDrainer> s_completionPortDrainer = Lazy.Create(() => new CompletionPortDrainer());
        private static bool s_terminateOnCloseOnCurrentProcessJob;

        /// <summary>
        /// Counter for generating unique completion keys (to map completion port messages to JobObjects).
        /// </summary>
        private static long s_currentCompletionKey;

        /// <summary>
        /// Completion key of this job (completion port messages with this key should affect this instance).
        /// </summary>
        private readonly IntPtr m_completionKey;

        private readonly object m_lock = new object();

        #region Fields protected by m_lock

        /// <summary>
        /// When set, the job has completed. <see cref="m_doneEvent"/> remains set but need not be used.
        /// No additional processes can be added.
        /// </summary>
        private bool m_done;

        /// <summary>
        /// Event indicating a transition to <c>m_done == true</c>
        /// This event is populated lazily, only if <see cref="WaitAsync"/> is called before completion.
        /// </summary>
        private ManualResetEvent m_doneEvent;

        /// <summary>
        /// When set, the job has been disposed and can no longer service new waits or processes.
        /// </summary>
        private bool m_disposed;

        #endregion

        #region Accounting types

        /// <summary>
        /// Contains I/O accounting information for a process or a job object, for a particular type of IO (e.g. read or write).
        /// These counters include all operations performed by all processes ever associated with the job.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public struct IOTypeCounters
        {
            /// <summary>
            /// Number of operations performed (independent of size).
            /// </summary>
            public ulong OperationCount;

            /// <summary>
            /// Total bytes transferred (regardless of the number of operations used to transfer them).
            /// </summary>
            public ulong TransferCount;
        }

        /// <summary>
        /// Contains I/O accounting information for a process or a job object.
        /// These counters include all operations performed by all processes ever associated with the job.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public struct IOCounters
        {
            /// <summary>
            /// Counters for read operations.
            /// </summary>
            public IOTypeCounters ReadCounters;

            /// <summary>
            /// Counters for write operations.
            /// </summary>
            public IOTypeCounters WriteCounters;

            /// <summary>
            /// Counters for other operations (not classified as either read or write).
            /// </summary>
            public IOTypeCounters OtherCounters;

            internal IOCounters(IO_COUNTERS nativeCounters)
            {
                ReadCounters.OperationCount = nativeCounters.ReadOperationCount;
                ReadCounters.TransferCount = nativeCounters.ReadTransferCount;

                WriteCounters.OperationCount = nativeCounters.WriteOperationCount;
                WriteCounters.TransferCount = nativeCounters.WriteTransferCount;

                OtherCounters.OperationCount = nativeCounters.OtherOperationCount;
                OtherCounters.TransferCount = nativeCounters.OtherTransferCount;
            }

            /// <summary>
            /// Computes the aggregate I/O performed (sum of the read, write, and other counters).
            /// </summary>
            [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
            [Pure]
            public IOTypeCounters GetAggregateIO()
            {
                return new IOTypeCounters()
                       {
                           OperationCount = ReadCounters.OperationCount + WriteCounters.OperationCount + OtherCounters.OperationCount,
                           TransferCount = ReadCounters.TransferCount + WriteCounters.TransferCount + OtherCounters.TransferCount,
                       };
            }
        }

        /// <summary>
        /// Accounting information for resources used by the job so far.
        /// </summary>
        /// <remarks>
        /// This accounting information contains aggregate, monotonic counters rolled up across all processes in the job
        /// (as the job progresses, these counters only increase).
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public struct AccountingInformation
        {
            /// <summary>
            /// Counters for IO transfer.
            /// </summary>
            public IOCounters IO;

            /// <summary>
            /// User-mode execution time. Note that this counter increases as threads in the job execute (it is not equivalent to wall clock time).
            /// </summary>
            public TimeSpan UserTime;

            /// <summary>
            /// Kernel-mode execution time. Note that this counter increases as threads in the job execute (it is not equivalent to wall clock time).
            /// </summary>
            public TimeSpan KernelTime;

            /// <summary>
            /// Peak memory usage considering all processes (highest point-in-time sum of the memory usage of all job processes).
            /// </summary>
            public ulong PeakMemoryUsage;

            /// <summary>
            /// Number of processes started within or added to the job. This includes both running and already-terminated processes, if any.
            /// </summary>
            public uint NumberOfProcesses;
        }

        #endregion

        /// <summary>
        /// Creates a job
        /// </summary>
        /// <remarks>
        /// If name is null, an anonymous object is created.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "ExtendedLimitInformation")]
        internal JobObject(string name)
            : base(true)
        {
            IntPtr jobHandle = Native.Processes.ProcessUtilities.CreateJobObject(IntPtr.Zero, name);
            if (jobHandle == IntPtr.Zero)
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "Unable to create job object.");
            }

            SetHandle(jobHandle);

            m_completionKey = GetNextCompletionKey();

            var limitInfo = default(JOBOBJECT_ASSOCIATE_COMPLETION_PORT);
            limitInfo.CompletionKey = m_completionKey;

            // Note that accessing s_completionPortDrainer.Value will ensure the drainer thread starts.
            limitInfo.CompletionPort = s_completionPortDrainer.Value.DangerousGetCompletionPortHandle();
            if (!Native.Processes.ProcessUtilities.SetInformationJobObject(
                handle,
                JOBOBJECTINFOCLASS.AssociateCompletionPortInformation,
                &limitInfo,
                (uint)Marshal.SizeOf(limitInfo)))
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "Unable to set job completion port.");
            }

            // Note that we don't risk calling MarkDone before the constructor returns;
            // the drainer can't be notified about this job until the first time a process is added.
            s_completionPortDrainer.Value.Register(m_completionKey, this);
        }

        /// <summary>
        /// Configures termination and priority class.
        /// </summary>
        /// <remarks>
        /// Sets whether to terminate all processes in the job when the last handle to the job closes,
        /// and priority class.
        /// </remarks>
        /// <param name="terminateOnClose">If set, the job and all children will be terminated when the last handle to the job closes.</param>
        /// <param name="priorityClass">Forces a priority class onto all child processes in the job.</param>
        /// <param name="failCriticalErrors">If set, applies the effects of <c>SEM_NOGPFAULTERRORBOX</c> to all child processes in the job.</param>
        internal void SetLimitInformation(bool? terminateOnClose = null, ProcessPriorityClass? priorityClass = null, bool failCriticalErrors = false)
        {
            // There is a race in here; but that shouldn't matter in the way we use JobObjects in BuildXL.
            var limitInfo = default(JOBOBJECT_EXTENDED_LIMIT_INFORMATION);
            uint bytesWritten;
            if (!Native.Processes.ProcessUtilities.QueryInformationJobObject(
                handle,
                JOBOBJECTINFOCLASS.ExtendedLimitInformation,
                &limitInfo,
                (uint)Marshal.SizeOf(limitInfo),
                out bytesWritten))
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "Unable to query job ExtendedLimitInformation.");
            }

            if (terminateOnClose.HasValue)
            {
                if (terminateOnClose.Value)
                {
                    limitInfo.LimitFlags |= JOBOBJECT_LIMIT_FLAGS.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                }
                else
                {
                    limitInfo.LimitFlags &= ~JOBOBJECT_LIMIT_FLAGS.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                }
            }

            if (priorityClass.HasValue)
            {
                limitInfo.LimitFlags |= JOBOBJECT_LIMIT_FLAGS.JOB_OBJECT_LIMIT_PRIORITY_CLASS;
                limitInfo.PriorityClass = (uint)priorityClass.Value;
            }

            if (failCriticalErrors)
            {
                limitInfo.LimitFlags |= JOBOBJECT_LIMIT_FLAGS.JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION;
            }

            if (!Native.Processes.ProcessUtilities.SetInformationJobObject(
                handle,
                JOBOBJECTINFOCLASS.ExtendedLimitInformation,
                &limitInfo,
                (uint)Marshal.SizeOf(limitInfo)))
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "Unable to set job ExtendedLimitInformation.");
            }
        }

        /// <summary>
        /// Terminates all processes in this job object.
        /// </summary>
        internal bool Terminate(int exitCode)
        {
            return Native.Processes.ProcessUtilities.TerminateJobObject(handle, exitCode);
        }

        /// <summary>
        /// Checks whether there are any active processes for this job
        /// </summary>
        /// <remarks>If in doubt, it returns <code>true</code>.</remarks>
        internal bool HasAnyProcesses()
        {
            uint[] processIds;
            bool tooMany;
            return
                !TryGetProcessIds(out processIds, out tooMany) ||
                tooMany ||
                processIds.Length > 0;
        }

        /// <summary>
        /// Tries to retrieve the list of active process ids for this job.
        /// </summary>
        internal bool TryGetProcessIds(out uint[] processIds, out bool tooMany)
        {
            Contract.Ensures(!Contract.Result<bool>() || Contract.ValueAtReturn(out tooMany) || Contract.ValueAtReturn(out processIds) != null);

            // Allocate a sufficiently large buffer, with 8-byte alignment (sufficient for aligned 64-bit pointers).
            int bufferSizeInBytes = JOBOBJECT_BASIC_PROCESS_ID_LIST.RequiredBufferSizeForMaxProcessIdListLength;
            int bufferSizeInEntries = (bufferSizeInBytes + sizeof(ulong) - 1) / sizeof(ulong);
            var buffer = new ulong[bufferSizeInEntries];

            fixed (ulong* bufferPtr = buffer)
            {
                var processIdListPtr = (JOBOBJECT_BASIC_PROCESS_ID_LIST*)bufferPtr;
                Contract.Assert(processIdListPtr != null);

                uint bytesWritten;
                if (!Native.Processes.ProcessUtilities.QueryInformationJobObject(
                    handle,
                    JOBOBJECTINFOCLASS.JobObjectBasicProcessIdList,
                    processIdListPtr,
                    (uint)bufferSizeInBytes,
                    out bytesWritten))
                {
                    // observed to happen when process tree is killed via procexp
                    processIds = null;
                    tooMany = false;
                    return false;
                }

                Contract.Assume(bytesWritten <= bufferSizeInBytes);

                if (processIdListPtr->NumberOfAssignedProcesses > processIdListPtr->NumberOfProcessIdsInList)
                {
                    // This means "provided list too small"
                    processIds = null;
                    tooMany = true;
                    return true;
                }

                var l = new uint[processIdListPtr->NumberOfProcessIdsInList];
                UIntPtr* ids = &processIdListPtr->ProcessIdListFirst;
                for (int i = 0; i < l.Length; i++)
                {
                    // Note that this would throw an OverflowException if we saw a PID that can't fit in 32 bits. It is mysterious why
                    // the Job APIs choose to return pointer-sized entries, but other APIs (like GetProcessId) guarantee 32-bit PIDs.
                    l[i] = ids[i].ToUInt32();
                }

                processIds = l;
                tooMany = false;
                return true;
            }
        }

        /// <summary>
        /// Gets accounting information of this job object (aggregate resource usage by all processes ever in the job).
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public AccountingInformation GetAccountingInformation()
        {
            var info = default(JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION);

            uint bytesWritten;
            if (!Native.Processes.ProcessUtilities.QueryInformationJobObject(
                handle,
                JOBOBJECTINFOCLASS.JobObjectBasicAndIOAccountingInformation,
                &info,
                (uint)Marshal.SizeOf(info),
                out bytesWritten))
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "Unable to get basic accounting information.");
            }

            return new AccountingInformation()
                   {
                       IO = new IOCounters(info.IOCounters),
                       KernelTime = new TimeSpan(checked((long)info.BasicAccountingInformation.TotalKernelTime)),
                       UserTime = new TimeSpan(checked((long)info.BasicAccountingInformation.TotalUserTime)),
                       NumberOfProcesses = info.BasicAccountingInformation.TotalProcesses,
                       PeakMemoryUsage = GetPeakMemoryUsage(),
                   };
        }

        /// <summary>
        /// Gets the peak aggregate memory usage of this job (sum of memory usage of all processes).
        /// </summary>
        /// <remarks>
        /// Corresponds to PeakJobMemoryUsed on the job's extended limit information.
        /// See https://msdn.microsoft.com/en-us/library/windows/desktop/ms684156(v=vs.85).aspx
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public ulong GetPeakMemoryUsage()
        {
            var info = default(JOBOBJECT_EXTENDED_LIMIT_INFORMATION);

            uint bytesWritten;
            if (!Native.Processes.ProcessUtilities.QueryInformationJobObject(
                handle,
                JOBOBJECTINFOCLASS.ExtendedLimitInformation,
                &info,
                (uint)Marshal.SizeOf(info),
                out bytesWritten))
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "Unable to get extended limit information.");
            }

            return info.PeakJobMemoryUsed.ToUInt64();
        }

        /// <summary>
        /// Adds a running process to this job. Any children it later spawns will be part of this job, too.
        /// </summary>
#if FEATURE_SAFE_PROCESS_HANDLE
        private bool AddProcess(SafeProcessHandle processHandle)
#else
        private bool AddProcess(IntPtr processHandle)
#endif
        {
#if FEATURE_SAFE_PROCESS_HANDLE
            Contract.Requires(!processHandle.IsInvalid);
#else
            Contract.Requires(processHandle != IntPtr.Zero);
#endif

            lock (m_lock)
            {
                Contract.Assume(!m_disposed, "Can't add processes to a disposed JobObject");
                Contract.Assume(!m_done, "Can't add processes to a JobObject that has already completed");

                if (!Native.Processes.ProcessUtilities.AssignProcessToJobObject(
                    handle,
                    processHandle))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if a particular process is part of this job.
        /// </summary>
        internal bool ContainsProcess(SafeProcessHandle processHandle)
        {
            Contract.Requires(!processHandle.IsInvalid);

            bool result;
            if (!Native.Processes.ProcessUtilities.IsProcessInJob(
                processHandle,
                handle,
                out result))
            {
                throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "IsProcessInJob failed");
            }

            return result;
        }

        /// <inheritdoc />
        protected override bool ReleaseHandle()
        {
            return FileSystemWin.CloseHandle(handle);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                lock (m_lock)
                {
                    Contract.Assume(!m_disposed);

                    if (m_doneEvent != null)
                    {
                        m_doneEvent.Dispose();
                    }

                    Analysis.IgnoreResult(s_completionPortDrainer.Value.TryUnregister(m_completionKey));

                    m_disposed = true;
                }
            }
        }

        /// <summary>
        /// Add the current process to a job object, and sets limit information.
        /// </summary>
        public static bool SetLimitInformationOnCurrentProcessJob(bool? terminateOnClose = null, ProcessPriorityClass? priorityClass = null)
        {
            Contract.Requires(OSSupportsNestedJobs);

            bool ret = false;
            using (Process currentProcess = Process.GetCurrentProcess())
            {
                using (var jobObject = new JobObject(null))
                {
                    jobObject.SetLimitInformation(terminateOnClose, priorityClass);
#if FEATURE_SAFE_PROCESS_HANDLE
                    ret = jobObject.AddProcess(currentProcess.SafeHandle);
#else
                    ret = jobObject.AddProcess(currentProcess.Handle);
#endif
                    bool success = false;
                    jobObject.DangerousAddRef(ref success);
                    if (!success)
                    {
                        throw new InvalidOperationException();
                    }
                }

                return ret;
            }
        }

        /// <summary>
        /// Add the current process to a job object, and sets TerminateOnClose to true.
        /// </summary>
        /// <remarks>
        /// This is useful to ensure that any process started by the current process will
        /// terminate when the current process terminates.
        /// </remarks>
        public static void SetTerminateOnCloseOnCurrentProcessJob()
        {
            Contract.Requires(OSSupportsNestedJobs);
            if (!s_terminateOnCloseOnCurrentProcessJob)
            {
                lock (s_syncRoot)
                {
                    if (!s_terminateOnCloseOnCurrentProcessJob)
                    {
                        SetLimitInformationOnCurrentProcessJob(terminateOnClose: true);
                        s_terminateOnCloseOnCurrentProcessJob = true;
                    }
                }
            }
        }

        /// <summary>
        /// Moves this job to the done state such that all waiters are released and no more processes can be added.
        /// </summary>
        private void MarkDone()
        {
            // If there are no waiters, existingEvent is null. Otherwise, it should be an unset event shared
            // by all of the waiters. We replace the existing event (or null) with the terminal event so that
            // future waiters have a fast path.
            lock (m_lock)
            {
                // WaitAsync is not required before Dispose, so completion races with dispose.
                if (m_disposed)
                {
                    return;
                }

                Contract.Assume(
                    !m_done,
                    "MarkDone should be called exactly once; perhaps a process was added to the job after it had already become empty.");

                m_done = true;

                if (m_doneEvent != null)
                {
                    m_doneEvent.Set();
                }
            }
        }

        /// <summary>
        /// Waits until all processes have terminated.
        /// </summary>
        /// <remarks>
        /// This function may safely be invoked concurrently / multiple times.
        /// </remarks>
        /// <returns>Boolean indicating whether all processes have terminated (false indicates that a timeout occurred instead)</returns>
        public Task<bool> WaitAsync(TimeSpan timeout)
        {
            Contract.Requires(timeout >= TimeSpan.Zero);
            Contract.Requires(timeout.TotalMilliseconds < uint.MaxValue);

            ManualResetEvent doneEvent;

            lock (m_lock)
            {
                Contract.Assume(!m_disposed, "WaitAsync called on a disposed JobObject");

                doneEvent = m_doneEvent;

                if (m_done || !HasAnyProcesses())
                {
                    // Fast path: no need to wait
                    // Note that we check HasAnyProcesses() in addition to m_done so as to more likely avoid pathological behavior in which calls to WaitAsync
                    // happen close to job exit (e.g. because the job had one process, and the process handle already signalled); in that case we are
                    // racing with the completion port drainer to set m_done, so creating an event and possibly sleeping the current thread would be wasteful.
                    return BoolTask.True;
                }
                else if (doneEvent == null)
                {
                    // Slower path: need to wait as the first waiter (no event yet).
                    doneEvent = m_doneEvent = new ManualResetEvent(initialState: false);
                }
                else
                {
                    // Slower path: need to wait on an existing event (with a previous waiter).
                }
            }

            Contract.Assert(doneEvent != null);

            // doneEvent is now an event that was (at one point) m_doneEvent.
            // It may be set already, but RegisterWaitForSingleObject is robust to that case.
            var waiter = TaskSourceSlim.Create<bool>();
            RegisteredWaitHandle waitPoolHandle = null;
            waitPoolHandle = ThreadPool.RegisterWaitForSingleObject(
                doneEvent,
                (state, timedOut) =>
                {
                    waiter.SetResult(!timedOut);

                    // Note that the assignment of waitPoolHandle races with the callback.
                    // Worst case, we'll let the garbage collector get it.
                    if (waitPoolHandle != null)
                    {
                        waitPoolHandle.Unregister(null);
                    }
                },
                state: null,
                timeout: timeout,
                executeOnlyOnce: true);

            return waiter.Task;
        }

        private sealed class CompletionPortDrainer
        {
            private readonly ConcurrentDictionary<IntPtr, JobObject> m_jobs = new ConcurrentDictionary<IntPtr, JobObject>();
            private readonly SafeIOCompletionPortHandle m_completionPort;

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
            public CompletionPortDrainer()
            {
                SafeIOCompletionPortHandle completionPort = Native.Processes.Windows.ProcessUtilitiesWin.CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, IntPtr.Zero, 1);
                if (completionPort.IsInvalid)
                {
                    throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "CreateIoCompletionPort failed");
                }

                m_completionPort = completionPort;
                new Thread(ProcessCompletionPortsBackgroundThread) { IsBackground = true, Name = "JobObject Completion Drainer" }.Start();
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods", MessageId = "System.Runtime.InteropServices.SafeHandle.DangerousGetHandle")]
            public IntPtr DangerousGetCompletionPortHandle()
            {
                return m_completionPort.DangerousGetHandle();
            }

            public void Register(IntPtr key, JobObject value)
            {
                Contract.Requires(value != null);

                if (!m_jobs.TryAdd(key, value))
                {
                    throw new InvalidOperationException("Invoking Wait concurrently on the same instance is not supported.");
                }
            }

            public bool TryUnregister(IntPtr key)
            {
                JobObject job;
                return m_jobs.TryRemove(key, out job);
            }

            /// <summary>
            /// Monitors completion status.
            /// </summary>
            /// <remarks>
            /// Note that messages are not guaranteed to arrive, as
            /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms684141(v=vs.85).aspx says:
            /// "Note that, except for limits set with the JobObjectNotificationLimitInformation information class,
            /// messages are intended only as notifications and their delivery to the completion port is not guaranteed.
            /// The failure of a message to arrive at the completion port does not necessarily mean that the event did not occur.
            /// Notifications for limits set with JobObjectNotificationLimitInformation are guaranteed to arrive at the completion
            /// port."
            /// However, as it turns out, contemporary OS implementations actually implement reliable message delivery for some kinds of messages,
            /// including the JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO message we are interested in here.
            /// </remarks>
            [SuppressMessage("Microsoft.Performance", "CA1801")]
            private void ProcessCompletionPortsBackgroundThread(object state)
            {
                while (true)
                {
                    uint completionCode;
                    IntPtr completionKey;
                    IntPtr overlappedPtr;
                    if (!Native.Processes.Windows.ProcessUtilitiesWin.GetQueuedCompletionStatus(
                        this.m_completionPort,
                        out completionCode,
                        out completionKey,
                        out overlappedPtr,
                        Native.Processes.ProcessUtilities.INFINITY))
                    {
                        throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "GetQueuedCompletionStatus failed");
                    }

                    // Mark the associate job as done and stop tracking it (they are only allowed to complete once).
                    // Since we allow jobs to be disposed without waiting on them, it is okay for the job to have been unregistered already.
                    JobObject job;
                    if (completionCode == Native.Processes.ProcessUtilities.JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO &&
                        this.m_jobs.TryRemove(completionKey, out job))
                    {
                        job.MarkDone();
                    }
                }
            }
        }

        private static IntPtr GetNextCompletionKey()
        {
            long value = Interlocked.Increment(ref s_currentCompletionKey);
            if (value < 0)
            {
                throw new OverflowException();
            }

            return checked((IntPtr)value);
        }
    }
}
