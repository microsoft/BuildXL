// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Interop.Unix;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.Unix.Sandbox;

namespace BuildXL.Processes
{
    /// <summary>
    /// A connection that communicates with a sandbox via a FIFO (a.k.a., named pipe).
    ///
    /// A separate FIFO is used for each pip.  The sandbox is injected into the pip via EBPF
    /// </summary>
    /// <remarks>
    /// This class is a direct simplification of <see cref="SandboxConnectionLinuxDetours"/> and significant
    /// part of the code here is duplicated. There are no immediate plans to factor out the common code
    /// since the intention is to retire the interpose sandbox. Factoring out common code will make the overall
    /// result less readable once interpose is removed, and that work would have to be reverted back to its current shape.
    /// </remarks>
    public sealed class SandboxConnectionLinuxEBPF : ISandboxConnection
    {
        // Used to signal that no active processes were seen through the FIFO. The value -21 is arbitrary,
        // it could be any negative value, as it will be read in place of a value representing
        // a length, which could take any positive value.
        // This value means that we *may* have reached the end of reports since no active processes are around. We may
        // still have reports to be processed containing start process reports.
        private const int NoActiveProcessesSentinel = -21;
        
        // This value means that we actually reached the end of the reports: no reports need to be processed and we got to
        // 0 active processes
        private const int EndOfReportsSentinel = -22;

        /// <summary>
        /// Location of the Linux EBPF launcher. CODESYNC: Public/Src/Sandbox/Linux/ebpf/BuildXL.Sandbox.Linux.eBPF.dsc
        /// </summary>
        public static readonly string EBPFLauncher = SandboxedProcessUnix.EnsureDeploymentFile("bxl-ebpf-runner");

        /// <summary>
        /// Environment variable containing the path to the file access manifest to be read by the detoured process.
        /// </summary>
        public static readonly string BuildXLFamPathEnvVarName = "__BUILDXL_FAM_PATH";

        /// <summary>
        /// Encapsulates a background thread that is processing incoming messages
        /// </summary>
        internal sealed class Info : IDisposable
        {
            private readonly Thread m_workerThread;
                
            // We use these two to synchronize sending a sentinel via the write handle and disposing the read handle. Trying to write to a FIFO with no read handles open
            // produces an error (a broken pipe)
            private bool m_readHandleDisposed = false;
            internal object ReadHandleLock { get; } = new();
                
            internal SandboxedProcessUnix Process { get; }
            internal string ReportsFifoPath { get; }
            internal string FamPath { get; }

            private readonly ManagedFailureCallback m_failureCallback;
            private readonly bool m_isInTestMode;

            /// <remarks>
            /// This dictionary is accessed both from the report processor threads as well as the thread
            /// backing <see cref="m_activeProcessesChecker"/>, hence it must be thread-safe.
            ///
            /// Implementation detail: ConcurrentDictionary is used to implement a thread-safe set (because no
            /// ConcurrentSet class exists).  Therefore, the values in this dictionary are completely ignored;
            /// the keys represent the set of currently active process IDs.
            /// </remarks>
            private readonly ConcurrentDictionary<int, byte> m_activeProcesses;

            /// <summary>
            /// This dictionary stores the processes that requested a breakaway. They truly broke away if the
            /// corresponding exec* call succeeded, but we don't know this here for sure. However, the only
            /// consequence of a pid being stored here is that we won't wait for it to finish after
            /// the root process exited. The assumption is that if the process was about to breakaway and
            /// it is alive when the root process finished, then it actually broke away and we shouldn't wait for it.
            /// </summary>
            private readonly ConcurrentDictionary<int, byte> m_breakawayProcesses;

            /// <summary>
            /// Sanity check to make sure the sandbox is torn down appropriately even if, for whatever reason, we never
            /// see the root process start event.
            /// </summary>
            private bool m_rootProcessWasRemoved = false;
            
            private readonly object m_removePidLock = new object();

            private readonly CancellableTimedAction m_activeProcessesChecker;
            private readonly Lazy<SafeFileHandle> m_lazyWriteHandle;

            private static readonly TimeSpan s_activeProcessesCheckerInterval = TimeSpan.FromSeconds(1);

