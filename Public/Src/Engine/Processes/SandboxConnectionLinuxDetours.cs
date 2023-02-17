// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Interop.Unix;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.ParallelAlgorithms;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.Unix.Sandbox;

namespace BuildXL.Processes
{
    /// <summary>
    /// A connection that communicates with a sandbox via a FIFO (a.k.a., named pipe).
    ///
    /// A separate FIFO is used for each pip.  The sandbox is injected into the pip
    /// by virtue of setting the LD_PRELOAD environment variable to point to a native
    /// dynamic library where all the system call interposing is implemented.
    /// </summary>
    public sealed class SandboxConnectionLinuxDetours : ISandboxConnection
    {
        internal sealed class PathCacheRecord
        {
            internal RequestedAccess RequestedAccess { get; set; }

            internal RequestedAccess GetClosure(RequestedAccess access)
            {
                var result = RequestedAccess.None;

                // Read implies Probe
                if (access.HasFlag(RequestedAccess.Read))
                {
                    result |= RequestedAccess.Probe;
                }

                // Write implies Read and Probe
                if (access.HasFlag(RequestedAccess.Write))
                {
                    result |= RequestedAccess.Read | RequestedAccess.Probe;
                }

                return result;
            }

            internal bool CheckCacheHitAndUpdate(RequestedAccess access)
            {
                // if all flags in 'access' are already present --> cache hit
                bool isCacheHit = (RequestedAccess & access) == access;
                if (!isCacheHit)
                {
                    RequestedAccess |= GetClosure(access);
                }
                return isCacheHit;
            }
        }

        internal sealed class Info : IDisposable
        {
            internal SandboxedProcessUnix Process { get; }
            internal string ReportsFifoPath { get; }
            internal string FamPath { get; }

            private readonly Sandbox.ManagedFailureCallback m_failureCallback;
            private readonly Dictionary<string, PathCacheRecord> m_pathCache; // TODO: use AbsolutePath instead of string
            private readonly bool m_isInTestMode;

            /// <remarks>
            /// This dictionary is accessed both from the <see cref="m_workerThread"/> thread as well as the thread
            /// backing <see cref="m_activeProcessesChecker"/>, hence it must be thread-safe.
            ///
            /// Implementation detail: ConcurrentDictionary is used to implement a thread-safe set (because no
            /// ConcurrentSet class exists).  Therefore, the values in this dictionary are completely ignored;
            /// the keys represent the set of currently active process IDs.
            /// </remarks>
            private readonly ConcurrentDictionary<int, byte> m_activeProcesses;

            private readonly CancellableTimedAction m_activeProcessesChecker;
            private readonly Lazy<SafeFileHandle> m_lazyWriteHandle;
            private readonly Thread m_workerThread;
            private readonly ActionBlockSlim<(PooledObjectWrapper<byte[]> wrapper, int length)> m_accessReportProcessingBlock;

            private int m_stopRequestCounter;
            private int m_completeAccessReportProcessingCounter;

            private static readonly TimeSpan ActiveProcessesCheckerInterval = TimeSpan.FromSeconds(1);

            private static ArrayPool<byte> ByteArrayPool { get; } = new ArrayPool<byte>(4096);

