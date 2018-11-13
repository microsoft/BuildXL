// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BuildXL.Interop.MacOS;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Processes
{
    /// <summary>
    /// A class that manages the connection to the macOS kernel extension and provides utilities to communicate with the sandbox kernel extension
    /// </summary>
    public sealed class SandboxedKextConnection : ISandboxedKextConnection
    {
        /// <summary>
        /// Number of workers connecting to the sanbox kernel extension, each with their own dedicated report queue that is processing reported file accesses
        /// </summary>
        public int NumberOfKextConnections { get; private set; }

        /// <summary>
        /// Size of a sandbox kernel extension report queue in MB (the sandbox kernel extension provides sensible defaults when this is not set)
        /// </summary>
        public int ReportQueueSizeMb { get; private set; }

        /// <summary>
        /// Until some automation for kernel extension building and deployment is in place, this number has to be kept in sync with the 'CFBundleVersion'
        /// inside the Info.plist file of the kernel extension code base. BuildXL will not work if a version mismatch is detected!
        /// </summary>
        public const string RequiredKextVersionNumber = "1.15.99";

        /// <summary>
        /// See TN2420 (https://developer.apple.com/library/archive/technotes/tn2420/_index.html) on how versioning numbers are formatted in the Apple ecosystem
        /// </summary>
        private const int m_maxVersionNumberLength = 17;

        private readonly ConcurrentDictionary<long, SandboxedProcessMacKext> m_pipProcesses = new ConcurrentDictionary<long, SandboxedProcessMacKext>();

        private readonly List<Sandbox.KextSharedMemoryInfo> m_sharedMemoryInfos = new List<Sandbox.KextSharedMemoryInfo>();

        private readonly List<Thread> m_workerThreads = new List<Thread>();

        private readonly Sandbox.KextConnectionInfoCallback m_callback;

        private readonly Sandbox.FailureNotificationCallback m_failureCallback;

        private readonly Sandbox.KextConnectionInfo m_kextConnectionInfo;

        private readonly bool m_skipDisposingForTests;

        /// <summary>
        /// Initializes the sandbox kernel extension connection manager, setting up the kernel extension connection and workers that drain the
        /// kernel event queue and report file accesses
        /// </summary>
        public SandboxedKextConnection(int numberOfKextConnections, ulong reportQueueSizeMB = 0, Sandbox.FailureNotificationCallback failureCallback = null, bool skipDisposingForTests = false)
        {
            Contract.Requires(numberOfKextConnections > 0, "The number of connections to establish to the kernel extension must at least be 1.");
            NumberOfKextConnections = numberOfKextConnections;

            m_skipDisposingForTests = skipDisposingForTests;

            m_callback = () =>
            {
                return m_kextConnectionInfo;
            };

            Sandbox.InitializeKextConnectionInfoCallback(m_callback);

            unsafe
            {
                var connectionInfo = new Sandbox.KextConnectionInfo() { Error = Sandbox.KextSuccess };
                Sandbox.InitializeKextConnection(&connectionInfo);
                if (connectionInfo.Error != Sandbox.KextSuccess)
                {
                    throw new BuildXLException($"Unable to connect to sandbox kernel extension (Code: {connectionInfo.Error}) - make sure it is loaded!");
                }

                m_kextConnectionInfo = connectionInfo;

                var stringBufferLength = m_maxVersionNumberLength + 1;
                var version = new StringBuilder(stringBufferLength);
                Sandbox.KextVersionString(version, stringBufferLength);

                if (!RequiredKextVersionNumber.Equals(version.ToString().TrimEnd('\0')))
                {
                    throw new BuildXLException($"Sandbox kernel extension version mismatch, the loaded kernel extension version '{version}' does not match the required version '{RequiredKextVersionNumber}'.");
                }

                if (reportQueueSizeMB > 0 && !Sandbox.SetReportQueueSize(reportQueueSizeMB))
                {
                    throw new BuildXLException($"Unable to set sandbox kernel extension report queue size.");
                }

                m_failureCallback = failureCallback;

                for (int count = 0; count < NumberOfKextConnections; count++)
                {
                    // Initialize the shared memory region
                    var memoryInfo = new Sandbox.KextSharedMemoryInfo() { Error = Sandbox.KextSuccess };
                    Sandbox.InitializeKextSharedMemory(&memoryInfo);
                    if (memoryInfo.Error != Sandbox.KextSuccess)
                    {
                        throw new BuildXLException($"Unable to allocate shared memory region for worker (Code:{memoryInfo.Error})");
                    }

                    if (m_failureCallback != null && !Sandbox.SetFailureNotificationHandler(m_failureCallback))
                    {
                        throw new BuildXLException($"Unable to set sandbox kernel extension failure notification callback handler.");
                    }

                    m_sharedMemoryInfos.Add(memoryInfo);
                }

                m_sharedMemoryInfos.ForEach(memoryInfo => {
                    Thread worker = new Thread(() => StartReceivingAccessReports(memoryInfo.Address, memoryInfo.Port));
                    m_workerThreads.Add(worker);
                    worker.IsBackground = true;
                    worker.Priority = ThreadPriority.Highest;
                    worker.Start();
                });
            }
        }

        /// <summary>
        /// Disposes the sandbox kernel extension connection and release the resources in the interop layer, when running tests this can be skipped
        /// </summary>
        public void Dispose()
        {
            if (!m_skipDisposingForTests)
            {
                ReleaseResources();
            }
        }

        /// <summary>
        /// Releases all resources and cleans up the interop instance too
        /// </summary>
        public void ReleaseResources()
        {
            unsafe
            {
                m_sharedMemoryInfos.ForEach(memoryInfo => { Sandbox.DeinitializeKextSharedMemory(&memoryInfo); });
                m_sharedMemoryInfos.Clear();

                m_workerThreads.ForEach(thread => { thread.Join(); });

                Sandbox.DeinitializeKextConnection();
            }
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
                    throw new BuildXLException(message, ExceptionRootCause.MissingRuntimeDependency);
                }

                // The only way it can happen that no process is found for 'report.PipId' is when that pip is
                // explicitly terminated (e.g., because it timed out or Ctrl-c was pressed)
                if (m_pipProcesses.TryGetValue(report.PipId, out var process))
                {
                    // if the process is found, its ProcessId must match the RootPid of the report.
                    if (process.ProcessId != report.RootPid)
                    {
                        throw new BuildXLException($"Unexpected PID for Pip {report.PipId:X}: Expected {process.ProcessId}, Reported {report.RootPid}");
                    }

                    process.PostAccessReport(report);
                }
            };

            Sandbox.ListenForFileAccessReports(callback, address, port);
        }

        /// <inheritdoc />
        public bool NotifyKextPipStarted(FileAccessManifest fam, SandboxedProcessMacKext process)
        {
            Contract.Requires(process.Started);
            m_pipProcesses[fam.PipId] = process;

            var setup = new FileAccessSetup()
            {
                DllNameX64 = string.Empty,
                DllNameX86 = string.Empty,
                ReportPath = string.Empty
            };

            using (var wrapper = Pools.MemoryStreamPool.GetInstance())
            {
                var debugFlags = true;
                ArraySegment<byte> manifestBytes = fam.GetPayloadBytes(
                    setup,
                    wrapper.Instance,
                    timeoutMins: 10, // don't care because on Mac we don't kill the process from the Kext once it times out
                    debugFlagsMatch: ref debugFlags);

                var payloadHandle = GCHandle.Alloc(manifestBytes.Array, GCHandleType.Pinned);
                var result = Sandbox.SendPipStarted(
                    processId: process.ProcessId,
                    pipId: fam.PipId,
                    famBytes: IntPtr.Add(payloadHandle.AddrOfPinnedObject(), manifestBytes.Offset),
                    famBytesLength: manifestBytes.Count);

                if (payloadHandle.IsAllocated)
                {
                    payloadHandle.Free();
                }

                return result;
            }
        }

        /// <inheritdoc />
        public bool NotifyKextProcessFinished(long pipId, SandboxedProcessMacKext process)
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
