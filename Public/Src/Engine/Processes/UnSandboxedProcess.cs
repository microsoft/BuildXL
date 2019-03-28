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
using static BuildXL.Interop.MacOS.IO;
using JetBrains.Annotations;
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
        private DateTime m_exitTime;
        private DateTime m_reportsReceivedTime;

        private Exception m_dumpCreationException;
        private bool m_killed = false;

        internal class CpuTimes
        {
            internal static readonly CpuTimes Zeros = new CpuTimes(new TimeSpan(0), new TimeSpan(0));

            internal TimeSpan User { get; }
            internal TimeSpan System { get; }

            internal CpuTimes(TimeSpan user, TimeSpan system)
            {
                User = user;
                System = system;
            }
        }

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
        protected Task WhenExited => m_processExitedTcs.Task;

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
        }

        /// <summary>
        /// Whether this process has been started (i.e., the <see cref="Start"/> method has been called on it).
        /// </summary>
        public bool Started => Process != null;

        /// <summary>
        /// Difference between now and when the process was started.
        /// </summary>
        protected TimeSpan CurrentRunningTime()
        {
            Contract.Requires(Started);
            return DateTime.UtcNow.Subtract(m_startTime);
        }

        /// <inheritdoc />
        public virtual void Start()
        {
            Contract.Requires(!Started, "Process was already started.  Cannot start process more than once.");

            CreateAndSetUpProcess();

            try
            {
                Process.Start();
            }
            catch (Win32Exception e)
            {
                // Can't use helper because when this throws the standard streams haven't been redirected and closing the streams fail...
                throw new BuildXLException($"[Pip{ProcessInfo.PipSemiStableHash} -- {ProcessInfo.PipDescription}] Failed to launch process: {ProcessInfo.FileName} {ProcessInfo.Arguments}", e);
            }

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
            var lifetime = DateTime.UtcNow - m_startTime;
            var cpuTimes = GetCpuTimes();
            LogProcessState(
                $"Process Times: " +
                $"started = {m_startTime}, " +
                $"exited = {m_exitTime} (since start = {ToSeconds(m_exitTime - m_startTime)}s), " +
                $"received reports = {m_reportsReceivedTime} (since start = {ToSeconds(m_reportsReceivedTime - m_startTime)}s), " +
                $"life time = {ToSeconds(lifetime)}s, " +
                $"user time = {ToSeconds(cpuTimes.User)}s, " +
                $"system time = {ToSeconds(cpuTimes.System)}s");
            SandboxedProcessFactory.Counters.AddToCounter(SandboxedProcessFactory.SandboxedProcessCounters.SandboxedProcessLifeTimeMs, (long)lifetime.TotalMilliseconds);
            Process?.Dispose();

            string ToSeconds(TimeSpan ts)
            {
                return (ts.TotalMilliseconds / 1000.0).ToString("0.00");
            }
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
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                return null;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        /// <inheritdoc />
        public long GetDetoursMaxHeapSize() => 0;

        /// <inheritdoc />
        public int GetLastMessageCount() => 0;

        /// <inheritdoc />
        public virtual async Task<SandboxedProcessResult> GetResultAsync()
        {
            Contract.Requires(Started);

            // 1: wait until process exits or process timeout is reached
            LogProcessState("Waiting for process to exit");
            var finishedTask = await Task.WhenAny(Task.Delay(ProcessInfo.Timeout.Value), WhenExited);
            m_exitTime = DateTime.UtcNow;

            // 2: kill process if timed out
            var timedOut = finishedTask != WhenExited;
            if (timedOut)
            {
                LogProcessState($"Process timed out after {ProcessInfo.Timeout.Value}; it will be forcefully killed.");
                await KillAsync();
            }

            // 3: wait for reports to be all received
            LogProcessState("Waiting for reports to be received");
            var reports = await GetReportsAsync();
            m_reportsReceivedTime = DateTime.UtcNow;
            reports?.Freeze();

            // 4: wait for stdout and stderr to be flushed
            LogProcessState("Waiting for stdout and stderr to be flushed");
            await Task.WhenAll(m_stdoutFlushedTcs.Task, m_stderrFlushedTcs.Task);

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
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
            {
                // thrown if the process doesn't exist (e.g., because it has already completed on its own)
            }

            m_stdoutFlushedTcs.TrySetResult(Unit.Void);
            m_stderrFlushedTcs.TrySetResult(Unit.Void);
            m_killed = true;
            return WhenExited;
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

#if PLATFORM_OSX
            // TODO: TASK 1488150
            // When targeting macOS and runnung on VSTS agents, we make sure the 'execute bit' has been set on the binary about to be started
            // as VSTS VMs currently have issues around file permission preservance
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AGENT_NAME")))
            {
                SetFilePermissionsForFilePath(ProcessInfo.FileName, FilePermissions.S_IRWXU);
            }
#endif

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
            Process.OutputDataReceived += (sender, e) => FeedStdOut(m_output, m_stdoutFlushedTcs, e.Data);
            Process.ErrorDataReceived  += (sender, e) => FeedStdErr(m_error, m_stderrFlushedTcs, e.Data);
            Process.Exited             += (sender, e) => m_processExitedTcs.TrySetResult(Unit.Void);

            return Process;
        }

        internal virtual void FeedStdOut(SandboxedProcessOutputBuilder b, TaskSourceSlim<Unit> tsc, string line)
            => FeedOutputBuilder(b, tsc, line);

        internal virtual void FeedStdErr(SandboxedProcessOutputBuilder b, TaskSourceSlim<Unit> tsc, string line)
            => FeedOutputBuilder(b, tsc, line);

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
        protected void LogProcessState(string message)
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

        internal static void FeedOutputBuilder(SandboxedProcessOutputBuilder output, TaskSourceSlim<Unit> signalCompletion, string line)
        {
            if (signalCompletion.Task.IsCompleted)
            {
                return;
            }

            output.AppendLine(line);
            if (line == null)
            {
                signalCompletion.TrySetResult(Unit.Void);
            }
        }

        /// <nodoc/>
        [NotNull]
        internal virtual CpuTimes GetCpuTimes()
        {
            // 'Dispatch.GetProcessTimes()' doesn't work because the process has already exited
            return CpuTimes.Zeros;
        }

        private ProcessTimes GetProcessTimes()
        {
            if (m_startTime == default(DateTime) || m_exitTime == default(DateTime))
            {
                return new ProcessTimes(0, 0, 0, 0);
            }

            var cpuTimes = GetCpuTimes();
            return new ProcessTimes(
                creation: m_startTime.ToFileTimeUtc(),
                exit: m_exitTime.ToFileTimeUtc(),
                kernel: cpuTimes.System.Ticks,
                user: cpuTimes.User.Ticks);
        }
    }
}
