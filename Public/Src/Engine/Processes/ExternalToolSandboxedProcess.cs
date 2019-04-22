// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace BuildXL.Processes
{
    /// <summary>
    /// Sanboxed process that will be executed by an external tool.
    /// </summary>
    public class ExternalToolSandboxedProcess : ExternalSandboxedProcess
    {
        private static readonly ISet<ReportedFileAccess> s_emptyFileAccessesSet = new HashSet<ReportedFileAccess>();

        /// <summary>
        /// Relative path to the default tool.
        /// </summary>
        public const string DefaultToolRelativePath = @"tools\SandboxedProcessExecutor\SandboxedProcessExecutor.exe";

        private readonly string m_toolPath;

        private readonly StringBuilder m_output = new StringBuilder();
        private readonly StringBuilder m_error = new StringBuilder();

        private AsyncProcessExecutor m_processExecutor;
        private Exception m_dumpCreationException;

        /// <summary>
        /// Creates an instance of <see cref="ExternalToolSandboxedProcess"/>.
        /// </summary>
        public ExternalToolSandboxedProcess(SandboxedProcessInfo sandboxedProcessInfo, string toolPath)
            : base(sandboxedProcessInfo)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(toolPath));
            m_toolPath = toolPath;
        }

        private int m_processId = -1;

        /// <inheritdoc />
        public override int ProcessId => m_processId != -1 ? m_processId : (m_processId = Process?.Id ?? -1);

        /// <summary>
        /// Underlying managed <see cref="Process"/> object.
        /// </summary>
        public Process Process => m_processExecutor?.Process;

        /// <inheritdoc />
        public override string StdOut => m_processExecutor?.StdOutCompleted ?? false ? m_error.ToString() : string.Empty;

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
        public override string GetAccessedFileName(ReportedFileAccess reportedFileAccess) => null;

        /// <inheritdoc />
        public override ulong? GetActivePeakMemoryUsage() => m_processExecutor?.GetActivePeakMemoryUsage();

        /// <inheritdoc />
        public override long GetDetoursMaxHeapSize() => 0;

        /// <inheritdoc />
        public override int GetLastMessageCount() => 0;

        /// <inheritdoc />
        public override async Task<SandboxedProcessResult> GetResultAsync()
        {
            Contract.Requires(m_processExecutor != null);

            await m_processExecutor.WaitForExitAsync();

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
        public override Task KillAsync()
        {
            Contract.Requires(m_processExecutor != null);

            ProcessDumper.TryDumpProcessAndChildren(ProcessId, GetOutputDirectory(), out m_dumpCreationException);

            return m_processExecutor.KillAsync();
        }

        /// <inheritdoc />
        public override void Start()
        {
            Setup();
            m_processExecutor.Start();
        }

        private string CreateArguments() => $"/sandboxedProcessInfo:{GetSandboxedProcessInfoFile()} /sandboxedProcessResult:{GetSandboxedProcessResultsFile()}";

        private void Setup()
        {
            SerializeSandboxedProcessInfoToFile();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = m_toolPath,
                    Arguments = CreateArguments(),
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
                line => m_output.AppendLine(line),
                line => m_error.AppendLine(line),
                SandboxedProcessInfo);
        }

        /// <summary>
        /// Starts process asynchronously.
        /// </summary>
        public static Task<ISandboxedProcess> StartAsync(SandboxedProcessInfo info, string toolPath)
        {
            return Task.Factory.StartNew(() =>
            {
                ISandboxedProcess process = new ExternalToolSandboxedProcess(info, toolPath);

                try
                {
                    process.Start();
                }
                catch
                {
                    process?.Dispose();
                    throw;
                }

                return process;
            });
        }

        private SandboxedProcessResult CreateResultForFailure()
        {
            string output = m_output.ToString();
            string error = m_error.ToString();
            string hint = Path.GetFileNameWithoutExtension(m_toolPath);
            var standardFiles = new SandboxedProcessStandardFiles(GetStdOutPath(hint), GetStdErrPath(hint));
            var storage = new StandardFileStorage(standardFiles);

            return new SandboxedProcessResult
            {
                ExitCode = m_processExecutor.TimedOut ? ExitCodes.Timeout : Process.ExitCode,
                Killed = m_processExecutor.Killed,
                TimedOut = m_processExecutor.TimedOut,
                HasDetoursInjectionFailures = false,
                StandardOutput = new SandboxedProcessOutput(output.Length, output, null, Console.OutputEncoding, storage, SandboxedProcessFile.StandardOutput, null),
                StandardError = new SandboxedProcessOutput(error.Length, error, null, Console.OutputEncoding, storage, SandboxedProcessFile.StandardError, null),
                HasReadWriteToReadFileAccessRequest = false,
                AllUnexpectedFileAccesses = s_emptyFileAccessesSet,
                FileAccesses = s_emptyFileAccessesSet,
                DetouringStatuses = new ProcessDetouringStatusData[0],
                ExplicitlyReportedFileAccesses = s_emptyFileAccessesSet,
                Processes = new ReportedProcess[0],
                MessageProcessingFailure = null,
                DumpCreationException = m_dumpCreationException,
                DumpFileDirectory = GetOutputDirectory(),
                PrimaryProcessTimes = new ProcessTimes(0, 0, 0, 0),
                SurvivingChildProcesses = new ReportedProcess[0],
            };
        }
    }
}
