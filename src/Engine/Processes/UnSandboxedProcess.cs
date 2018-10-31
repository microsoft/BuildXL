// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;
using BuildXL.Interop;
using BuildXL.Utilities;
using System.IO;
using static BuildXL.Utilities.FormattableStringEx;
using System.Linq;
using BuildXL.Native.IO;
#if FEATURE_SAFE_PROCESS_HANDLE
using Microsoft.Win32.SafeHandles;
#else
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
#endif

namespace BuildXL.Processes
{
    /// <summary>
    /// An implementation of <see cref="ISandboxedProcess"/> that doesn't perform any sandboxing.
    /// </summary>
    public class UnSandboxedProcess : ISandboxedProcess
    {
        private static readonly ISet<ReportedFileAccess> s_emptyFileAccessesSet = new HashSet<ReportedFileAccess>();

        private readonly TaskSourceSlim<Unit> m_processExitedTcs = TaskSourceSlim.Create<Unit>();
        private readonly TaskSourceSlim<Unit> m_stdoutFlushedTcs = TaskSourceSlim.Create<Unit>();
        private readonly TaskSourceSlim<Unit> m_stderrFlushedTcs = TaskSourceSlim.Create<Unit>();
        private readonly SandboxedProcessOutputBuilder m_output;
        private readonly SandboxedProcessOutputBuilder m_error;

        private DateTime m_startTime;
        private Exception m_dumpCreationException;
        private bool m_killed = false;

        /// <summary>
        /// Indicates if the process has been force killed during execution.
        /// </summary>
        protected virtual bool Killed => m_killed;

        /// <summary>
        /// Process information associated with this process.
        /// </summary>
        protected SandboxedProcessInfo ProcessInfo { get; }

        /// <summary>
        /// Underlying managed <see cref="Process"/> object.
        /// </summary>
        protected Process Process { get; private set; }

        /// <summary>
        /// Task that completes once this process dies.
        /// </summary>
        protected Task Completion { get; }

        /// <summary>
        /// Returns the path table from the supplied <see cref="SandboxedProcessInfo"/>.
        /// </summary>
        protected PathTable PathTable => ProcessInfo.PathTable;

        /// <summary>
        /// Whether there were any failures regarding sandboxing (e.g., sandbox
        /// couldn't be created, some file access reports were dropped, etc.)
        /// </summary>
        protected virtual bool HasSandboxFailures => false;

        /// <nodoc />
        public UnSandboxedProcess(SandboxedProcessInfo info)
        {
            Contract.Requires(info != null);

            info.Timeout = info.Timeout ?? TimeSpan.FromMinutes(10);

            ProcessInfo = info;

            m_output = new SandboxedProcessOutputBuilder(
                ProcessInfo.StandardOutputEncoding ?? Console.OutputEncoding,
                ProcessInfo.MaxLengthInMemory,
                ProcessInfo.FileStorage,
                SandboxedProcessFile.StandardOutput,
                ProcessInfo.StandardOutputObserver);

            m_error = new SandboxedProcessOutputBuilder(
                ProcessInfo.StandardErrorEncoding ?? Console.OutputEncoding,
                ProcessInfo.MaxLengthInMemory,
                ProcessInfo.FileStorage,
                SandboxedProcessFile.StandardError,
                ProcessInfo.StandardErrorObserver);

            Completion = Task.WhenAll(m_processExitedTcs.Task, m_stdoutFlushedTcs.Task, m_stderrFlushedTcs.Task);
        }

        /// <summary>
        /// Whether this process has been started (i.e., the <see cref="Start"/> method has been called on it).
        /// </summary>
        public bool Started => Process != null;

        /// <inheritdoc />
        public virtual void Start()
        {
            Contract.Requires(!Started, "Process was already started.  Cannot start process more than once.");

            CreateAndSetUpProcess();
            Process.Start();
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
            SetProcessStartedExecuting();
        }

        private int m_processId = -1;

        /// <inheritdoc />
        public int ProcessId => m_processId != -1 ? m_processId : (m_processId = Process.Id);

        /// <inheritdoc />
        public virtual void Dispose()
        {
            Process?.Dispose();
        }

        /// <inheritdoc />
        public string GetAccessedFileName(ReportedFileAccess reportedFileAccess) => null;