            internal Info(Sandbox.ManagedFailureCallback failureCallback, SandboxedProcessUnix process, string reportsFifoPath, string famPath,  bool isInTestMode)
            {
                m_isInTestMode = isInTestMode;
                m_stopRequestCounter = 0;
                m_completeAccessReportProcessingCounter = 0;
                m_failureCallback = failureCallback;
                Process = process;
                ReportsFifoPath = reportsFifoPath;
                FamPath = famPath;

                m_pathCache = new Dictionary<string, PathCacheRecord>();
                m_activeProcesses = new ConcurrentDictionary<int, byte>();
                m_activeProcessesChecker = new CancellableTimedAction(
                    CheckActiveProcesses,
                    intervalMs: Math.Min((int)process.ChildProcessTimeout.TotalMilliseconds, (int)ActiveProcessesCheckerInterval.TotalMilliseconds));

                // create a write handle (used to keep the fifo open, i.e.,
                // the 'read' syscall won't receive EOF until we close this writer
                m_lazyWriteHandle = new Lazy<SafeFileHandle>(() =>
                {
                    Process.LogProcessState($"Opening FIFO '{ReportsFifoPath}' for writing");
                    return IO.Open(ReportsFifoPath, IO.OpenFlags.O_WRONLY, 0);
                });

                // action block where parsing and processing of received ActionReport bytes is done
                m_accessReportProcessingBlock = ActionBlockSlim.Create<(PooledObjectWrapper<byte[]> wrapper, int length)>(
                    degreeOfParallelism: 1,
                    ProcessBytes,
                    singleProducedConstrained: true // Only m_workerThread posts to the action block
                    );

                // start a background thread for reading from the FIFO
                m_workerThread = new Thread(StartReceivingAccessReports);
                m_workerThread.IsBackground = true;
                m_workerThread.Priority = ThreadPriority.Highest;
            }

            /// <summary>
            /// Starts receiving access reports
            /// </summary>
            internal void Start()
            {
                m_workerThread.Start();
            }

            private void CheckActiveProcesses()
            {
                foreach (var pid in m_activeProcesses.Keys)
                {
                    if (!Dispatch.IsProcessAlive(pid))
                    {
                        Process.LogProcessState("CheckActiveProcesses");
                        RemovePid(pid);
                    }
                }
            }

            private void CompleteAccessReportProcessing()
            {
                var cnt = Interlocked.Increment(ref m_completeAccessReportProcessingCounter);
                if (cnt > 1)
                {
                    return; // already completed
                }

                m_accessReportProcessingBlock.Complete();
                m_accessReportProcessingBlock.Completion.ContinueWith(t =>
                {
                    Process.LogProcessState("Posting OpProcessTreeCompleted message");
                    Process.PostAccessReport(new AccessReport
                    {
                        Operation = FileOperation.OpProcessTreeCompleted,
                        PathOrPipStats = AccessReport.EncodePath("")
                    });
                });
            }

            /// <summary>
            /// Request to stop receiving access reports.  This method returns immediately;
            /// any currently pending reports will be processed asynchronously.
            /// </summary>
            internal void RequestStop()
            {
                if (Interlocked.Increment(ref m_stopRequestCounter) > 1)
                {
                    return; // already stopped
                }

                Process.LogProcessState($"Closing the write handle for FIFO '{ReportsFifoPath}'");
                // this will cause read() on the other end of the FIFO to return EOF once all native writers are done writing
                m_lazyWriteHandle.Value.Dispose();
                m_activeProcessesChecker.Cancel();

                // There are no more active processes and RequestStop() was called. This means
                // we are done processing reports, since both reads and writes on the FIFO are blocking, so
                // in order for processes to exit, we should have drained the corresponding reports already on managed side
                CompleteAccessReportProcessing();
            }

            /// <summary>Adds <paramref name="pid" /> to the set of active processes</summary>
            internal void AddPid(int pid)
            {
                bool added = m_activeProcesses.TryAdd(pid, 1);
                LogDebug($"AddPid({pid}) :: added: {added}; size: {m_activeProcesses.Count()}");
            }

