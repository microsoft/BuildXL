// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities.Core;

namespace BuildXL.Processes
{
    /// <summary>
    /// A <see cref="IDetoursEventListener"/> that invokes a callback when the root process start event is received.
    /// </summary>
    /// <remarks>
    /// This works on both Windows and Linux. The process start is detected via a <see cref="ReportedFileOperation.Process"/>
    /// </remarks>
    public sealed class ProcessStartEventListener : IDetoursEventListener
    {
        private readonly Action<int> m_callback;
        private int m_rootProcessNumber;

        /// <nodoc/>
        public ProcessStartEventListener(Action<int> callback, SandboxKind? sandboxConnectionKind)
        {
            Contract.Requires(callback != null);
            m_callback = callback;

            // We need FileAccessNotify so that HandleFileAccess is called,
            // and we keep the default collect flags so normal data collection is not disrupted.
            SetMessageHandlingFlags(
                MessageHandlingFlags.FileAccessNotify |
                MessageHandlingFlags.FileAccessCollect);

            // For EBPF-based monitoring, the first Process file access report is for the ebpf-runner process, and the second one is for the actual root process of the pip.
            // For other monitoring types, the first Process file access report is for the actual root process of the pip.
            m_rootProcessNumber = sandboxConnectionKind.HasValue && sandboxConnectionKind.Value == SandboxKind.LinuxEBPF
                ? 2
                : 1;
        }

        /// <inheritdoc />
        public override void HandleFileAccess(FileAccessData fileAccessData)
        {
            if (fileAccessData.Operation == ReportedFileOperation.Process
                && Interlocked.Decrement(ref m_rootProcessNumber) == 0)
            {
                m_callback((int)fileAccessData.ProcessId);
            }
        }

        /// <inheritdoc />
        public override void HandleDebugMessage(DebugData debugData)
        {
        }

        /// <inheritdoc />
        public override void HandleProcessData(ProcessData processData)
        {
        }

        /// <inheritdoc />
        public override void HandleProcessDetouringStatus(ProcessDetouringStatusData data)
        {
        }
    }
}
