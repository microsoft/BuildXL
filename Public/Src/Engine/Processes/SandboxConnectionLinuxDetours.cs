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
using System.Threading.Tasks.Dataflow;
using BuildXL.Interop.Unix;
using BuildXL.Native.IO;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
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

            internal static string GetDebugLogPath(string famPath) => Path.ChangeExtension(famPath, ".log");
            internal string DebugLogPath => GetDebugLogPath(FamPath);

            private readonly Sandbox.ManagedFailureCallback m_failureCallback;
            private readonly Dictionary<string, PathCacheRecord> m_pathCache; // TODO: use AbsolutePath instead of string
            private readonly HashSet<int> m_activeProcesses;
            private readonly Lazy<SafeFileHandle> m_lazyWriteHandle;
            private readonly Thread m_workerThread;

            internal Info(Sandbox.ManagedFailureCallback failureCallback, SandboxedProcessUnix process, string reportsFifoPath, string famPath)
            {
                m_failureCallback = failureCallback;
                Process = process;
                ReportsFifoPath = reportsFifoPath;
                FamPath = famPath;

                m_pathCache = new Dictionary<string, PathCacheRecord>();
                m_activeProcesses = new HashSet<int>
                {
                    process.ProcessId
                };

                // create a write handle (used to keep the fifo open, i.e., 
                // the 'read' syscall won't receive EOF until we close this writer
                m_lazyWriteHandle = new Lazy<SafeFileHandle>(() => 
                {
                    LogDebug($"Opening FIFO '{ReportsFifoPath}' for writing");
                    return IO.Open(ReportsFifoPath, IO.OpenFlags.O_WRONLY, 0);
                });

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

            /// <summary>
            /// Request to stop receiving access reports.  This method returns immediately;
            /// any currently pending reports will be processed asynchronously. 
            /// </summary>
            internal void RequestStop()
            {
                LogDebug($"Closing the write handle for FIFO '{ReportsFifoPath}'");
                // this will cause read() on the other end of the FIFO to return EOF once all native writers are done writing
                m_lazyWriteHandle.Value.Dispose();
            }

            /// <summary>Adds <paramref name="pid" /> to the set of active processes</summary>
            internal void AddPid(int pid)
            {
                bool added = m_activeProcesses.Add(pid);
                LogDebug($"AddPid({pid}) :: added: {added}; size: {m_activeProcesses.Count()}");
            }

            /// <summary>
            /// Removes <paramref name="pid" /> from the set of active processes.
            /// If no active processes are left thereafter, calls <see cref="RequestStop"/>.
            /// </summary>
            internal void RemovePid(int pid)
            {
                bool removed = m_activeProcesses.Remove(pid);
                LogDebug($"RemovePid({pid}) :: removed: {removed}; size: {m_activeProcesses.Count()}");
                if (m_activeProcesses.Count == 0)
                {
                    RequestStop();
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
                Process.LogDebug("[ERROR]: " + message);
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
                m_workerThread?.Join();
                m_pathCache.Clear();
                m_activeProcesses.Clear();
                Analysis.IgnoreResult(FileUtilities.TryDeleteFile(ReportsFifoPath, waitUntilDeletionFinished: false));
                Analysis.IgnoreResult(FileUtilities.TryDeleteFile(FamPath, waitUntilDeletionFinished: false));
            }

            private void ProcessBytes(byte[] bytes)
            {
                // Format:
                //   "%s|%d|%d|%d|%d|%d|%d|%s\n", __progname, getpid(), access, status, explicitLogging, err, opcode, reportPath
                string message = Encoding.GetString(bytes).TrimEnd('\n');
                LogDebug($"Processing message: {message}");

                // parse message and create AccessReport
                string[] parts = message.Split(new[] { '|' });
                Contract.Assert(parts.Length == 8);
                RequestedAccess access = (RequestedAccess)AssertInt(parts[2]);
                string path = parts[7];
                var report = new AccessReport
                {
                    Pid = (int)AssertInt(parts[1]),
                    PipId = Process.PipId,
                    RequestedAccess = (uint)access,
                    Status = AssertInt(parts[3]),
                    ExplicitLogging = AssertInt(parts[4]),
                    Error = AssertInt(parts[5]),
                    Operation = (FileOperation) AssertInt(parts[6]),
                    PathOrPipStats = Encoding.GetBytes(path),
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
                    var numRead = IO.Read(handle, buffer, offset, length);
                    if (numRead <= 0)
                    {
                        return numRead;
                    }
                    offset += numRead;
                    totalRead += numRead;
                }

                return length;
            }

            private void StartReceivingAccessReports()
            {
                var fifoName = ReportsFifoPath;

                // opening FIFO for reading (blocks until there is at least one writer connected)
                LogDebug($"Opening FIFO '{fifoName}' for reading");
                using var readHandle = IO.Open(fifoName, IO.OpenFlags.O_RDONLY, 0);
                if (readHandle.IsInvalid)
                {
                    LogError($"Opening FIFO {fifoName} for reading failed");
                    return;
                }

                // make sure that m_lazyWriteHandle has been created
                Analysis.IgnoreResult(m_lazyWriteHandle.Value);

                // action block where parsing and processing of received bytes is offloaded (so that the read loop below is as tight as possible)
                var actionBlock = new ActionBlock<byte[]>(ProcessBytes, new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = DataflowBlockOptions.Unbounded,
                    MaxDegreeOfParallelism = 1,
                    EnsureOrdered = true
                });

                while (true)
                {
                    // read length
                    byte[] messageLengthBytes = new byte[sizeof(int)];
                    var numRead = Read(readHandle, messageLengthBytes, 0, messageLengthBytes.Length);
                    if (numRead == 0) // EOF
                    {
                        break;
                    }

                    if (numRead < 0) // error
                    {
                        LogError($"Read from FIFO {ReportsFifoPath} failed with return value {numRead}");
                        break;
                    }

                    // decode length
                    int messageLength = BitConverter.ToInt32(messageLengthBytes, startIndex: 0);

                    // read a message of that length
                    byte[] messageBytes = new byte[messageLength];
                    numRead = Read(readHandle, messageBytes, 0, messageBytes.Length);
                    if (numRead < messageBytes.Length)
                    {
                        LogError($"Read from FIFO {ReportsFifoPath} failed with return value {numRead}");
                        break;
                    }

                    LogDebug($"Received a {numRead}-byte message");

                    // Add message to processing queue
                    actionBlock.Post(messageBytes);
                }

                // complete action block and wait for it to finish
                actionBlock.Complete();
                actionBlock.Completion.GetAwaiter().GetResult();

                // report process tree completed 
                var report = new AccessReport
                {
                    Operation = FileOperation.OpProcessTreeCompleted,
                    PathOrPipStats = AccessReport.EncodePath("")
                };
                Process.PostAccessReport(report);
            }
        }

        /// <inheritdoc />
        public SandboxKind Kind => SandboxKind.LinuxDetours;

        // TODO: remove this property from the interface
        /// <inheritdoc />
        public bool MeasureCpuTimes { get; } = false;

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
            IsInTestMode = isInTestMode;
            m_failureCallback = failureCallback;