            // These are just the byte representations of the sentinel values, so we don't need to compute them over and over
            private static readonly byte[] s_noActiveProcessesSentinelAsBytes = BitConverter.GetBytes(NoActiveProcessesSentinel);
            private static readonly byte[] s_endOfReportsSentinelAsBytes = BitConverter.GetBytes(EndOfReportsSentinel);

            private static ArrayPool<byte> ByteArrayPool { get; } = new ArrayPool<byte>(4096);

            internal Info(ManagedFailureCallback failureCallback, SandboxedProcessUnix process, string reportsFifoPath, string famPath, bool isInTestMode)
            {
                m_isInTestMode = isInTestMode;
                m_failureCallback = failureCallback;
                Process = process;
                ReportsFifoPath = reportsFifoPath;
                FamPath = famPath;

                m_activeProcesses = new ConcurrentDictionary<int, byte>();
                m_breakawayProcesses = new ConcurrentDictionary<int, byte>();

                m_activeProcessesChecker = new CancellableTimedAction(
                    CheckActiveProcesses,
                    intervalMs: Math.Min((int)process.ChildProcessTimeout.TotalMilliseconds, (int)s_activeProcessesCheckerInterval.TotalMilliseconds));

                // create a write handle (used to keep the fifo open, i.e.,
                // the 'read' syscall won't receive EOF until we close this writer
                m_lazyWriteHandle = GetLazyWriteHandle(ReportsFifoPath);

                m_workerThread = new Thread(() => StartReceivingAccessReports())
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Highest
                };
            }

            internal void Start() => m_workerThread.Start();

            /// <nodoc />
            internal void JoinReceivingThread() => m_workerThread.Join();

            private static int Read(SafeFileHandle handle, byte[] buffer, int offset, int length)
            {
                Contract.Requires(buffer.Length >= offset + length);
                int totalRead = 0;
                while (totalRead < length)
                {
                    var numRead = IO.Read(handle, buffer, offset + totalRead, length - totalRead);
                    if (numRead <= 0)
                    {
                        return numRead;
                    }
                    totalRead += numRead;
                }

                return totalRead;
            }

