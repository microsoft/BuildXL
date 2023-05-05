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
using BuildXL.Utilities.Core;
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
        // Used to signal the end of reports through the FIFO. The value -21 is arbitrary,
        // it could be any negative value, as it will be read in place of a value representing
        // a length, which could take any positive value.
        private const int EndOfReportsSentinel = -21;

        private static readonly string s_detoursLibFile = SandboxedProcessUnix.EnsureDeploymentFile("libDetours.so");
        private static readonly string s_auditLibFile = SandboxedProcessUnix.EnsureDeploymentFile("libBxlAudit.so");

        /// <summary>
        /// Environment variable containing the path to the file access manifest to be read by the detoured process.
        /// </summary>
        public static readonly string BuildXLFamPathEnvVarName = "__BUILDXL_FAM_PATH";

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
            /// <summary>
            /// This fifo is used to communication between the process and BuildXL for non-file access related messages.
            /// We use a second pipe here because it is possible for the reports pipe to drain slow due to a large number of messages.
            /// </summary>
            internal string SecondaryFifoPath { get; }
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
            private readonly Lazy<SafeFileHandle> m_lazySecondaryFifoWriteHandle;
            private readonly Thread m_workerThread;
            private readonly Thread m_secondaryWorkerThread;
            private readonly ActionBlockSlim<(PooledObjectWrapper<byte[]> wrapper, int length)> m_accessReportProcessingBlock;

            private int m_stopRequestCounter;
            private int m_completeAccessReportProcessingCounter;

            private static readonly TimeSpan ActiveProcessesCheckerInterval = TimeSpan.FromSeconds(1);

            private static ArrayPool<byte> ByteArrayPool { get; } = new ArrayPool<byte>(4096);

            internal Info(Sandbox.ManagedFailureCallback failureCallback, SandboxedProcessUnix process, string reportsFifoPath, string secondaryFifoPath, string famPath,  bool isInTestMode)
            {
                m_isInTestMode = isInTestMode;
                m_stopRequestCounter = 0;
                m_completeAccessReportProcessingCounter = 0;
                m_failureCallback = failureCallback;
                Process = process;
                ReportsFifoPath = reportsFifoPath;
                SecondaryFifoPath = secondaryFifoPath;
                FamPath = famPath;

                m_pathCache = new Dictionary<string, PathCacheRecord>();
                m_activeProcesses = new ConcurrentDictionary<int, byte>();
                m_activeProcessesChecker = new CancellableTimedAction(
                    CheckActiveProcesses,
                    intervalMs: Math.Min((int)process.ChildProcessTimeout.TotalMilliseconds, (int)ActiveProcessesCheckerInterval.TotalMilliseconds));

                // create a write handle (used to keep the fifo open, i.e.,
                // the 'read' syscall won't receive EOF until we close this writer
                m_lazyWriteHandle = GetLazyWriteHandle(ReportsFifoPath);

                // action block where parsing and processing of received ActionReport bytes is done
                m_accessReportProcessingBlock = ActionBlockSlim.Create<(PooledObjectWrapper<byte[]> wrapper, int length)>(
                    degreeOfParallelism: 1,
                    ProcessBytes,
                    singleProducedConstrained: true // Only m_workerThread posts to the action block
                    );

                // start a background thread for reading from the FIFO
                m_workerThread = new Thread(() => StartReceivingAccessReports(ReportsFifoPath, m_lazyWriteHandle));
                m_workerThread.IsBackground = true;
                m_workerThread.Priority = ThreadPriority.Highest;

                // Second thread for reading the secondary FIFO
                // The secondary pipe is used here to allow for messages that are higher priority (such as ptrace notifcations)
                // to be delivered back to the managed layer faster if the fifo used for file access reports is congested.
                if (!string.IsNullOrEmpty(SecondaryFifoPath))
                {
                    m_lazySecondaryFifoWriteHandle = GetLazyWriteHandle(SecondaryFifoPath);

                    m_secondaryWorkerThread = new Thread(() => StartReceivingAccessReports(SecondaryFifoPath, m_lazySecondaryFifoWriteHandle));
                    m_secondaryWorkerThread.IsBackground = true;
                    m_secondaryWorkerThread.Priority = ThreadPriority.Highest;
                }
            }

            private Lazy<SafeFileHandle> GetLazyWriteHandle(string path)
            {
                return new Lazy<SafeFileHandle>(() =>
                {
                    Process.LogDebug($"Opening FIFO '{path}' for writing");
                    return IO.Open(path, IO.OpenFlags.O_WRONLY, 0);
                });
            }

            /// <summary>
            /// Starts receiving access reports
            /// </summary>
            internal void Start()
            {
                m_workerThread.Start();
                if (!string.IsNullOrEmpty(SecondaryFifoPath))
                {
                    m_secondaryWorkerThread.Start();
                }
            }

            private void CheckActiveProcesses()
            {
                foreach (var pid in m_activeProcesses.Keys)
                {
                    if (!Dispatch.IsProcessAlive(pid))
                    {
                        Process.LogDebug("CheckActiveProcesses");
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
                    Process.LogDebug("Posting OpProcessTreeCompleted message");
                    Process.PostAccessReport(new AccessReport
                    {
                        Operation = FileOperation.OpProcessTreeCompleted,
                        PathOrPipStats = AccessReport.EncodePath("")
                    });
                });
            }

            /// <summary>
            /// Request to stop receiving access reports. 
            /// Any currently pending reports will be processed asynchronously.
            /// </summary>
            internal void RequestStop()
            {
                if (Interlocked.Increment(ref m_stopRequestCounter) > 1)
                {
                    return; // already stopped
                }

                // If RequestStop was called, it means there are no more active processes, so all the sandbox reports  
                // should have been already writen to the FIFO. However, the thread consuming the FIFO might still be
                // running, as it reads the FIFO message-by-message, and the pipe might still hold some reports at this point.
                // To signal to that thread that the reports have finished, we push a sentinel value through the pipe. 
                // Just closing the write handle would be enough to produce an EOF on the FIFO, which we could use as
                // the termination signal: in practice, the reception of EOF was observed to happen with a considerable
                // delay after calling Dispose on the handle, and communicating the sentinel was immediate, so this approach
                // was preferred.
                try
                {
                    writeEndOfReportSentinal(m_lazyWriteHandle);   
                    if (!string.IsNullOrEmpty(SecondaryFifoPath))
                    {
                        writeEndOfReportSentinal(m_lazySecondaryFifoWriteHandle);
                    }
                }
                catch (Exception e)
                {
                    LogError($"An exception ocurred while writing EndOfReportsSentinel to the FIFO. Details: {e}");
                }
                finally 
                { 
                    Process.LogDebug($"Closing the write handle for FIFO '{ReportsFifoPath}'");
                    m_lazyWriteHandle.Value.Dispose();
                    if (!string.IsNullOrEmpty(SecondaryFifoPath))
                    {
                        m_lazySecondaryFifoWriteHandle.Value.Dispose();
                    }
                    m_activeProcessesChecker.Cancel();
                }

                void writeEndOfReportSentinal(Lazy<SafeFileHandle> handle)
                {
                    using var fileStream = new FileStream(handle.Value, FileAccess.Write);
                    using var binaryWriter = new BinaryWriter(fileStream);
                    binaryWriter.Write(EndOfReportsSentinel);
                }
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
                    if (!string.IsNullOrEmpty(SecondaryFifoPath))
                    {
                        m_secondaryWorkerThread.Join();
                    }
                }
            }

            /// <summary>
            /// This method is backing <see cref="m_accessReportProcessingBlock"/>.
            /// </summary>
            private void ProcessBytes((PooledObjectWrapper<byte[]> wrapper, int length) item)
            {
                using (item.wrapper)
                {

                    var message = s_encoding.GetString(item.wrapper.Instance, index: 0, count: item.length).AsSpan().TrimEnd('\n');

                    // parse the message, consuming the span field by field. The format is:
                    //  "%s|%d|%d|%d|%d|%d|%d|%s|%d\n", __progname, getpid(), access, status, explicitLogging, err, opcode, reportPath, isDirectory
                    var restOfMessage = message;
                    _ = nextField(restOfMessage, out restOfMessage);  // ignore progname
                    var pid = AssertInt(nextField(restOfMessage, out restOfMessage));
                    var access = (RequestedAccess)AssertInt(nextField(restOfMessage, out restOfMessage));
                    var status = AssertInt(nextField(restOfMessage, out restOfMessage));
                    var explicitlogging = AssertInt(nextField(restOfMessage, out restOfMessage));
                    var err = AssertInt(nextField(restOfMessage, out restOfMessage));
                    var opCode = AssertInt(nextField(restOfMessage, out restOfMessage));
                    var path = nextField(restOfMessage, out restOfMessage);
                    var isDirectory = AssertInt(nextField(restOfMessage, out restOfMessage));
                    Contract.Assert(restOfMessage.IsEmpty);  // We should have reached the end of the message
                                                             
                    // ignore accesses to libDetours.so, because we injected that library
                    if (path.SequenceEqual(s_detoursLibFile.AsSpan()))
                    {
                        return;
                    }

                    var report = new AccessReport
                    {
                        Pid = (int)pid,
                        PipId = Process.PipId,
                        RequestedAccess = (uint)access,
                        Status = status,
                        ExplicitLogging = explicitlogging,
                        Error = err,
                        Operation = (FileOperation)opCode,
                        PathOrPipStats = s_encoding.GetBytes(path.ToArray()),
                        IsDirectory = isDirectory,
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
                    // We don't want to check the path cache for statically linked processes
                    // because we rely on this report to start the ptrace sandbox
                    else if (report.Operation != FileOperation.OpStaticallyLinkedProcess)
                    {
                        var pathStr = path.ToString();
                        // check the path cache (only when the message is not about process tree)                        
                        if (GetOrCreateCacheRecord(pathStr).CheckCacheHitAndUpdate(access))
                        {
                            LogDebug($"Cache hit for access report: ({pathStr}, {access})");
                            return;
                        }
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
            private void StartReceivingAccessReports(string fifoName, Lazy<SafeFileHandle> fifoHandle)
            {
                // opening FIFO for reading (blocks until there is at least one writer connected)
                LogDebug($"Opening FIFO '{fifoName}' for reading");
                using var readHandle = IO.Open(fifoName, IO.OpenFlags.O_RDONLY, 0);
                if (readHandle.IsInvalid)
                {
                    LogError($"Opening FIFO {fifoName} for reading failed.");
                    return;
                }

                // make sure that m_lazyWriteHandle has been created
                Analysis.IgnoreResult(fifoHandle.Value);

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
                        LogError($"Read from FIFO {fifoName} failed with return value {numRead}.");
                        break;
                    }

                    // decode length
                    int messageLength = BitConverter.ToInt32(messageLengthBytes, startIndex: 0);

                    // if 'length' corresponds to this special (negative) value that is pumped 
                    // to the FIFO after the process tree has completed, then we have drained all reports
                    // (see 'RequestStop' for more details).
                    if (messageLength == EndOfReportsSentinel)
                    {
                        Process.LogDebug("Exiting 'receive reports' loop.");
                        break;
                    }

                    // read a message of that length
                    PooledObjectWrapper<byte[]> messageBytes = ByteArrayPool.GetInstance(messageLength);
                    numRead = Read(readHandle, messageBytes.Instance, 0, messageLength);
                    if (numRead < messageLength)
                    {
                        LogError($"Read from FIFO {fifoName} failed: read only {numRead} out of {messageLength} bytes.");
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

        /// <summary>
        /// Name of PTrace runner file.
        /// </summary>
        public const string PTraceRunnerFileName = "ptracerunner";

        private readonly ConcurrentDictionary<long, Info> m_pipProcesses = new();

        private readonly ManagedFailureCallback m_failureCallback;

        private static readonly Encoding s_encoding = Encoding.UTF8;

        /// <inheritdoc />
        /// <remarks>Unimportant</remarks>
        public TimeSpan CurrentDrought => TimeSpan.FromSeconds(0);

        /// <nodoc />
        public SandboxConnectionLinuxDetours(ManagedFailureCallback failureCallback = null, bool isInTestMode = false)
        {
            m_failureCallback = failureCallback;
            IsInTestMode = isInTestMode;
            Native.Processes.ProcessUtilities.SetNativeConfiguration(SandboxConnection.IsInDebugMode);
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

        /// <inheritdoc />
        public IEnumerable<(string, string)> AdditionalEnvVarsToSet(SandboxedProcessInfo info, string uniqueName)
        {
            var detoursLibPath = info.RootJailInfo.CopyToRootJailIfNeeded(s_detoursLibFile);
            (_, _, string famPath) = GetPaths(info.RootJailInfo, uniqueName);

            yield return ("__BUILDXL_ROOT_PID", "1"); // CODESYNC: Public/Src/Sandbox/Linux/common.h (temp solution for breakaway processes)
            yield return (BuildXLFamPathEnvVarName, info.RootJailInfo.ToPathInsideRootJail(famPath));
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
                yield return ("LD_AUDIT", info.RootJailInfo.CopyToRootJailIfNeeded(s_auditLibFile) + ":" + info.EnvironmentVariables.TryGetValue("LD_AUDIT", string.Empty));
            }
        }

        /// <summary>
        /// Returns the paths for the FIFO and FAM based on the unique name for a pip.
        /// </summary>
        public static (string fifo, string secondaryFifo, string fam) GetPaths(RootJailInfo? rootJailInfo, string uniqueName)
        {
            string rootDir = rootJailInfo?.RootJail ?? Path.GetTempPath();
            string fifoPath = Path.Combine(rootDir, $"bxl_{uniqueName}.fifo");
            // CODESYNC: Public/Src/Sandbox/Linux/bxl_observer.cpp
            string secondaryFifoPath = Path.Combine(rootDir, $"bxl_{uniqueName}.fifo2");
            string famPath = Path.ChangeExtension(fifoPath, ".fam");
            return (fifo: fifoPath, secondaryFifo: secondaryFifoPath, fam: famPath);
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

            (string fifoPath, string secondaryFifoPath, string famPath) = GetPaths(process.RootJailInfo, process.UniqueName);
            
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

            process.LogDebug($"Saved FAM to '{famPath}'");

            // create a FIFO (named pipe)
            createNewFifo(fifoPath);

            // Secondary fifo is only used by the ptrace sandbox for now
            if (fam.EnableLinuxPTraceSandbox)
            {
                createNewFifo(secondaryFifoPath);
            }
            else
            {
                secondaryFifoPath = string.Empty;
            }

            // create and save info for this pip
            var info = new Info(m_failureCallback, process, fifoPath, secondaryFifoPath, famPath, IsInTestMode);
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
        public bool NotifyPipFinished(long pipId, SandboxedProcessUnix process) => m_pipProcesses.TryRemove(pipId, out _);
    }
}
