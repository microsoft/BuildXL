// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildXL.Processes
{
    /// <summary>
    /// Sandboxed process that will be executed in VM.
    /// </summary>
    public class ExternalVMSandboxedProcess : ExternalSandboxedProcess
    {
        public override int ProcessId => throw new NotImplementedException();

        public override string StdOut => throw new NotImplementedException();

        public override string StdErr => throw new NotImplementedException();

        public override int? ExitCode => throw new NotImplementedException();

        private readonly StringBuilder m_output = new StringBuilder();
        private readonly StringBuilder m_error = new StringBuilder();

        private AsyncProcessExecutor m_processExecutor;

        private readonly ExternalToolSandboxedProcessExecutor m_tool;
        private readonly string m_vmCommandProxy;

        private readonly string m_userName;

        /// <summary>
        /// Creates an instance of <see cref="ExternalVMSandboxedProcess"/>.
        /// </summary>
        public ExternalVMSandboxedProcess(
            SandboxedProcessInfo sandboxedProcessInfo, 
            string vmCommandProxy, 
            ExternalToolSandboxedProcessExecutor tool)
            : base(sandboxedProcessInfo)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(vmCommandProxy));
            Contract.Requires(tool != null);

            m_vmCommandProxy = vmCommandProxy;
            m_tool = tool;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            m_processExecutor?.Dispose();
        }

        public override string GetAccessedFileName(ReportedFileAccess reportedFileAccess)
        {
            throw new NotImplementedException();
        }

        public override ulong? GetActivePeakMemoryUsage()
        {
            throw new NotImplementedException();
        }

        public override long GetDetoursMaxHeapSize()
        {
            throw new NotImplementedException();
        }

        public override int GetLastMessageCount()
        {
            throw new NotImplementedException();
        }

        public override Task<SandboxedProcessResult> GetResultAsync()
        {
            throw new NotImplementedException();
        }

        public override Task KillAsync()
        {
            throw new NotImplementedException();
        }

        public override void Start()
        {
            throw new NotImplementedException();
        }

        private void InitVm
    }
}