        /// <inheritdoc />
        public ulong? GetActivePeakMemoryUsage()
        {
            try
            {
                if (Process == null || Process.HasExited)
                {
                    return null;
                }

                return Dispatch.GetActivePeakMemoryUsage(Process.Handle, ProcessId);
            }
            catch(Exception)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public long GetDetoursMaxHeapSize() => 0;

        /// <inheritdoc />
        public int GetLastMessageCount() => 0;

        /// <inheritdoc />
        public virtual async Task<SandboxedProcessResult> GetResultAsync()
        {
            Contract.Requires(Started);

            var finishedTask = await Task.WhenAny(Task.Delay(ProcessInfo.Timeout.Value), Completion);
            var timedOut = finishedTask != Completion;
            if (timedOut)
            {
                LogDebugProcessState("Process timed out");
                await KillAsync();
            }

            var reports = await GetReportsAsync();
            reports?.Freeze();

            var reportFileAccesses = ProcessInfo.FileAccessManifest?.ReportFileAccesses == true;
            var fileAccesses = reportFileAccesses ? (reports?.FileAccesses ?? s_emptyFileAccessesSet) : null;

            return new SandboxedProcessResult
            {
                ExitCode                            = timedOut ? ExitCodes.Timeout : Process.ExitCode,
                Killed                              = Killed,
                TimedOut                            = timedOut,
                HasDetoursInjectionFailures         = HasSandboxFailures,
                StandardOutput                      = m_output.Freeze(),
                StandardError                       = m_error.Freeze(),
                HasReadWriteToReadFileAccessRequest = reports?.HasReadWriteToReadFileAccessRequest ?? false,
                AllUnexpectedFileAccesses           = reports?.FileUnexpectedAccesses ?? s_emptyFileAccessesSet,
                FileAccesses                        = fileAccesses,
                DetouringStatuses                   = reports?.ProcessDetoursStatuses,
                ExplicitlyReportedFileAccesses      = reports?.ExplicitlyReportedFileAccesses,
                Processes                           = CoalesceProcesses(reports?.Processes),
                MessageProcessingFailure            = reports?.MessageProcessingFailure,
                DumpCreationException               = m_dumpCreationException,
                DumpFileDirectory                   = ProcessInfo.TimeoutDumpDirectory,
                PrimaryProcessTimes                 = GetProcessTimes(),
                SurvivingChildProcesses             = CoalesceProcesses(GetSurvivingChildProcesses())
            };
        }

        /// <summary>
        /// For each PID chooses the last reported process.
        /// </summary>
        protected IReadOnlyList<ReportedProcess> CoalesceProcesses(IEnumerable<ReportedProcess> processes)
        {
            return processes?
                .GroupBy(p => p.ProcessId)
                .Select(grp => grp.Last())
                .ToList();
        }

        /// <inheritdoc />
        public virtual Task KillAsync()
        {
            Contract.Requires(Started);

            ProcessDumper.TryDumpProcessAndChildren(ProcessId, ProcessInfo.TimeoutDumpDirectory, out m_dumpCreationException);

            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill();
                }

                m_stdoutFlushedTcs.TrySetResult(Unit.Void);
                m_stderrFlushedTcs.TrySetResult(Unit.Void);
            }
            catch (Win32Exception)
            {
                // thrown if the process doesn't exist (e.g., because it has already completed on its own)
            }

            m_killed = true;
            return Completion;
        }

