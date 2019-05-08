// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.VmCommandProxy;

namespace BuildXL.Processes
{
    /// <summary>
    /// Sandboxed process that will be executed in VM.
    /// </summary>
    public class ExternalVmSandboxedProcess : ExternalSandboxedProcess
    {
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

        private readonly StringBuilder m_output = new StringBuilder();
        private readonly StringBuilder m_error = new StringBuilder();

        private AsyncProcessExecutor m_processExecutor;

        private readonly ExternalToolSandboxedProcessExecutor m_tool;
        private readonly VmInitializer m_vmInitializer;

        /// <summary>
        /// Creates an instance of <see cref="ExternalVmSandboxedProcess"/>.
        /// </summary>
        public ExternalVmSandboxedProcess(
            SandboxedProcessInfo sandboxedProcessInfo, 
            VmInitializer vmInitializer, 
            ExternalToolSandboxedProcessExecutor tool)
            : base(sandboxedProcessInfo)
        {
            Contract.Requires(vmInitializer != null);
            Contract.Requires(tool != null);

            m_vmInitializer = vmInitializer;
            m_tool = tool;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            m_processExecutor?.Dispose();
        }

        /// <inheritdoc />
        public override ulong? GetActivePeakMemoryUsage() => m_processExecutor?.GetActivePeakMemoryUsage();

        /// <inheritdoc />
        [SuppressMessage("AsyncUsage", "AsyncFixer02:MissingAsyncOpportunity")]
        public override async Task<SandboxedProcessResult> GetResultAsync()
        {
            Contract.Requires(m_processExecutor != null);

            // (1) Wait for VmCommandProxy.
            await m_processExecutor.WaitForExitAsync();
            await m_processExecutor.WaitForStdOutAndStdErrAsync();

            // (2) Validate result of VmCommandProxy.
            if (m_processExecutor.TimedOut || m_processExecutor.Killed)
            {
                // If timed out/killed, then sandboxed process result may have not been deserialized yet.
                return CreateResultForVmCommandProxyFailure();
            }

            if (Process.ExitCode != 0)
            {
                return CreateResultForVmCommandProxyFailure();
            }

            if (!FileUtilities.FileExistsNoFollow(RunOutputPath))
            {
                m_error.AppendLine($"Could not find VM output file '{RunOutputPath}");
                return CreateResultForVmCommandProxyFailure();
            }

            // (3) Validate the result of sandboxed process executor run by VmCommandProxy.
            RunResult runVmResult = ExceptionUtilities.HandleRecoverableIOException(
                () => VmSerializer.DeserializeFromFile<RunResult>(RunOutputPath),
                e => m_error.AppendLine(e.Message));

            if (runVmResult == null)
            {
                return CreateResultForVmCommandProxyFailure();
            }

            if (runVmResult.ProcessStateInfo.ExitCode != 0)
            {
                return CreateResultForSandboxExecutorFailure(runVmResult);
            }

            return DeserializeSandboxedProcessResultFromFile();
        }

        /// <inheritdoc />
        public override Task KillAsync() => KillProcessExecutorAsync(m_processExecutor);

        /// <inheritdoc />
        public override void Start()
        {
            RunInVm();
        }

        private void RunInVm()
        {
            // (1) Serialize sandboxed prosess info.
            SerializeSandboxedProcessInfoToFile();

            // (2) Create and serialize run request.
            var runRequest = new RunRequest
            {
                AbsolutePath = m_tool.ExecutablePath,
                Arguments = m_tool.CreateArguments(GetSandboxedProcessInfoFile(), GetSandboxedProcessResultsFile()),
                WorkingDirectory = SandboxedProcessInfo.WorkingDirectory
            };

            VmSerializer.SerializeToFile(RunRequestPath, runRequest);

            // (2) Create a process to execute VmCommandProxy.
            string arguments = $"{VmCommand.Run} /{VmCommand.Param.InputJsonFile}:\"{RunRequestPath}\" /{VmCommand.Param.OutputJsonFile}:\"{RunOutputPath}\"";
            var process = CreateVmCommandProxyProcess(arguments);

            LogExternalExecution($"call {m_vmInitializer.VmCommandProxy} {arguments}");

            m_processExecutor = new AsyncProcessExecutor(
                process,
                TimeSpan.FromMilliseconds(-1), // Timeout should only be applied to the process that the external tool executes.
                line => AppendLineIfNotNull(m_output, line),
                line => AppendLineIfNotNull(m_error, line),
                SandboxedProcessInfo.Provenance,
                message => LogExternalExecution(message));

            m_processExecutor.Start();
        }

        private string RunRequestPath => GetVmCommandProxyPath("VmRunInput");

        private string RunOutputPath => GetVmCommandProxyPath("VmRunOutput");

        private string GetVmCommandProxyPath(string command) => Path.Combine(GetOutputDirectory(), $"{command}-Pip{SandboxedProcessInfo.PipSemiStableHash:X16}.json");

        private Process CreateVmCommandProxyProcess(string arguments)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = m_vmInitializer.VmCommandProxy,
                    Arguments = arguments,
                    WorkingDirectory = SandboxedProcessInfo.WorkingDirectory,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },

                EnableRaisingEvents = true
            };
        }

        private SandboxedProcessResult CreateResultForVmCommandProxyFailure()
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

        private SandboxedProcessResult CreateResultForSandboxExecutorFailure(RunResult runVmResult)
        {
            Contract.Requires(runVmResult != null);

            return CreateResultForFailure(
                exitCode: runVmResult.ProcessStateInfo.ExitCode,
                killed: false,
                timedOut: false,
                output: runVmResult.StdOut ?? string.Empty,
                error: runVmResult.StdErr ?? string.Empty,
                hint: Path.GetFileNameWithoutExtension(m_tool.ExecutablePath));
        }
    }
}
