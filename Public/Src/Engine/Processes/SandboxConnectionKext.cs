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
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Processes
{
    /// <summary>
    /// A class that manages the connection to the macOS kernel extension and provides utilities to communicate with the sandbox kernel extension
    /// </summary>
    public sealed class SandboxConnectionKext : ISandboxConnection
    {
        /// <inheritdoc />
        public SandboxKind Kind => SandboxKind.MacOsKext;

        /// <summary>
        /// Configuration for <see cref="SandboxConnectionKext"/>.
        /// </summary>
        public sealed class Config
        {
            /// <summary>
            /// Whether to measure user/system CPU times of sandboxed processes.
            /// </summary>
            public bool MeasureCpuTimes;

            /// <summary>
            /// Configuration for the kernel extension.
            /// </summary>
            public Sandbox.KextConfig? KextConfig;

            /// <summary>
            /// Callback to invoke in the case of an irrecoverable kernel extension error.
            /// </summary>
            public Sandbox.ManagedFailureCallback FailureCallback;
        }

        /// <inheritdoc />
        public ulong MinReportQueueEnqueueTime => Volatile.Read(ref m_reportQueueLastEnqueueTime);

        /// <inheritdoc />
        public bool IsInTestMode { get; }

        private const string KextInstallHelperFormat =
@"

Use the the following command to load/reload the sandbox kernel extension and fix this issue:

----> sudo /bin/bash '{0}/bxl.sh' --load-kext <----

";

        private static readonly string s_buildXLBin = Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetLocation());

        private string KextInstallHelper { get; } = string.Format(KextInstallHelperFormat, s_buildXLBin);

        /// <summary>
        /// <see cref="BuildXL.Pips.Fingerprints.KextInfo.RequiredKextVersionNumber"/>
        /// </summary>
        public const string RequiredKextVersionNumber = BuildXL.Pips.Fingerprints.KextInfo.RequiredKextVersionNumber;

        /// <summary>
        /// See TN2420 (https://developer.apple.com/library/archive/technotes/tn2420/_index.html) on how versioning numbers are formatted in the Apple ecosystem
        /// </summary>
        private const int MaxVersionNumberLength = 17;

        private readonly ConcurrentDictionary<long, SandboxedProcessUnix> m_pipProcesses = new ConcurrentDictionary<long, SandboxedProcessUnix>();

        private Sandbox.KextConnectionInfo m_kextConnectionInfo;
        private readonly Sandbox.KextSharedMemoryInfo m_sharedMemoryInfo;
        private readonly Sandbox.ManagedFailureCallback m_failureCallback;
        private readonly Thread m_workerThread;

        /// <summary>
        /// Enqueue time of the last received report (or 0 if no reports have been received)
        /// </summary>
        private ulong m_reportQueueLastEnqueueTime;

        /// <summary>
        /// The time (in ticks) when the last report was received.
        /// </summary>
        private long m_lastReportReceivedTimestampTicks = DateTime.UtcNow.Ticks;

        private long LastReportReceivedTimestampTicks => Volatile.Read(ref m_lastReportReceivedTimestampTicks);

        /// <inheritdoc />
        public TimeSpan CurrentDrought => DateTime.UtcNow.Subtract(new DateTime(ticks: LastReportReceivedTimestampTicks));

        /// <summary>
        /// Initializes the sandbox kernel extension connection, setting up the kernel extension connection and workers that drain the
        /// kernel event queue and report file accesses
        /// </summary>
        public SandboxConnectionKext(Config config = null, bool skipDisposingForTests = false)
        {
            m_reportQueueLastEnqueueTime = 0;
            m_kextConnectionInfo = new Sandbox.KextConnectionInfo() { Error = Sandbox.SandboxSuccess };
            m_sharedMemoryInfo = new Sandbox.KextSharedMemoryInfo() { Error = Sandbox.SandboxSuccess };

            IsInTestMode = skipDisposingForTests;

            // initialize kext connection
            Sandbox.InitializeKextConnection(ref m_kextConnectionInfo);
            if (m_kextConnectionInfo.Error != Sandbox.SandboxSuccess)
            {
                throw new BuildXLException($@"Unable to connect to sandbox kernel extension (Code: {m_kextConnectionInfo.Error}) - make sure it is loaded and retry! {KextInstallHelper}");
            }

            // check and set if the sandbox is running in debug configuration
            bool isDebug = false;
            Sandbox.CheckForDebugMode(ref isDebug, m_kextConnectionInfo);
            ProcessUtilities.SetNativeConfiguration(isDebug);

#if DEBUG
            if (!ProcessUtilities.IsNativeInDebugConfiguration())
#else
            if (ProcessUtilities.IsNativeInDebugConfiguration())
#endif
            {
                throw new BuildXLException($"Sandbox kernel extension build flavor mismatch - the extension must match the engine build flavor, Debug != Release. {KextInstallHelper}");
            }

            // check if the sandbox version matches
            var stringBufferLength = MaxVersionNumberLength + 1;
            var version = new StringBuilder(stringBufferLength);
            Sandbox.KextVersionString(version, stringBufferLength);

            if (!RequiredKextVersionNumber.Equals(version.ToString().TrimEnd('\0')))
            {
                throw new BuildXLException($"Sandbox kernel extension version mismatch, the loaded kernel extension version '{version}' does not match the required version '{RequiredKextVersionNumber}'. {KextInstallHelper}");
            }

            if (config?.KextConfig != null)
            {
                if (!Sandbox.Configure(config.KextConfig.Value, m_kextConnectionInfo))
                {
                    throw new BuildXLException($"Unable to configure sandbox kernel extension");
                }
            }

            m_failureCallback = config?.FailureCallback;

            // Initialize the shared memory region
            Sandbox.InitializeKextSharedMemory(m_kextConnectionInfo, ref m_sharedMemoryInfo);
            if (m_sharedMemoryInfo.Error != Sandbox.SandboxSuccess)
            {
                throw new BuildXLException($"Unable to allocate shared memory region for worker (Code:{m_sharedMemoryInfo.Error})");
            }

            if (!SetFailureNotificationHandler())
            {
                throw new BuildXLException($"Unable to set sandbox kernel extension failure notification callback handler");
            }

            m_workerThread = new Thread(() => StartReceivingAccessReports(m_sharedMemoryInfo.Address, m_sharedMemoryInfo.Port));
            m_workerThread.IsBackground = true;
            m_workerThread.Priority = ThreadPriority.Highest;
            m_workerThread.Start();
        }

        private unsafe bool SetFailureNotificationHandler()
        {
            return Sandbox.SetFailureNotificationHandler(KextFailureCallback, m_kextConnectionInfo);
        }

        private unsafe void KextFailureCallback(void* refCon, int status)
        {
            m_failureCallback?.Invoke(status, $"Unrecoverable kernel extension failure happened - try reloading the kernel extension or restart your system. {KextInstallHelper}");
        }

        /// <summary>
        /// Disposes the sandbox kernel extension connection and release the resources in the interop layer, when running tests this can be skipped
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
            Sandbox.DeinitializeKextSharedMemory(m_sharedMemoryInfo, m_kextConnectionInfo);

            m_workerThread.Join();

            Sandbox.DeinitializeKextConnection(m_kextConnectionInfo);
        }

        /// <summary>
        /// Starts listening for reports from the kernel extension on a dedicated thread
        /// </summary>
        private void StartReceivingAccessReports(ulong address, uint port)
        {
            Sandbox.AccessReportCallback callback = (Sandbox.AccessReport report, int code) =>
            {
                if (code != Sandbox.ReportQueueSuccessCode)
                {
                    var message = "Kernel extension report queue failed with error: " + code;
                    throw new BuildXLException(message, Utilities.ExceptionRootCause.MissingRuntimeDependency);
                }

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
                        m_failureCallback?.Invoke(-1, $"Unexpected PID for Pip {report.PipId:X}: Expected {process.ProcessId}, Reported {report.RootPid}");
                    }
                    else
                    {
                        process.PostAccessReport(report);
                    }
                }
            };

            Sandbox.ListenForFileAccessReports(callback, Marshal.SizeOf<Sandbox.AccessReport>(), address, port);
        }

        /// <inheritdoc />
        public bool NotifyUsage(uint cpuUsage, uint availableRamMB)
        {
            return Sandbox.UpdateCurrentResourceUsage(cpuUsage, availableRamMB, m_kextConnectionInfo);
        }

        /// <inheritdoc />
        public void NotifyPipReady(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process, Task reportCompletion) {}

        /// <inheritdoc />
        public bool NotifyPipStarted(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process)
        {
            Contract.Requires(process.Started);
            Contract.Requires(fam.PipId != 0);

            if (!m_pipProcesses.TryAdd(fam.PipId, process))
            {
                throw new BuildXLException($"Process with PidId {fam.PipId} already exists");
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
                    timeoutMins: 10, // don't care because on Mac we don't kill the process from the Kext once it times out
                    debugFlagsMatch: ref debugFlags);

                Contract.Assert(manifestBytes.Offset == 0);

                var result = Sandbox.SendPipStarted(
                    processId: process.ProcessId,
                    pipId: fam.PipId,
                    famBytes: manifestBytes.Array,
                    famBytesLength: manifestBytes.Count,
                    type: Sandbox.ConnectionType.Kext,
                    info: ref m_kextConnectionInfo);

                return result;
            }
        }

        /// <inheritdoc />
        public IEnumerable<(string, string)> AdditionalEnvVarsToSet(SandboxedProcessInfo info, string uniqueName)
        {
            return Enumerable.Empty<(string, string)>();
        }

        /// <inheritdoc />
        public void NotifyPipProcessTerminated(long pipId, int processId)
        {
            Sandbox.SendPipProcessTerminated(pipId, processId, type: Sandbox.ConnectionType.Kext, info: ref m_kextConnectionInfo);
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
