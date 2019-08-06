// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BuildXL.Interop.MacOS;
using BuildXL.Native.Processes;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// A class that manages the connection to the macOS EndpointSecurity subsystem
    /// </summary>
    public sealed class SandboxConnectionES : ISandboxConnection
    {
        /// <inheritdoc />
        public bool MeasureCpuTimes { get; }

        /// <inheritdoc />
        public ulong MinReportQueueEnqueueTime => Volatile.Read(ref m_reportQueueLastEnqueueTime);

        /// <inheritdoc />
        public bool IsInTestMode { get; }

        private readonly ConcurrentDictionary<long, SandboxedProcessMac> m_pipProcesses = new ConcurrentDictionary<long, SandboxedProcessMac>();

        // TODO: remove at some later point
        private Sandbox.KextConnectionInfo m_fakeKextConnectionInfo = new Sandbox.KextConnectionInfo();
        private Sandbox.ESConnectionInfo m_esConnectionInfo;
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
        /// Initializes the ES sandbox
        /// </summary>
        public SandboxConnectionES()
        {
            m_reportQueueLastEnqueueTime = 0;
            m_esConnectionInfo = new Sandbox.ESConnectionInfo() { Error = Sandbox.SandboxSuccess };

            MeasureCpuTimes = false;
            IsInTestMode = false;

            var process = System.Diagnostics.Process.GetCurrentProcess();

            Sandbox.InitializeEndpointSecuritySandbox(ref m_esConnectionInfo, process.Id);
            if (m_esConnectionInfo.Error != Sandbox.SandboxSuccess)
            {
                throw new BuildXLException($@"Unable to connect to EndpointSecurity sandbox (Code: {m_esConnectionInfo.Error})");
            }

#if DEBUG
            ProcessUtilities.SetNativeConfiguration(true);
#else
            ProcessUtilities.SetNativeConfiguration(false);
            
#endif

            m_workerThread = new Thread(() => StartReceivingAccessReports());
            m_workerThread.Name = "EndpointSecurityCallbackProcessor";
            m_workerThread.Priority = ThreadPriority.Highest;
            m_workerThread.IsBackground = true;
            m_workerThread.Start();
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
            Sandbox.DeinitializeEndpointSecuritySandbox(m_esConnectionInfo);
            m_workerThread.Join();
        }

        /// <summary>
        /// Starts listening for reports from the EndpointSecurity sandbox
        /// </summary>
        private void StartReceivingAccessReports()
        {
            Sandbox.AccessReportCallback callback = (Sandbox.AccessReport report, int code) =>
            {   
                if (code != Sandbox.ReportQueueSuccessCode)
                {
                    var message = "EndpointSecurity event delivery failed with error: " + code;
                    throw new BuildXLException(message, ExceptionRootCause.MissingRuntimeDependency);
                }
                
                // Stamp the access report as it has been dequeued at this point
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

            Sandbox.ObserverFileAccessReports(ref m_esConnectionInfo, callback, Marshal.SizeOf<Sandbox.AccessReport>());
        }

        /// <inheritdoc />
        public bool NotifyUsage(uint cpuUsage, uint availableRamMB)
        {
            // TODO: Will we need this?
            return true;
        }

        /// <inheritdoc />
        public bool NotifyPipStarted(FileAccessManifest fam, SandboxedProcessMac process)
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
                    setup,
                    wrapper.Instance,
                    timeoutMins: 10, // don't care because on Mac we don't kill the process from the sandbox once it times out
                    debugFlagsMatch: ref debugFlags);

                Contract.Assert(manifestBytes.Offset == 0);

                var result = Sandbox.SendPipStarted(
                    processId: process.ProcessId,
                    pipId: fam.PipId,
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
        public bool NotifyProcessFinished(long pipId, SandboxedProcessMac process)
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
