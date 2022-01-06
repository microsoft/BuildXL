// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Processes.Remoting;
using BuildXL.Utilities;

namespace ExternalToolTest.BuildXL.Scheduler
{
    /// <summary>
    /// Remote process for testing.
    /// </summary>
    internal class TestRemoteProcess : IRemoteProcess
    {
        private readonly StringBuilder m_output = new();
        private readonly StringBuilder m_error = new ();
        private readonly RemoteCommandExecutionInfo m_processInfo;
        private readonly CancellationToken m_cancellationToken;
        private readonly bool m_shouldRunLocally;

        private AsyncProcessExecutor m_processExecutor;
        private Process m_process;
        private Task<IRemoteProcessResult> m_completion;

        public Task<IRemoteProcessResult> Completion => m_completion;

        /// <summary>
        /// Creates an instance of <see cref="TestRemoteProcess"/>.
        /// </summary>
        public TestRemoteProcess(
            RemoteCommandExecutionInfo processInfo,
            CancellationToken cancellationToken,
            bool shouldRunLocally = false)
        {
            m_processInfo = processInfo;
            m_cancellationToken = cancellationToken;
            m_shouldRunLocally = shouldRunLocally;
        }

        /// <inheritdoc/>
        public void Dispose() => m_processExecutor?.Dispose();

        public Task StartAsync()
        {
            m_process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = m_processInfo.Command,
                    Arguments = m_processInfo.Args,
                    WorkingDirectory = m_processInfo.WorkingDirectory,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },

                EnableRaisingEvents = true
            };

            m_processExecutor = new AsyncProcessExecutor(
                m_process,
                TimeSpan.FromMilliseconds(-1), // Timeout should only be applied to the process that the external tool executes.
                line =>
                {
                    if (line != null)
                    {
                        m_output.AppendLine(line);
                    }
                },
                line =>
                {
                    if (line != null)
                    {
                        m_error.Append(line);
                    }
                },
                $"{nameof(TestRemoteProcess)}: {m_processInfo.Command} {m_processInfo.Args}");

            m_processExecutor.Start();
            m_completion = WaitForCompletionAsync();
            return Task.CompletedTask;
        }

        private async Task<IRemoteProcessResult> WaitForCompletionAsync()
        {
            await m_processExecutor.WaitForExitAsync();
            await m_processExecutor.WaitForStdOutAndStdErrAsync();

            return m_processExecutor.TimedOut || m_processExecutor.Killed || m_shouldRunLocally
                ? RemoteProcessResult.CreateForLocalRun()
                : RemoteProcessResult.CreateFromCompletedProcess(
                    m_process.ExitCode,
                    m_output.ToString(),
                    m_error.ToString(),
                    CommandExecutionDisposition.Remoted);
        }
    }

    internal sealed class RemoteProcessResult : IRemoteProcessResult
    {
        /// <inheritdoc/>
        public bool ShouldRunLocally { get; private set; }

        /// <inheritdoc/>
        public int? ExitCode { get; private set; }

        /// <inheritdoc/>
        public string StdOut { get; private set; }

        /// <inheritdoc/>
        public string StdErr { get; private set; }

        /// <inheritdoc/>
        public CommandExecutionDisposition Disposition { get; private set; }

        internal static RemoteProcessResult CreateForLocalRun() => new() { ShouldRunLocally = true };

        internal static RemoteProcessResult CreateFromCompletedProcess(int exitCode, string stdOut, string stdErr, CommandExecutionDisposition disposition) =>
            new() { ExitCode = exitCode, StdOut = stdOut, StdErr = stdErr, Disposition = disposition };
    }
}
