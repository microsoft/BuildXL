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
    internal class TestRemoteProcess : IRemoteProcessPip
    {
        private readonly StringBuilder m_output = new();
        private readonly StringBuilder m_error = new ();
        private readonly RemoteProcessInfo m_processInfo;
        private readonly CancellationToken m_cancellationToken;
        private readonly bool m_shouldRunLocally;

        private AsyncProcessExecutor m_processExecutor;
        private Process m_process;
        private Task<IRemoteProcessPipResult> m_completion;

        public Task<IRemoteProcessPipResult> Completion => m_completion;

        /// <summary>
        /// Creates an instance of <see cref="TestRemoteProcess"/>.
        /// </summary>
        public TestRemoteProcess(
            RemoteProcessInfo processInfo,
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
                    FileName = m_processInfo.Executable,
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
                $"{nameof(TestRemoteProcess)}: {m_processInfo.Executable} {m_processInfo.Args}");

            m_processExecutor.Start();
            m_completion = WaitForCompletionAsync();
            return Task.CompletedTask;
        }

        private async Task<IRemoteProcessPipResult> WaitForCompletionAsync()
        {
            await m_processExecutor.WaitForExitAsync();
            await m_processExecutor.WaitForStdOutAndStdErrAsync();

            return m_processExecutor.TimedOut || m_processExecutor.Killed || m_shouldRunLocally
                ? RemoteProcessResult.CreateForLocalRun()
                : RemoteProcessResult.CreateFromCompletedProcess(
                    m_process.ExitCode,
                    m_output.ToString(),
                    m_error.ToString(),
                    RemoteResultDisposition.Remoted);
        }
    }

    internal sealed class RemoteProcessResult : IRemoteProcessPipResult
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
        public RemoteResultDisposition Disposition { get; private set; }

        internal static RemoteProcessResult CreateForLocalRun() => new() { ShouldRunLocally = true };

        internal static RemoteProcessResult CreateFromCompletedProcess(int exitCode, string stdOut, string stdErr, RemoteResultDisposition disposition) =>
            new() { ExitCode = exitCode, StdOut = stdOut, StdErr = stdErr, Disposition = disposition };
    }
}