            /// <summary>
            /// Removes <paramref name="pid" /> from the set of active processes.
            /// If no active processes are left thereafter, calls <see cref="RequestStop"/>.
            /// </summary>
            internal void RemovePid(int pid)
            {
                bool removed = m_activeProcesses.TryRemove(pid, out var _);
                LogDebug($"RemovePid({pid}) :: removed: {removed}; size: {m_activeProcesses.Count()}");
                if (m_activeProcesses.Count == 0)
                {
                    RequestStop();
                }
                else if (removed && pid == Process.ProcessId)
                {
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

            internal PathCacheRecord GetOrCreateCacheRecord(string path)
            {
                PathCacheRecord cacheRecord;
                if (!m_pathCache.TryGetValue(path, out cacheRecord))
                {
                    cacheRecord = new PathCacheRecord()
                    {
                        RequestedAccess = RequestedAccess.None
                    };
                    m_pathCache[path] = cacheRecord;
                }

                return cacheRecord;
            }

            internal void LogError(string message)
            {
                message = $"{message} (errno: {Marshal.GetLastWin32Error()})";
                Process.LogProcessState("[ERROR]: " + message);
                m_failureCallback?.Invoke(1, message);
            }

            internal void LogDebug(string message)
            {
                Process.LogDebug(message);
            }

            /// <nodoc />
            public void Dispose()
            {
                RequestStop();
                
                try
                {
                    m_activeProcessesChecker.Join();
                }
                catch(ThreadStateException)
                {
                    // The active process checker is only started once the main thread exits and child processes are still active. So we can start disposing the connection
                    // without that being the case
                }

                m_pathCache.Clear();
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
                    m_workerThread.Join();
                }
            }

            /// <summary>
            /// This method is backing <see cref="m_accessReportProcessingBlock"/>.
            /// </summary>
            private void ProcessBytes((PooledObjectWrapper<byte[]> wrapper, int length) item)
            {
                using (item.wrapper)
                {
                    // Format:
                    //   "%s|%d|%d|%d|%d|%d|%d|%s|%d\n", __progname, getpid(), access, status, explicitLogging, err, opcode, reportPath, isDirectory
                    string message = Encoding.GetString(item.wrapper.Instance, index: 0, count: item.length).TrimEnd('\n');

                    // parse message and create AccessReport
                    string[] parts = message.Split(new[] { '|' });
                    Contract.Assert(parts.Length == 9);
                    RequestedAccess access = (RequestedAccess)AssertInt(parts[2]);
                    string path = parts[7];

                    // ignore accesses to libDetours.so, because we injected that library
                    if (path == DetoursLibFile)
                    {
                        return;
                    }

                    var report = new AccessReport
                    {
                        Pid = (int)AssertInt(parts[1]),
                        PipId = Process.PipId,
                        RequestedAccess = (uint)access,
                        Status = AssertInt(parts[3]),
                        ExplicitLogging = AssertInt(parts[4]),
                        Error = AssertInt(parts[5]),
                        Operation = (FileOperation)AssertInt(parts[6]),
                        PathOrPipStats = Encoding.GetBytes(path),
                        IsDirectory = AssertInt(parts[8]),
                    };

                    // update active processes
                    if (report.Operation == FileOperation.OpProcessStart)
                    {
                        AddPid(report.Pid);
                    }
                    else if (report.Operation == FileOperation.OpProcessExit)
                    {
                        RemovePid(report.Pid);
                    }
                    else
                    {
                        // check the path cache (only when the message is not about process tree)
                        if (GetOrCreateCacheRecord(path).CheckCacheHitAndUpdate(access))
                        {
                            LogDebug("Cache hit for access report: " + message);
                            return;
                        }
                    }

                    // post the AccessReport
                    Process.PostAccessReport(report);
                }
            }

            private uint AssertInt(string str)
            {
                if (uint.TryParse(str, out uint result))
                {
                    return result;
                }
                else
                {
                    LogError($"Could not parse int from '{str}'");
                    return 0;
                }
            }

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
            private void StartReceivingAccessReports()
            {
                var fifoName = ReportsFifoPath;

                // opening FIFO for reading (blocks until there is at least one writer connected)
                LogDebug($"Opening FIFO '{fifoName}' for reading");
                using var readHandle = IO.Open(fifoName, IO.OpenFlags.O_RDONLY, 0);
                if (readHandle.IsInvalid)
                {
                    LogError($"Opening FIFO {fifoName} for reading failed.");
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
                        Process.LogProcessState("Exiting 'receive reports' loop.");
                        break;
                    }

                    if (numRead < 0) // error
                    {
                        LogError($"Read from FIFO {ReportsFifoPath} failed with return value {numRead}.");
                        break;
                    }

                    // decode length
                    int messageLength = BitConverter.ToInt32(messageLengthBytes, startIndex: 0);

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
                    m_accessReportProcessingBlock.Post((messageBytes, messageLength));
                }

                CompleteAccessReportProcessing();
            }
        }