#if DEBUG
            BuildXL.Native.Processes.ProcessUtilities.SetNativeConfiguration(true);
#else
            BuildXL.Native.Processes.ProcessUtilities.SetNativeConfiguration(false);
#endif
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

        private string DetoursLibFile => Path.Combine(Path.GetDirectoryName(AssemblyHelper.GetThisProgramExeLocation()), "libDetours.so");

        /// <inheritdoc />
        public IEnumerable<(string, string)> AdditionalEnvVarsToSet(long pipId)
        {
            if (!m_pipProcesses.TryGetValue(pipId, out var info))
            {
                throw new BuildXLException($"No info found for pip id {pipId}");
            }

            yield return ("__BUILDXL_FAM_PATH", info.FamPath);
            yield return ("LD_PRELOAD", DetoursLibFile);
            if (IsInTestMode)
            {
                info.LogDebug("Setting sandbox debug log path to: " + info.DebugLogPath);
                yield return ("__BUILDXL_LOG_PATH", info.DebugLogPath);
            }
        }

        /// <inheritdoc />
        public bool NotifyPipStarted(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process)
        {
            Contract.Requires(process.Started);
            Contract.Requires(process.PipId != 0);

            string fifoPath = $"{FileUtilities.GetTempPath()}.Pip{process.PipSemiStableHash:X}.{process.ProcessId}.fifo";
            string famPath = Path.ChangeExtension(fifoPath, ".fam");

            if (IsInTestMode)
            {
                fam.AddPath(toAbsPath(Info.GetDebugLogPath(famPath)), mask: FileAccessPolicy.MaskAll, values: FileAccessPolicy.AllowAll);
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
            if (IO.MkFifo(fifoPath, IO.FilePermissions.S_IRWXU) != 0)
            {
                m_failureCallback?.Invoke(1, $"Creating FIFO {fifoPath} failed");
                return false;
            }

            process.LogDebug($"Created FIFO at '{fifoPath}'");

            // create and save info for this pip
            var info = new Info(m_failureCallback, process, fifoPath, famPath);
            if (!m_pipProcesses.TryAdd(process.PipId, info))
            {
                throw new BuildXLException($"Process with PidId {process.PipId} already exists");
            }

            info.Start();
            return true;

            AbsolutePath toAbsPath(string path) => AbsolutePath.Create(process.PathTable, path);
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
            if (m_pipProcesses.TryRemove(pipId, out var info))
            {
                info.Dispose();
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