            /// <summary>
            /// The method backing the <see cref="m_workerThread"/> thread.
            /// </summary>
            /// <remarks>
            /// The way we deal with the decision about when to stop reading messages from the FIFO deserves some details:
            /// * Messages are read from the FIFO and processed via <see cref="ProcessBytes(PooledObjectWrapper{byte[]}, int)"/>.
            /// * A write handle <see cref="m_lazyWriteHandle"/> is kept open to avoid reaching EOF if other writers (running tools) happen to close the FIFO
            /// * The potential end of the receive loop is triggered by removing the last active process from <see cref="m_activeProcesses"/>. This
            ///   can happen because the <see cref="m_activeProcessesChecker"/> detected than an active process is no longer alive or because 
            ///   a process exited report is seen. When this case is reached a special message <see cref="NoActiveProcessesSentinel"/> is sent from this
            ///   same loop. Sentinels are just special messages used for synchronization purposes.
            /// * Sending <see cref="NoActiveProcessesSentinel"/> *may* result in ending this processing loop: when <see cref="NoActiveProcessesSentinel"/> is sent, 
            ///   other messages may still be on the processing pipe (e.g. observe that the active process checker runs in a separate 
            ///   thread, and the point in time when the sentinel is sent is not synchronized with the point in time when we processed all reports). When we get to 
            ///   processing the sentinel and if 'start process' reports had arrived, we just ignore the sentinel and keep processing messages. We will eventually
            ///   reach again 0 processes and the sentinel will be sent another time.
            /// * If <see cref="NoActiveProcessesSentinel"/> arrives and we see 0 active processes, we can safely exit the loop. In this case we send another
            ///   sentinel <see cref="EndOfReportsSentinel"/>. Instead of this we could just close <see cref="m_lazyWriteHandle"/> and let the receiving loop reach an EOF,
            ///   but this proved to be slow in some cases (since it is likely depending on some GC process). So instead we send a sentinel. The loop can safely exit when we
            ///   see this since no pending messages can be left to be processed (we saw the <see cref="NoActiveProcessesSentinel"/> on the other end of the pipe at the 
            ///   same pipe there were 0 active processes).
            /// </remarks>
            private void StartReceivingAccessReports()
            {
                // opening FIFO for reading (blocks until there is at least one writer connected)
                LogDebug($"Opening FIFO '{ReportsFifoPath}' for reading");

                var readHandle = IO.Open(ReportsFifoPath, IO.OpenFlags.O_RDONLY, 0);
                try
                {
                    if (readHandle.IsInvalid)
                    {
                        LogError($"Opening FIFO {ReportsFifoPath} for reading failed.");
                        return;
                    }

                    // make sure that m_lazyWriteHandle has been created
                    Analysis.IgnoreResult(m_lazyWriteHandle.Value);

                    byte[] messageLengthBytes = new byte[sizeof(int)];
                    while (true)
                    {
                        // read length
                        var numRead = Read(readHandle, messageLengthBytes, 0, messageLengthBytes.Length);
                        if (numRead == 0) // EOF
                        {
                            // We don't expect EOF before reading the EndOfReportsSentinel (see below)
                            LogError("Exiting 'receive reports' loop on EOF without observing the end of reports sentinel value.");
                            break;
                        }

                        if (numRead < 0) // error
                        {
                            LogError($"Read from FIFO {ReportsFifoPath} failed with return value {numRead}.");
                            break;
                        }

                        // decode length
                        int messageLength = BitConverter.ToInt32(messageLengthBytes, startIndex: 0);

                        // The process tree we know about so far has completed. We might still
                        // have 'process start' reports to be processed, so we just send this sentinel and let the processing block decide.
                        if (messageLength == NoActiveProcessesSentinel)
                        {
                            ProcessBytes(ByteArrayPool.GetInstance(0), NoActiveProcessesSentinel);
                            continue;
                        }

                        // We processed all pending messages in the processing block and didn't see any active processes, we can exit the loop
                        if (messageLength == EndOfReportsSentinel)
                        {
                            LogDebug($"End of reports sentinel arrived on FIFO {ReportsFifoPath}. Exiting 'receive reports' loop.");

                            break;
                        }

                        // read a message of that length
                        PooledObjectWrapper<byte[]> messageBytes = ByteArrayPool.GetInstance(messageLength);
                        numRead = Read(readHandle, messageBytes.Instance, 0, messageLength);
                        if (numRead < messageLength)
                        {
                            LogError($"Read from FIFO {ReportsFifoPath} failed: read only {numRead} out of {messageLength} bytes.");
                            messageBytes.Dispose();
                            break;
                        }

                        // Add message to processing queue
                        try
                        {
                            ProcessBytes(messageBytes, messageLength);
                        }
                        catch (Exception e)
                        {
                            Analysis.IgnoreException("Will error and exit on LogError");
                            LogError($"Could not post message to the processing block for {ReportsFifoPath}. Exception details: {e}");
                            break;
                        }
                    }

                    LogDebug($"Completed receiving access reports for fifo '{ReportsFifoPath}'");
                }
                finally
                {
                    // Synchronize the disposal to make sure we don't try to send a sentinel (e.g. the active process checker seeing 0 processes)
                    // while disposing the read handle
                    lock (ReadHandleLock)
                    {
                        LogDebug($"Disposing read handle for fifo '{ReportsFifoPath}'");
                        m_readHandleDisposed = true;
                        readHandle.Dispose();
                    }
                }

                LogDebug("Posting OpProcessTreeCompleted message");
                Process.PostAccessReport(new SandboxReportLinux
                {
                    ReportType = ReportType.FileAccess,
                    FileOperation = ReportedFileOperation.ProcessTreeCompletedAck,
                });
            }

            private Lazy<SafeFileHandle> GetLazyWriteHandle(string path)
            {
                return new Lazy<SafeFileHandle>(() =>
                {
                    LogDebug($"Opening FIFO '{path}' for writing");
                    return IO.Open(path, IO.OpenFlags.O_WRONLY, 0);
                });
            }

            private void CheckActiveProcesses()
            {
                foreach (var pid in m_activeProcesses.Keys)
                {
                    if (!Dispatch.IsProcessAlive(pid))
                    {
                        LogDebug($"CheckActiveProcesses. Process {pid} is not alive, removing.");
                        RemovePid(pid);
                        continue;
                    }

                    // We shouldn't wait for a breakaway process after the main root process exited.
                    if (m_breakawayProcesses.TryGetValue(pid, out _))
                    {
                        LogDebug($"CheckActiveProcesses. Process {pid} is a breakaway, removing.");
                        RemovePid(pid);
                    }
                }
            }