        /// <inheritdoc />
        public SandboxKind Kind => SandboxKind.LinuxDetours;

        /// <inheritdoc />
        /// <remarks>Unimportant</remarks>
        public ulong MinReportQueueEnqueueTime => (ulong)DateTime.UtcNow.Ticks;

        /// <inheritdoc />
        public bool IsInTestMode { get; }

        private static readonly string s_buildXLBin = Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetLocation());

        private readonly ConcurrentDictionary<long, Info> m_pipProcesses = new ConcurrentDictionary<long, Info>();

        private readonly Sandbox.ManagedFailureCallback m_failureCallback;

        private static readonly Encoding Encoding = Encoding.UTF8;

        /// <inheritdoc />
        /// <remarks>Unimportant</remarks>
        public TimeSpan CurrentDrought => TimeSpan.FromSeconds(0);

        /// <nodoc />
        public SandboxConnectionLinuxDetours(Sandbox.ManagedFailureCallback failureCallback = null, bool isInTestMode = false)
        {
            m_failureCallback = failureCallback;
            IsInTestMode = isInTestMode;
            BuildXL.Native.Processes.ProcessUtilities.SetNativeConfiguration(BuildXL.Processes.SandboxConnection.IsInDebugMode);
        }

        /// <inheritdoc />
        public void ReleaseResources()
        {
        }

        /// <summary>
        /// Disposes the sandbox kernel extension connection and release the resources in the interop layer, when running tests this can be skipped
        /// </summary>
        public void Dispose()
        {
            ReleaseResources();
        }

        /// <inheritdoc />
        public bool NotifyUsage(uint cpuUsage, uint availableRamMB)
        {
            return true;
        }

        private static readonly string DetoursLibFile = SandboxedProcessUnix.EnsureDeploymentFile("libDetours.so");
        private static readonly string AuditLibFile = SandboxedProcessUnix.EnsureDeploymentFile("libBxlAudit.so");

        /// <inheritdoc />
        public IEnumerable<(string, string)> AdditionalEnvVarsToSet(SandboxedProcessInfo info, string uniqueName)
        {
            var detoursLibPath = info.RootJailInfo.CopyToRootJailIfNeeded(DetoursLibFile);
            (string fifoPath, string famPath) = GetPaths(info.RootJailInfo, uniqueName);

            yield return ("__BUILDXL_ROOT_PID", "1"); // CODESYNC: Public/Src/Sandbox/Linux/bxl_observer.hpp (temp solution for breakaway processes)
            yield return ("__BUILDXL_FAM_PATH", info.RootJailInfo.ToPathInsideRootJail(famPath));
            yield return ("__BUILDXL_DETOURS_PATH", detoursLibPath);

            if (info.RootJailInfo?.DisableSandboxing != true)
            {
                yield return ("LD_PRELOAD", detoursLibPath + ":" + info.EnvironmentVariables.TryGetValue("LD_PRELOAD", string.Empty));
            }

            // Auditing is disabled by default. LD_AUDIT is able to observe the dependencies on system-level libraries that LD_PRELOAD misses. These libraries
            // may include libgcc, libc, libpthread and libDetours itself. System libraries are typically not part of the fingerprint since their behavior is unlikely
            // to change (a similar effect can be achieved by enabling 'dependsOnCurrentHosOSDirectories' on DScript). In particular, that libDetours itself is detected could
            // be problematic since any change in the sandbox version will imply a cache miss.
            // In addition to that, LD_AUDIT is known to be expensive from a perf standpoint (perf analysis on some JS customers showed a 2X degradation in sandboxing overhead
            // on e2e builds when LD_AUDIT is on).
            if (info.RootJailInfo?.DisableAuditing == false)
            {
                yield return ("LD_AUDIT", info.RootJailInfo.CopyToRootJailIfNeeded(AuditLibFile) + ":" + info.EnvironmentVariables.TryGetValue("LD_AUDIT", string.Empty));
            }
        }