        /// <summary>
        /// Mutates <see cref="Process"/>.
        /// </summary>
        protected Process CreateAndSetUpProcess()
        {
            Contract.Requires(Process == null);

            if (!FileUtilities.FileExistsNoFollow(ProcessInfo.FileName))
            {
                ThrowCouldNotStartProcess(I($"File '{ProcessInfo.FileName}' not found"), new Win32Exception(0x2));
            }

            Process = new Process();
            Process.StartInfo = new ProcessStartInfo
            {
                FileName = ProcessInfo.FileName,
                Arguments = ProcessInfo.Arguments,
                WorkingDirectory = ProcessInfo.WorkingDirectory,
                StandardErrorEncoding = m_output.Encoding,
                StandardOutputEncoding = m_error.Encoding,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.StartInfo.EnvironmentVariables.Clear();
            if (ProcessInfo.EnvironmentVariables != null)
            {
                foreach (var envKvp in ProcessInfo.EnvironmentVariables.ToDictionary())
                {
                    Process.StartInfo.EnvironmentVariables[envKvp.Key] = envKvp.Value;
                }
            }

            Process.EnableRaisingEvents = true;
            Process.OutputDataReceived += (sender, e) => FeedOutputBuilder(m_output, m_stdoutFlushedTcs, e.Data);
            Process.ErrorDataReceived  += (sender, e) => FeedOutputBuilder(m_error, m_stderrFlushedTcs, e.Data);
            Process.Exited             += (sender, e) => m_processExitedTcs.TrySetResult(Unit.Void);

            return Process;
        }

        /// <summary>
        /// Throws a <see cref="BuildXLException"/> with pip hash and description prefixed to a supplied <paramref name="reason"/>.
        /// </summary>
        /// <param name="reason">Additional explanation of why process creation failed</param>
        /// <param name="inner">Optional inner exception that caused the process creation to fail</param>
        protected void ThrowCouldNotStartProcess(string reason, Exception inner = null)
        {
            ThrowBuildXLException($"Process creation failed: {reason}", inner);
        }

        /// <summary>
        /// Throws a <see cref="BuildXLException"/> with pip hash and description prefixed to a supplied <paramref name="message"/>.
        /// </summary>
        /// <param name="message">Explanation of why process failed</param>
        /// <param name="inner">Optional inner exception that caused the failure</param>
        protected void ThrowBuildXLException(string message, Exception inner = null)
        {
            Process?.StandardInput?.Close();
            throw new BuildXLException($"[Pip{ProcessInfo.PipSemiStableHash} -- {ProcessInfo.PipDescription}] {message}", inner);
        }

        /// <nodoc />
        protected virtual bool ReportsCompleted() => true;

        /// <nodoc />
        protected void LogDebugProcessState(string message)
        {
            if (ProcessInfo.LoggingContext != null)
            {
                string fullMessage = I($"Exited: {m_processExitedTcs.Task.IsCompleted}, StdOut: {m_stdoutFlushedTcs.Task.IsCompleted}, StdErr: {m_stderrFlushedTcs.Task.IsCompleted}, Reports: {ReportsCompleted()} :: {message}");
                Tracing.Logger.Log.LogDetoursDebugMessage(ProcessInfo.LoggingContext, ProcessInfo.PipSemiStableHash, ProcessInfo.PipDescription, fullMessage);
            }
        }

        /// <summary>
        ///   - sets <see cref="m_startTime"/> to current time
        ///   - notifies the process id listener
        /// </summary>
        /// <remarks>
        /// Mutates <see cref="m_startTime"/>.
        /// </remarks>
        protected void SetProcessStartedExecuting()
        {
            m_startTime = DateTime.UtcNow;
            ProcessInfo.ProcessIdListener?.Invoke(ProcessId);
        }

        /// <summary>
        /// Returns surviving child processes in the case when the pip had to be terminated because its child
        /// processes didn't exit within allotted time (<see cref="SandboxedProcessInfo.NestedProcessTerminationTimeout"/>)
        /// after the main pip process has already exited.
        /// </summary>
        protected virtual IEnumerable<ReportedProcess> GetSurvivingChildProcesses() => null;

        /// <summary>
        /// Returns any collected sandboxed process reports or null.
        /// </summary>
        internal virtual Task<SandboxedProcessReports> GetReportsAsync() => Task.FromResult<SandboxedProcessReports>(null);

        private static void FeedOutputBuilder(SandboxedProcessOutputBuilder output, TaskSourceSlim<Unit> signalCompletion, string line)
        {
            output.AppendLine(line);
            if (line == null)
            {
                signalCompletion.TrySetResult(Unit.Void);
            }
        }

        private ProcessTimes GetProcessTimes()
        {
            Dispatch.GetProcessTimes(Process.Handle, ProcessId, out long creation, out long exit, out long kernel, out long user);
            return new ProcessTimes(creation: creation, exit: exit, kernel: kernel, user: user);
        }
    }
}