            /// <summary>
            /// Request to stop receiving access reports. 
            /// Any currently pending reports will be processed asynchronously.
            /// </summary>
            internal void RequestStop()
            {
                LogDebug($"RequestStop: closing the write handle for FIFO '{ReportsFifoPath}'");

                m_lazyWriteHandle.Value.Close();
                m_lazyWriteHandle.Value.Dispose();

                m_activeProcessesChecker.Cancel();
            }

            private void WriteSentinel(Lazy<SafeFileHandle> writeHandle, byte[] sentinelBytes)
            {
                // If the read or write handles are already closed, no need to send a sentinel
                if (m_readHandleDisposed || writeHandle.Value.IsClosed || writeHandle.Value.IsInvalid)
                {
                    return;
                }
                
                // Synchronize sending the sentinel so we don't dispose the read handle without coordination
                // Without any read handle open, writing to the FIFO causes an broken pipe error
                lock (ReadHandleLock)
                {
                    if (m_readHandleDisposed)
                    {
                        return;
                    }

                    // Observe this will be atomic because the length of an int is less than PIPE_BUF
                    var bytesWritten = Write(writeHandle.Value, sentinelBytes, 0, sentinelBytes.Length);
                    if (bytesWritten < 0) // error
                    {
                        string win32Message = new Win32Exception(Marshal.GetLastWin32Error()).Message;

                        StackTrace stackTrace = new StackTrace();

                        LogError($"Cannot write sentinel {BitConverter.ToInt32(sentinelBytes, 0)} to {ReportsFifoPath}. Error: {win32Message}. {stackTrace}");

                        // Dispose the handle so we avoid a potential hang in the process reports loop
                        writeHandle.Value.Dispose();
                    }
                }
            }

            private static int Write(SafeFileHandle handle, byte[] buffer, int offset, int length)
            {
                Contract.Requires(buffer.Length >= offset + length);
                int totalWrite = 0;
                while (totalWrite < length)
                {
                    var numWrite = IO.Write(handle, buffer, offset + totalWrite, length - totalWrite);
                    if (numWrite <= 0)
                    {
                        return numWrite;
                    }
                    totalWrite += numWrite;
                }

                return totalWrite;
            }

            /// <summary>Adds <paramref name="pid" /> to the set of active processes</summary>
            internal void AddPid(int pid)
            {
                bool added = m_activeProcesses.TryAdd(pid, 1);
                LogDebug($"AddPid({pid}) :: added: {added}; size: {m_activeProcesses.Count}");

                // If the recently started process has the same pid as an existing breakaway one, that means
                // the breakaway process ended and we have a case of process id reuse. The newly started process
                // is not a breakaway one
                // Observe that we send clone events on both parent and child processes (which trigger calls to AddPid), and these can arrive in non-deterministic order.
                // So the case where both clone events arrive before the breakaway event is possible. Therefore, only try to detect process id reuse if we actually added the pid, otherwise
                // we might misinterpret the arrival of the second clone event as a process id reuse.
                if (added && m_breakawayProcesses.TryRemove(pid, out _))
                {
                    LogDebug($"AddPid({pid}) :: New process is reusing a breakaway pid presumably dead");
                }
            }