        private (string fifo, string fam) GetPaths(RootJailInfo? rootJailInfo, string uniqueName)
        {
            string rootDir = rootJailInfo?.RootJail ?? Path.GetTempPath();
            string fifoPath = Path.Combine(rootDir, $"bxl_{uniqueName}.fifo");
            string famPath = Path.ChangeExtension(fifoPath, ".fam");
            return (fifo: fifoPath, fam: famPath);
        }

        /// <inheritdoc />
        public bool NotifyPipStarted(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process)
        {
            if (!m_pipProcesses.TryGetValue(process.PipId, out var info))
            {
                throw new BuildXLException($"No info found for pip id {process.PipId}");
            }

            info.AddPid(process.ProcessId);
            return true;
        }

        /// <inheritdoc />
        public void NotifyPipReady(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process, Task reportCompletion)
        {
            Contract.Requires(!process.Started);
            Contract.Requires(process.PipId != 0);

            (string fifoPath, string famPath) = GetPaths(process.RootJailInfo, process.UniqueName);
            
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
                    new FileAccessSetup { DllNameX64 = string.Empty, DllNameX86 = string.Empty, ReportPath = process.ToPathInsideRootJail(fifoPath) },
                    wrapper.Instance,
                    timeoutMins: 10, // don't care
                    debugFlagsMatch: ref debugFlags);

                Contract.Assert(manifestBytes.Offset == 0);
                File.WriteAllBytes(famPath, manifestBytes.ToArray());
            }

            process.LogProcessState($"Saved FAM to '{famPath}'");

            // create a FIFO (named pipe)
            Analysis.IgnoreResult(FileUtilities.TryDeleteFile(fifoPath, retryOnFailure: false));
            if (IO.MkFifo(fifoPath, IO.FilePermissions.S_IRWXU) != 0)
            {
                throw new BuildXLException($"Creating FIFO {fifoPath} failed. (errno: {Marshal.GetLastWin32Error()})");
            }

            process.LogProcessState($"Created FIFO at '{fifoPath}'");

            // create and save info for this pip
            var info = new Info(m_failureCallback, process, fifoPath, famPath, IsInTestMode);
            if (!m_pipProcesses.TryAdd(process.PipId, info))
            {
                throw new BuildXLException($"Process with PidId {process.PipId} already exists");
            }

            // Make sure we dispose the process info after report processing is completed
            reportCompletion.ContinueWith(t => info.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.RunContinuationsAsynchronously);

            info.Start();
        }

        /// <inheritdoc />
        public void NotifyPipProcessTerminated(long pipId, int processId)
        {
            if (m_pipProcesses.TryGetValue(pipId, out var info))
            {
                info.RemovePid(processId);
            }
        }

        /// <inheritdoc />
        public void NotifyRootProcessExited(long pipId, SandboxedProcessUnix process)
        {
            if (m_pipProcesses.TryGetValue(pipId, out var info))
            {
                info.RemovePid(process.ProcessId);
            }
        }

        /// <inheritdoc />
        public bool NotifyPipFinished(long pipId, SandboxedProcessUnix process)
        {
            if (m_pipProcesses.TryRemove(pipId, out _))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
