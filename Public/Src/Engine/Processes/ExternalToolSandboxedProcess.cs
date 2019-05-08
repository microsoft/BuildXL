// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Sanboxed process that will be executed by an external tool.
    /// </summary>
    public class ExternalToolSandboxedProcess : ExternalSandboxedProcess
    {
        private readonly ExternalToolSandboxedProcessExecutor m_tool;

        private readonly StringBuilder m_output = new StringBuilder();
        private readonly StringBuilder m_error = new StringBuilder();

        private AsyncProcessExecutor m_processExecutor;

        /// <summary>
        /// Creates an instance of <see cref="ExternalToolSandboxedProcess"/>.
        /// </summary>
        public ExternalToolSandboxedProcess(SandboxedProcessInfo sandboxedProcessInfo, ExternalToolSandboxedProcessExecutor tool)
            : base(sandboxedProcessInfo)
        {
            Contract.Requires(tool != null);
            m_tool = tool;
        }

        /// <inheritdoc />
        public override int ProcessId => Process?.Id ?? -1;

        /// <summary>
        /// Underlying managed <see cref="Process"/> object.
        /// </summary>
        public Process Process => m_processExecutor?.Process;

        /// <inheritdoc />
        public override string StdOut => m_processExecutor?.StdOutCompleted ?? false ? m_output.ToString() : string.Empty;

        /// <inheritdoc />
        public override string StdErr => m_processExecutor?.StdErrCompleted ?? false ? m_error.ToString() : string.Empty;

        /// <inheritdoc />
        public override int? ExitCode => m_processExecutor.ExitCompleted ? Process?.ExitCode : default;

        /// <inheritdoc />
        public override void Dispose()
        {
            m_processExecutor?.Dispose();
        }

        /// <inheritdoc />
        public override ulong? GetActivePeakMemoryUsage() => m_processExecutor?.GetActivePeakMemoryUsage();

        /// <inheritdoc />
        public override async Task<SandboxedProcessResult> GetResultAsync()
        {
            Contract.Requires(m_processExecutor != null);

            await m_processExecutor.WaitForExitAsync();
            await m_processExecutor.WaitForStdOutAndStdErrAsync();

            if (m_processExecutor.TimedOut || m_processExecutor.Killed)
            {
                // If timed out/killed, then sandboxed process result may have not been deserialized yet.
                return CreateResultForFailure();
            }

            if (Process.ExitCode != 0)
            {
                return CreateResultForFailure();
            }

            return DeserializeSandboxedProcessResultFromFile();
        }

        /// <inheritdoc />
        public override Task KillAsync() => KillProcessExecutorAsync(m_processExecutor);

        /// <inheritdoc />
        public override void Start()
        {
            SerializeSandboxedProcessInfoToFile();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = m_tool.ExecutablePath,
                    Arguments = m_tool.CreateArguments(GetSandboxedProcessInfoFile(), GetSandboxedProcessResultsFile()),
                    WorkingDirectory = SandboxedProcessInfo.WorkingDirectory,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },

                EnableRaisingEvents = true
            };

            m_processExecutor = new AsyncProcessExecutor(
                process,
                TimeSpan.FromMilliseconds(-1), // Timeout should only be applied to the process that the external tool executes.
                line => AppendLineIfNotNull(m_output, line),
                line => AppendLineIfNotNull(m_error, line),
                SandboxedProcessInfo.Provenance,
                message => LogExternalExecution(message));

            m_processExecutor.Start();
        }

        private SandboxedProcessResult CreateResultForFailure()
        {
            string output = m_output.ToString();
            string error = m_error.ToString();
            string hint = Path.GetFileNameWithoutExtension(m_tool.ExecutablePath);

            return CreateResultForFailure(
                exitCode: m_processExecutor.TimedOut ? ExitCodes.Timeout : Process.ExitCode,
                killed: m_processExecutor.Killed,
                timedOut: m_processExecutor.TimedOut,
                output: output,
                error: error,
                hint: hint);
        }
    }
}