            /// <summary>
            /// Removes <paramref name="pid" /> from the set of active processes.
            /// </summary>
            internal void RemovePid(int pid)
            {
                bool removed;
                lock (m_removePidLock)
                {
                    removed = m_activeProcesses.TryRemove(pid, out var _);
                    LogDebug($"RemovePid({pid}) :: removed: {removed}; size: {m_activeProcesses.Count}");
                    
                    // If the process that we tried to remove is the root process, we do a defensive check here to see if we ever added it.
                    if (pid == Process.ProcessId)
                    {
                        // If it was successfully removed it means we saw a process start event for it and we are fine. Just track it
                        if (removed)
                        {
                            m_rootProcessWasRemoved = true;
                        }
                        
                        // If it was not removed and it was never removed before, then we have a problem:
                        // we missed the process start event for it. In the case of EBPF, this can happen if the pip was abruptly terminated
                        // before it has the chance to send a process start event (since this happens on the ebpf launcher)
                        // This happening for the root process is particularly sensitive, if no active processes are left we should send the proper
                        // sentinel to try to tear down the sandbox. Failing to doing so can make the sandbox non-terminating.
                        else if (!m_rootProcessWasRemoved)
                        {
                            LogDebug($"We missed the process start event for root process {pid}");
                            // The first time we try to remove the root process without having added it first, let's pretend we actually removed it,
                            // so the sentinel sending/active process checker start logic can kick in anyway.
                            removed = true;
                            // Let's make sure we do this only once. There are actually not many consequences if we do this multiple times, but we can avoid
                            // sending the sentinel unnecessarily multiple times.
                            m_rootProcessWasRemoved = true;
                        }
                    }
                }

                if (removed && m_activeProcesses.IsEmpty)
                {
                    LogDebug($"Removed {pid} and the active count is 0. Sending sentinel on primary FIFO");
                    // We just reached 0 active processes. Notify this through the FIFO so we can check on the other end
                    // whether this means we are done processing reports. There might be reports still to be processed, including start process reports,
                    // so pushing this sentinel makes sure we process all pending reports before reaching a decision
                    
                    WriteSentinel(m_lazyWriteHandle, s_noActiveProcessesSentinelAsBytes);
                }
                else if (removed && pid == Process.ProcessId)
                {
                    LogDebug($"Root process {pid} was removed. Starting the active process checker.");

                    // We just removed the root process and there are still active processes left
                    //   => start periodically checking if they are still alive, because we don't
                    //      have a reliable mechanism for receiving those events straight from the
                    //      child processes (e.g., if they crash, we might not hear about it)
                    //
                    // Observe also that we do have a reliable mechanism for detecting when the
                    // root process exits (even if it crashes): see NotifyRootProcessExited below,
                    // which is guaranteed to be called by SandboxedProcessUnix.
                    m_activeProcessesChecker.Start();
                }
            }

            internal void LogError(string message)
            {
                message = $"[Pip{Process.PipSemiStableHash:X16}] {message} (errno: {Marshal.GetLastWin32Error()})";
                Process.LogProcessState("[ERROR]: " + message);
                m_failureCallback?.Invoke(1, message);
            }

            internal void LogDebug(string s) => Process.LogDebug(s);

            /// <nodoc />
            public void Dispose()
            {
                RequestStop();

                try
                {
                    m_activeProcessesChecker.Join();
                }
                catch (ThreadStateException)
                {
                    // The active process checker is only started once the main thread exits and child processes are still active. So we can start disposing the connection
                    // without that being the case
                }

                m_activeProcesses.Clear();
                Analysis.IgnoreResult(FileUtilities.TryDeleteFile(ReportsFifoPath, retryOnFailure: false));
                Analysis.IgnoreResult(FileUtilities.TryDeleteFile(FamPath, retryOnFailure: false));
                if (m_isInTestMode)
                {
                    // The worker thread should complete in all but most extreme cases.  One such extreme case
                    // is when the underlying filesystems crashes or shuts down completely (which is possible,
                    // especially if that's a custom-implemented filesystem running in user space).  When that
                    // happens, some write handled to the created FIFO may remain open, so the 'read' call in
                    // 'StartReceivingAccessReports' may remain stuck forever.
                    LogDebug("Waiting for the worker thread to complete");
                    JoinReceivingThread();
                }
            }

