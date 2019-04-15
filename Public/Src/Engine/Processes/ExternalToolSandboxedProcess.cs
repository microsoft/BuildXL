// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Native.IO;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Processes
{
    /// <summary>
    /// Sanboxed process that will be executed by an external tool.
    /// </summary>
    public class ExternalToolSandboxedProcess : ExternalSandboxedProcess
    {
        /// <summary>
        /// Relative path to the default tool.
        /// </summary>
        public const string DefaultToolRelativePath = @"tools\SandboxedProcessExecutor\SandboxedProcessExecutor.exe";

        private readonly string m_toolPath;

        private static readonly ISet<ReportedFileAccess> s_emptyFileAccessesSet = new HashSet<ReportedFileAccess>();

        private readonly TaskSourceSlim<Unit> m_processExitedTcs = TaskSourceSlim.Create<Unit>();
        private readonly TaskSourceSlim<Unit> m_stdoutFlushedTcs = TaskSourceSlim.Create<Unit>();
        private readonly TaskSourceSlim<Unit> m_stderrFlushedTcs = TaskSourceSlim.Create<Unit>();
        private readonly StringBuilder m_output = new StringBuilder();
        private readonly StringBuilder m_error = new StringBuilder();

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
        public override int ProcessId => m_processId != -1 ? m_processId : (m_processId = Process.Id);

        /// <summary>
        /// Underlying managed <see cref="Process"/> object.
        /// </summary>
        public Process Process { get; private set; }

        /// <summary>
        /// Task that completes once this process dies.
        /// </summary>
        private Task WhenExited => m_processExitedTcs.Task;

        /// <inheritdoc />
        public override void Dispose()
        {
            Process?.Dispose();
        }

        /// <inheritdoc />
        public override string GetAccessedFileName(ReportedFileAccess reportedFileAccess) => null;

        /// <inheritdoc />
        public override ulong? GetActivePeakMemoryUsage()
        {
            try
            {
                if (Process == null || Process.HasExited)
                {
                    return null;
                }

                return Dispatch.GetActivePeakMemoryUsage(Process.Handle, ProcessId);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                return null;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        /// <inheritdoc />
        public override long GetDetoursMaxHeapSize() => 0;

        /// <inheritdoc />
        public override int GetLastMessageCount() => 0;

        /// <inheritdoc />
        public override async Task<SandboxedProcessResult> GetResultAsync()
        {
            Contract.Requires(Process != null);

            int timeout = SandboxedProcessInfo.Timeout.HasValue
                ? (int)SandboxedProcessInfo.Timeout.Value.TotalMilliseconds
                : int.MaxValue;

            var finishedTask = await Task.WhenAny(Task.Delay(timeout), WhenExited);

            var timedOut = finishedTask != WhenExited;
            if (timedOut)
            {
                await KillAsync();
            }

            await Task.WhenAll(m_stdoutFlushedTcs.Task, m_stderrFlushedTcs.Task);

            if (Process.ExitCode != 0)
            {
                string[] message = new[] { "StdOut:", m_output.ToString(), "StdErr:", m_error.ToString() };
                ThrowBuildXLException($"Process execution of '{m_toolPath}' failed with exit code {Process.ExitCode}:{Environment.NewLine}{string.Join(Environment.NewLine, message)}");
            }

            return DeserializeSandboxedProcessResultFromFile();
        }

        /// <inheritdoc />
        public override Task KillAsync()
        {
            Contract.Requires(Process != null);

            ProcessDumper.TryDumpProcessAndChildren(ProcessId, SandboxedProcessInfo.TimeoutDumpDirectory, out m_dumpCreationException);

            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill();
                }
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
            {
                // thrown if the process doesn't exist (e.g., because it has already completed on its own)
            }

            m_stdoutFlushedTcs.TrySetResult(Unit.Void);
            m_stderrFlushedTcs.TrySetResult(Unit.Void);

            return WhenExited;
        }

        /// <inheritdoc />
        public override void Start()
        {
            Setup();

            try
            {
                Process.Start();
            }
            catch (Win32Exception e)
            {
                ThrowBuildXLException($"Failed to launch process '{m_toolPath}'", e);
            }

            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
        }

        private string CreateArguments() => $"/sandboxedProcessInfo:{GetSandboxedProcessInfoFile()} /sandboxedProcessResult:{GetSandboxedProcessResultsFile()}";

        private Process Setup()
        {
            SerializeSandboxedProcessInfoToFile();
            if (!FileUtilities.FileExistsNoFollow(m_toolPath))
            {
                ThrowBuildXLException($"Process creation failed: File '{m_toolPath}' not found");
            }

            Process = new Process();
            Process.StartInfo = new ProcessStartInfo
            {
                FileName = m_toolPath,
                Arguments = CreateArguments(),
                WorkingDirectory = SandboxedProcessInfo.WorkingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.EnableRaisingEvents = true;
            Process.OutputDataReceived += (sender, e) => FeedOutputBuilder(m_output, m_stdoutFlushedTcs, e.Data);
            Process.ErrorDataReceived += (sender, e) => FeedOutputBuilder(m_error, m_stderrFlushedTcs, e.Data);
            Process.Exited += (sender, e) => m_processExitedTcs.TrySetResult(Unit.Void);

            return Process;
        }

        private static void FeedOutputBuilder(StringBuilder output, TaskSourceSlim<Unit> signalCompletion, string line)
        {
            if (signalCompletion.Task.IsCompleted)
            {
                return;
            }

            if (line == null)
            {
                signalCompletion.TrySetResult(Unit.Void);
            }
            else
            {
                output.AppendLine(line);
            }
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
    }
}
