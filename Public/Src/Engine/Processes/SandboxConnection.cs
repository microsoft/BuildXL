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
using BuildXL.Interop.Unix;
using BuildXL.Native.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Processes
{
    /// <summary>
    /// A class that manages the connection to the generic macOS sandboxing implementation
    /// </summary>
    public sealed class SandboxConnection : ISandboxConnection
    {
        /// <inheritdoc />
        public SandboxKind Kind { get; }

        /// <inheritdoc />
        public ulong MinReportQueueEnqueueTime => Volatile.Read(ref m_reportQueueLastEnqueueTime);

        /// <inheritdoc />
        public bool IsInTestMode { get; }

        /// <nodoc />
        public static bool IsInDebugMode { get; } = 
#if DEBUG
            true;
#else
            false;
#endif

        private readonly ConcurrentDictionary<long, SandboxedProcessUnix> m_pipProcesses = new ConcurrentDictionary<long, SandboxedProcessUnix>();

        // TODO: remove at some later point
        private Sandbox.KextConnectionInfo m_fakeKextConnectionInfo = new Sandbox.KextConnectionInfo();

        private readonly Sandbox.SandboxConnectionInfo m_sandboxConnectionInfo;

        /// <summary>
        /// Enqueue time of the last received report (or 0 if no reports have been received)
        /// </summary>
        private ulong m_reportQueueLastEnqueueTime;

        /// <summary>
        /// The time (in ticks) when the last report was received.
        /// </summary>
        private long m_lastReportReceivedTimestampTicks = DateTime.UtcNow.Ticks;

        private long LastReportReceivedTimestampTicks => Volatile.Read(ref m_lastReportReceivedTimestampTicks);

        private Sandbox.AccessReportCallback m_AccessReportCallback;

        static private Sandbox.Configuration ConfigurationForSandboxKind(SandboxKind kind)
        {
            switch (kind)
            {
                case SandboxKind.MacOsEndpointSecurity:
                    return Sandbox.Configuration.EndpointSecuritySandboxType;
                case SandboxKind.MacOsDetours:
                    return Sandbox.Configuration.DetoursSandboxType;
                case SandboxKind.MacOsHybrid:
                    return Sandbox.Configuration.HybridSandboxType;
                case SandboxKind.MacOsKext:
                    return Sandbox.Configuration.KextType;
                default:
                    throw new BuildXLException($"Could not find mapping for sandbox configration with sandbox kind: {kind}", ExceptionRootCause.FailFast);
            }
        }

        /// <inheritdoc />
        public TimeSpan CurrentDrought => DateTime.UtcNow.Subtract(new DateTime(ticks: LastReportReceivedTimestampTicks));

        /// <summary>
        /// Initializes the ES sandbox
        /// </summary>
        public SandboxConnection(SandboxKind kind, bool isInTestMode = false)
        {
            Kind = kind;
            m_reportQueueLastEnqueueTime = 0;
            m_sandboxConnectionInfo = new Sandbox.SandboxConnectionInfo()
            {
                Config = ConfigurationForSandboxKind(kind),
                Error = Sandbox.SandboxSuccess
            };

            IsInTestMode = isInTestMode;

            var process = System.Diagnostics.Process.GetCurrentProcess();
            Sandbox.InitializeSandbox(ref m_sandboxConnectionInfo, process.Id);
            if (m_sandboxConnectionInfo.Error != Sandbox.SandboxSuccess)
            {
                throw new BuildXLException($@"Unable to initialize generic sandbox, please check the sources for error code: {m_sandboxConnectionInfo.Error})");
            }

            ProcessUtilities.SetNativeConfiguration(IsInDebugMode);

            m_AccessReportCallback = (Sandbox.AccessReport report, int code) =>
            {
                if (code != Sandbox.ReportQueueSuccessCode)
                {
                    var message = "Sandbox event delivery failed with error: " + code;
                    throw new BuildXLException(message, ExceptionRootCause.MissingRuntimeDependency);
                }

                // Stamp the access report with a dequeue timestamp
                report.Statistics.DequeueTime = Sandbox.GetMachAbsoluteTime();

                // Update last received timestamp
                Volatile.Write(ref m_lastReportReceivedTimestampTicks, DateTime.UtcNow.Ticks);

                // Remember the latest enqueue time
                Volatile.Write(ref m_reportQueueLastEnqueueTime, report.Statistics.EnqueueTime);

                // The only way it can happen that no process is found for 'report.PipId' is when that pip is
                // explicitly terminated (e.g., because it timed out or Ctrl-c was pressed)
                if (m_pipProcesses.TryGetValue(report.PipId, out var process))
                {
                    // if the process is found, its ProcessId must match the RootPid of the report.
                    if (process.ProcessId != report.RootPid)
                    {
                        throw new BuildXLException("The process id from the lookup did not match the file access report process id", ExceptionRootCause.FailFast);
                    }
                    else
                    {
                        process.PostAccessReport(report);
                    }
                }
            };

            Sandbox.ObserverFileAccessReports(ref m_sandboxConnectionInfo, m_AccessReportCallback, Marshal.SizeOf<Sandbox.AccessReport>());
        }

        /// <summary>
        /// Disposes the sandbox connection and release the resources in the interop layer, when running tests this can be skipped
        /// </summary>
        public void Dispose()
        {
            if (!IsInTestMode)
            {
                ReleaseResources();
            }
        }

        /// <summary>
        /// Releases all resources and cleans up the interop instance too
        /// </summary>
        public void ReleaseResources()
        {
            Sandbox.DeinitializeSandbox();
            m_AccessReportCallback = null;
        }

        /// <inheritdoc />
        public bool NotifyUsage(uint cpuUsage, uint availableRamMB)
        {
            // TODO: Will we need this?
            return true;
        }

        /// <inheritdoc />
        public IEnumerable<(string, string)> AdditionalEnvVarsToSet(SandboxedProcessInfo info, string uniqueName)
        {
            return Enumerable.Empty<(string, string)>();
        }

        /// <inheritdoc />
        public void NotifyPipReady(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process, Task reportCompletion) {}

        /// <inheritdoc />
        public bool NotifyPipStarted(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process)
        {
            Contract.Requires(process.Started);
            Contract.Requires(process.PipId != 0);

            if (!m_pipProcesses.TryAdd(fam.PipId, process))
            {
                throw new BuildXLException($"Process with PidId {process.PipId} already exists");
            }

            var setup = new FileAccessSetup()
            {
                DllNameX64 = string.Empty,
                DllNameX86 = string.Empty,
                ReportPath = process.ExecutableAbsolutePath, // piggybacking on ReportPath to pass full executable path
            };

            using (var wrapper = Pools.MemoryStreamPool.GetInstance())
            {
                var debugFlags = true;
                ArraySegment<byte> manifestBytes = fam.GetPayloadBytes(
                    loggingContext,
                    setup,
                    wrapper.Instance,
                    timeoutMins: 10, // don't care because on Mac we don't kill the process from the sandbox once it times out
                    debugFlagsMatch: ref debugFlags);

                Contract.Assert(manifestBytes.Offset == 0);

                var result = Sandbox.SendPipStarted(
                    processId: process.ProcessId,
                    pipId: process.PipId,
                    famBytes: manifestBytes.Array,
                    famBytesLength: manifestBytes.Count,
                    type: Sandbox.ConnectionType.EndpointSecurity,
                    info: ref m_fakeKextConnectionInfo);

                return result;
            }
        }

        /// <inheritdoc />
        public void NotifyPipProcessTerminated(long pipId, int processId)
        {
            Sandbox.SendPipProcessTerminated(pipId, processId, type: Sandbox.ConnectionType.EndpointSecurity, info: ref m_fakeKextConnectionInfo);
        }

        /// <inheritdoc />
        public void NotifyRootProcessExited(long pipId, SandboxedProcessUnix process)
        {
            // this implementation keeps track of what the root process is an when it exits,
            // so it doesn't need to be notified about it separately
        }

        /// <inheritdoc />
        public bool NotifyPipFinished(long pipId, SandboxedProcessUnix process)
        {
            if (m_pipProcesses.TryRemove(pipId, out var proc))
            {
                Contract.Assert(process == proc);
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