            /// <summary>
            /// Processes the bytes received from the FIFO.
            /// </summary>
            private void ProcessBytes(PooledObjectWrapper<byte[]> wrapper, int length)
            {
                using (wrapper)
                {
                    // This means the active process checker detected that no processes were running. But we need to make sure we still have no active processes. There is a race between
                    // that count reaching 0 and a potential new create process report being processed. Since the create report is reported on the parent process (and as well on the child), if this race
                    // happened the create process report should have bumped the active process count.
                    if (length == NoActiveProcessesSentinel)
                    {
                        if (m_activeProcesses.IsEmpty)
                        {
                            LogDebug($"NoActiveProcessesSentinel received for fifo {ReportsFifoPath} and 0 active processes found. Requesting completion to the report processor.");
                            // Send the end of report sentinel, so we can exit the report loop
                            WriteSentinel(m_lazyWriteHandle, s_endOfReportsSentinelAsBytes);
                        }
                        else
                        {
                            // In this case we just ignore the message. The sentinel will be sent again once we reach 0
                            // active processes
                            LogDebug($"NoActiveProcessesSentinel received for fifo {ReportsFifoPath} but {m_activeProcesses.Count} processes were detected. This means new start process reports arrived afterwards. The sentinel is ignored.");

                            // Observe that this is a case where at some point we reached 0 active processes but new process start events arrived afterwards
                            // The root process has exited already (since we reached 0 processes), and we might be in a case where the active process checker
                            // has not started (when the last process to exit before reaching 0 was the root process). Start it now to account for the orphan 
                            // processes that started since then.
                            // If the active process checker was started already, this call has no effect.
                            LogDebug($"NoActiveProcessesSentinel was ignored. Starting the active process checker to account for newly added orphan processes.");
                            m_activeProcessesChecker.Start();
                        }

                        return;
                    }

                    Contract.Assert(length > 0, "No other sentinel but the one above should be posted");

                    var messageStr = s_encoding.GetString(wrapper.Instance, index: 0, count: length);
                    var message = messageStr.AsSpan().TrimEnd('\n');

                    // Report format should be in sync with native code on Linux sandbox.
                    // CODESYNC: Public/Src/Sandbox/Linux/ReportBuilder.cpp

                    // 1. Report Type.
                    var restOfMessage = message;
                    var reportType = (ReportType)AssertInt(nextField(restOfMessage, out restOfMessage));
                    var report = new SandboxReportLinux()
                    {
                        ReportType = reportType
                    };
                    
                    switch (reportType)
                    {
                        case ReportType.FileAccess:
                        {
                            /*
                             * File Access Report Format: %d|%s|%d|%d|%d|%d|%d|%d|%d|%d|%s\n
                             * 
                             * 1. Report Type
                             * 2. System call name
                             * 3. File Operation
                             * 4. Process ID
                             * 5. Parent Process ID
                             * 6. Error
                             * 7. Requested Access
                             * 8. File Access Status
                             * 9. Report Explicitly
                             * 10. Is Directory
                             * 11. Is path truncated
                             * 12. Path
                            */
                            report.SystemCall = s_encoding.GetString(s_encoding.GetBytes(nextField(restOfMessage, out restOfMessage).ToArray()));
                            report.FileOperation = FileOperationLinux.ToReportedFileOperation((FileOperationLinux.Operations)AssertInt(nextField(restOfMessage, out restOfMessage)));
                            report.ProcessId = AssertInt(nextField(restOfMessage, out restOfMessage));
                            report.ParentProcessId = AssertInt(nextField(restOfMessage, out restOfMessage));
                            report.Error = AssertInt(nextField(restOfMessage, out restOfMessage));
                            report.RequestedAccess = (RequestedAccess)AssertInt(nextField(restOfMessage, out restOfMessage));
                            report.FileAccessStatus = AssertInt(nextField(restOfMessage, out restOfMessage));
                            report.ExplicitlyReport = AssertInt(nextField(restOfMessage, out restOfMessage)); // explicitLogging?
                            report.IsDirectory = AssertInt(nextField(restOfMessage, out restOfMessage)) != 0;
                            report.IsPathTruncated = AssertInt(nextField(restOfMessage, out restOfMessage)) != 0;
                            report.Data = s_encoding.GetString(s_encoding.GetBytes(nextField(restOfMessage, out restOfMessage).ToArray()));

                            if (report.FileOperation == ReportedFileOperation.ProcessExec) {
                                // Process exec may contain a command line as well
                                report.CommandLineArguments = s_encoding.GetString(s_encoding.GetBytes(nextField(restOfMessage, out restOfMessage).ToArray()));
                            }

                            break;
                        }
                        case ReportType.DebugMessage:
                        {
                            /*
                             * Debug report format: %d|%d|%d|%s\n
                             * 
                             * 1. Report Type
                             * 2. Process ID
                             * 3. Severity
                             * 4. Message
                            */
                            report.ProcessId = AssertInt(nextField(restOfMessage, out restOfMessage));
                            report.Severity = (DebugEventSeverity)AssertInt(nextField(restOfMessage, out restOfMessage));
                            report.Data = s_encoding.GetString(s_encoding.GetBytes(nextField(restOfMessage, out restOfMessage).ToArray())).Replace('!', '|');

                            break;
                        }
                        default:
                            break;
                    }

                    Contract.Assert(restOfMessage.IsEmpty);  // We should have reached the end of the message

                    // update active processes
                    if (report.FileOperation == ReportedFileOperation.Process)
                    {
                        LogDebug($"Received FileOperation.OpProcessStart for pid {report.ProcessId})");
                        AddPid((int)report.ProcessId);
                    }
                    else if (report.FileOperation == ReportedFileOperation.ProcessExit)
                    {
                        LogDebug($"Received FileOperation.OpProcessExit for pid {report.ProcessId})");
                        RemovePid((int)report.ProcessId);
                    }
                    else if (report.FileOperation == ReportedFileOperation.ProcessBreakaway)
                    {
                        LogDebug($"Received FileOperation.ProcessBreakaway for pid {report.ProcessId})");
                        m_breakawayProcesses[(int)report.ProcessId] = 0;
                    }

                    // Let's check for linux-specific reports that we want to ignore
                    if (report.ReportType == ReportType.FileAccess && IgnoreLinuxSpecificReports(report.Data))
                    {
                        LogDebug($"Ignored access for pid {report.ProcessId} on '{report.Data.ToString()}'");
                        return;
                    }

                    // post the AccessReport
                    Process.PostAccessReport(report);
                }

                // Reads next field of the serialized message, i.e. split on the first | and return both parts
                static ReadOnlySpan<char> nextField(ReadOnlySpan<char> message, out ReadOnlySpan<char> rest)
                {
                    for (int i = 0; i < message.Length; i++)
                    {
                        if (message[i] == '|')
                        {
                            rest = i + 1 == message.Length ? ReadOnlySpan<char>.Empty : message.Slice(i+1); // Defend against | being the last character, although we don't expect this
                            return message.Slice(0, i);
                        }
                    }

                    rest = ReadOnlySpan<char>.Empty;
                    return message;
                }
            }

