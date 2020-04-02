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
        internal sealed class Info : IDisposable
        {
            internal LoggingContext LoggingContext { get; set; }
            internal SandboxedProcessUnix Process { get; set; }
            internal string ReportsFifoPath { get; set; }
            internal string FamPath { get; set; }
            internal Thread WorkerThread { get; set; }
            internal SafeFileHandle WriteHandle { get; set; }

            /// <nodoc />
            public void Dispose()
            {
                WriteHandle?.Dispose(); // this will cause read() to return EOF once all native writers are done writing
                WorkerThread?.Join();
                Analysis.IgnoreResult(FileUtilities.TryDeleteFile(ReportsFifoPath, waitUntilDeletionFinished: false));
                Analysis.IgnoreResult(FileUtilities.TryDeleteFile(FamPath, waitUntilDeletionFinished: false));
            }
        }

        /// <inheritdoc />
        public SandboxKind Kind => SandboxKind.LinuxDetours;

        // TODO: remove this property from the interface
        /// <inheritdoc />
        public bool MeasureCpuTimes { get; } = false;

        /// <inheritdoc />
        public ulong MinReportQueueEnqueueTime => Volatile.Read(ref m_reportQueueLastEnqueueTime);

        /// <inheritdoc />
        public bool IsInTestMode { get; }

        private static readonly string s_buildXLBin = Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetLocation());

        private readonly ConcurrentDictionary<long, Info> m_pipProcesses = new ConcurrentDictionary<long, Info>();

        private readonly Sandbox.ManagedFailureCallback m_failureCallback;

        /// <summary>
        /// Enqueue time of the last received report (or 0 if no reports have been received)
        /// </summary>
        private ulong m_reportQueueLastEnqueueTime;

        /// <summary>
        /// The time (in ticks) when the last report was received.
        /// </summary>
        private long m_lastReportReceivedTimestampTicks = DateTime.UtcNow.Ticks;

        private long LastReportReceivedTimestampTicks => Volatile.Read(ref m_lastReportReceivedTimestampTicks);

        private static readonly Encoding Encoding = Encoding.UTF8;

        /// <inheritdoc />
        public TimeSpan CurrentDrought => DateTime.UtcNow.Subtract(new DateTime(ticks: LastReportReceivedTimestampTicks));

        /// <nodoc />
        public SandboxConnectionLinuxDetours(Sandbox.ManagedFailureCallback failureCallback = null, bool isInTestMode = false)
        {
            IsInTestMode = isInTestMode;
            m_failureCallback = failureCallback;
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
        }

        /// <inheritdoc />
        public bool NotifyPipStarted(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process)
        {
            Contract.Requires(process.Started);
            Contract.Requires(process.PipId != 0);

            string tempDirPath = Path.GetTempPath();
            string fifoPath = Path.Combine(tempDirPath, $"Pip{process.PipSemiStableHash:X}.{process.PipId}.{process.ProcessId}.fifo");
            string famPath = Path.ChangeExtension(fifoPath, ".fam");

            var info = new Info
            {
                LoggingContext = loggingContext,
                Process = process,
                ReportsFifoPath = fifoPath,
                FamPath = famPath,
            };

            if (!m_pipProcesses.TryAdd(process.PipId, info))
            {
                throw new BuildXLException($"Process with PidId {process.PipId} already exists");
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

            // create a FIFO (named pipe)
            if (IO.MkFifo(fifoPath, IO.FilePermissions.S_IRWXU) != 0)
            {
                logError($"Creating FIFO {fifoPath} failed");
                return false;
            }

            // start a background thread for reading from the FIFO
            info.WorkerThread = new Thread(() => StartReceivingAccessReports(info));
            info.WorkerThread.IsBackground = true;
            info.WorkerThread.Priority = ThreadPriority.Highest;
            info.WorkerThread.Start();

            return true;

            void logError(string message)
            {
                LogError(info, message);
            }
        }

        private void LogError(Info info, string message)
        {
            Logger.Log.PipProcessStartFailed(info.LoggingContext, info.Process.PipSemiStableHash, info.Process.PipDescription, Marshal.GetLastWin32Error(), message);
            m_failureCallback?.Invoke(1, message);
        }

        private void ProcessBytes(Info info, byte[] bytes)
        {
            // Format:
            //   "%s|%d|%d|%d|%d|%d|%s|%d|%s\n", __progname, getpid(), access, status, explicitLogging, err, operation, opcode, reportPath
            string message = Encoding.GetString(bytes).TrimEnd('\n');
            string[] parts = message.Split(new[] { '|' });
            Contract.Assert(parts.Length == 9);
            var report = new AccessReport
            {
                Pid = (int)AssertInt(info, parts[1]),
                PipId = info.Process.PipId,
                RequestedAccess = AssertInt(info, parts[2]),
                Status = AssertInt(info, parts[3]),
                ExplicitLogging = AssertInt(info, parts[4]),
                Error = AssertInt(info, parts[5]),
                Operation = (FileOperation) AssertInt(info, parts[7]),
                PathOrPipStats = Encoding.GetBytes(parts[8]),
            };
            info.Process.PostAccessReport(report);
        }

        private uint AssertInt(Info info, string str)
        {
            if (uint.TryParse(str, out uint result))
            {
                return result;
            }
            else
            {
                LogError(info, $"Could not parse int from '{str}'");
                return 0;
            }
        }

        private int Read(SafeFileHandle handle, byte[] buffer, int offset, int length)
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

        private void StartReceivingAccessReports(Info info)
        {
            var fifoName = info.ReportsFifoPath;

            // opening FIFO for reading (blocks until there is at least one writer connected)
            using var readHandle = IO.Open(fifoName, IO.OpenFlags.O_RDONLY, 0);
            if (readHandle.IsInvalid)
            {
                LogError(info, $"Opening FIFO {fifoName} for reading failed");
                return;
            }

            // opening FIFO for writing so that the 'read' syscall doesn't receive EOF until we close this writer
            info.WriteHandle = IO.Open(fifoName, IO.OpenFlags.O_WRONLY, 0);
            if (info.WriteHandle.IsInvalid)
            {
                LogError(info, $"Opening FIFO {fifoName} for writing failed");
                return;
            }

            // action block where parsing and processing of received bytes is offloaded (so that the read loop below is as tight as possible)
            var actionBlock = new ActionBlock<byte[]>((byte[] bytes) => ProcessBytes(info, bytes), new ExecutionDataflowBlockOptions
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
                    LogError(info, $"Read from FIFO {info.ReportsFifoPath} failed with return value {numRead}");
                    break;
                }

                // decode length
                int messageLength = BitConverter.ToInt32(messageLengthBytes, startIndex: 0);

                // read a message of that length
                byte[] messageBytes = new byte[messageLength];
                numRead = Read(readHandle, messageBytes, 0, messageBytes.Length);
                if (numRead < messageBytes.Length)
                {
                    LogError(info, $"Read from FIFO {info.ReportsFifoPath} failed with return value {numRead}");
                    break;
                }

                // Update last received timestamp
                long now = DateTime.UtcNow.Ticks;
                Volatile.Write(ref m_lastReportReceivedTimestampTicks, now);
                Volatile.Write(ref m_reportQueueLastEnqueueTime, (ulong)now);

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
            info.Process.PostAccessReport(report);
        }

        /// <inheritdoc />
        public void NotifyPipProcessTerminated(long pipId, int processId)
        {
        }

        /// <inheritdoc />
        public void NotifyRootProcessExited(long pipId, SandboxedProcessUnix process)
        {
            if (m_pipProcesses.TryRemove(pipId, out var info))
            {
                Contract.Assert(process == info.Process);
                info.Dispose();
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