            private bool IgnoreLinuxSpecificReports(string path)
            {
                // Check whether a given path is an anonymous file (a file that lives in RAM and only exists until all references to that file are dropped)
                // The path to an anonymous file reported by stat will always be '/memfd:<fileName> (deleted)'
                if (path.StartsWith("/memfd:", StringComparison.Ordinal))
                {                     
                    return true;
                }

                return false;
            }

            private uint AssertInt(ReadOnlySpan<char> str)
            {
#if NETCOREAPP
                if (uint.TryParse(str, out uint result))
#else // .NET 472 - no ReadOnlySpan<char> overloads. We don't really care about perf for .NET472 here
                if (uint.TryParse(str.ToString(), out uint result))
#endif
                {
                    return result;
                }
                else
                {
                    LogError($"Could not parse int from '{str.ToString()}'");
                    return 0;
                }
            }
        }

        /// <inheritdoc />
        public SandboxKind Kind => SandboxKind.LinuxEBPF;

        /// <inheritdoc />
        public bool IsInTestMode { get; }

        private readonly ConcurrentDictionary<long, Info> m_pipProcesses = new();

        private readonly ManagedFailureCallback m_failureCallback;

        private static readonly Encoding s_encoding = Encoding.UTF8;

        /// <nodoc />
        public SandboxConnectionLinuxEBPF(ManagedFailureCallback failureCallback = null, bool isInTestMode = false)
        {
            m_failureCallback = failureCallback;
            IsInTestMode = isInTestMode;
            Native.Processes.ProcessUtilities.SetNativeConfiguration(UnsandboxedProcess.IsInDebugMode);

            // Validate that the ebpf loader has the admin capability set.
            // For now we only want to do this for ADO builds because they won't require an interactive prompt
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")))
            {
                var getCapUtils = UnixGetCapUtils.CreateGetCap();
                if (!getCapUtils.BinaryContainsCapabilities(EBPFLauncher, UnixCapability.CAP_SYS_ADMIN))
                {
                    var setCapUtils = UnixSetCapUtils.CreateSetCap();
                    if (!setCapUtils.SetCapability(EBPFLauncher, UnixCapability.CAP_SYS_ADMIN))
                    {
                        throw new BuildXLException($"Failed to set CAP_SYS_ADMIN capability on {EBPFLauncher}");
                    }
                }
            }
            
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public bool NotifyUsage(uint cpuUsage, uint availableRamMB)
        {
            return true;
        }

        /// <inheritdoc />
        public IEnumerable<(string, string)> AdditionalEnvVarsToSet(SandboxedProcessInfo info, string uniqueName)
        {
            (_, string famPath) = GetPaths(uniqueName);

            yield return (BuildXLFamPathEnvVarName, famPath);
            yield return ("__BUILDXL_EBPF_PATH", EBPFLauncher);
        }

        /// <summary>
        /// Returns the paths for the FIFO and FAM based on the unique name for a pip.
        /// </summary>
        public static (string fifo, string fam) GetPaths(string uniqueName)
        {
            string rootDir = Path.GetTempPath();
            string fifoPath = Path.Combine(rootDir, $"bxl_{uniqueName}.fifo");
            // CODESYNC: Public/Src/Sandbox/Linux/bxl_observer.cpp
            string famPath = Path.ChangeExtension(fifoPath, ".fam");
            return (fifo: fifoPath, fam: famPath);
        }

        /// <inheritdoc />
        public bool NotifyPipStarted(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process) => true;

        /// <inheritdoc />
        public void NotifyPipReady(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process, Task reportCompletion)
        {
            Contract.Requires(!process.Started);
            Contract.Requires(process.PipId != 0);

            (string fifoPath, string famPath) = GetPaths(process.UniqueName);
            
            if (IsInTestMode)
            {
                fam.EnableLinuxSandboxLogging = true;
            }

            // serialize FAM
            using (var wrapper = Pools.MemoryStreamPool.GetInstance())
            {
                var debugFlags = true;
                ArraySegment<byte> manifestBytes = fam.GetPayloadBytes(
                    loggingContext,
                    new FileAccessSetup { DllNameX64 = string.Empty, DllNameX86 = string.Empty, ReportPath = fifoPath },
                    wrapper.Instance,
                    timeoutMins: 10, // don't care
                    debugFlagsMatch: ref debugFlags);

                Contract.Assert(manifestBytes.Offset == 0);
                File.WriteAllBytes(famPath, manifestBytes.ToArray());
            }

            process.LogDebug($"Saved FAM to '{famPath}'");

            // create a FIFO (named pipe)
            createNewFifo(fifoPath);

            // create and save info for this pip
            var info = new Info(m_failureCallback, process, fifoPath, famPath, IsInTestMode);
            if (!m_pipProcesses.TryAdd(process.PipId, info))
            {
                throw new BuildXLException($"Process with PidId {process.PipId} already exists");
            }

            // Make sure we dispose the process info after report processing is completed
            reportCompletion.ContinueWith(t => info.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.RunContinuationsAsynchronously);

            info.Start();

            void createNewFifo(string fifo)
            {
                Analysis.IgnoreResult(FileUtilities.TryDeleteFile(fifo, retryOnFailure: false));
                if (IO.MkFifo(fifo, IO.FilePermissions.S_IRWXU) != 0)
                {
                    throw new BuildXLException($"Creating FIFO {fifo} failed. (errno: {Marshal.GetLastWin32Error()})");
                }

                process.LogDebug($"Created FIFO at '{fifo}'");
            }
        }

        /// <inheritdoc />
        public void NotifyPipProcessTerminated(long pipId, int processId)
        {
            if (m_pipProcesses.TryGetValue(pipId, out var info))
            {
                info.Process.LogDebug($"NotifyPipProcessTerminated. Removing pid {processId}");
                info.RemovePid(processId);
            }
        }

        /// <inheritdoc />
        public void NotifyRootProcessExited(long pipId, SandboxedProcessUnix process)
        {
            if (m_pipProcesses.TryGetValue(pipId, out var info))
            {
                info.Process.LogDebug($"NotifyRootProcessExited. Removing pid {process.ProcessId}");
                info.RemovePid(process.ProcessId);
            }
        }

        /// <inheritdoc />
        public bool NotifyPipFinished(long pipId, SandboxedProcessUnix process) => m_pipProcesses.TryRemove(pipId, out _);

        /// <summary>
        /// Overrides the process start info to run the process through the sandbox.
        /// </summary>
        public void OverrideProcessStartInfo(ProcessStartInfo processStartInfo)
        {
            processStartInfo.Arguments = $"{processStartInfo.FileName} {processStartInfo.Arguments}";
            processStartInfo.FileName = EBPFLauncher;
        }
    }
}
