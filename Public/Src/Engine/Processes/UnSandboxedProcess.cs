// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
#if FEATURE_SAFE_PROCESS_HANDLE
using Microsoft.Win32.SafeHandles;
#else
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
#endif
#if PLATFORM_OSX
using static BuildXL.Interop.MacOS.IO;
#endif

using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Processes
{
    /// <summary>
    /// An implementation of <see cref="ISandboxedProcess"/> that doesn't perform any sandboxing.
    /// </summary>
    public class UnsandboxedProcess : ISandboxedProcess
    {
        private static readonly ISet<ReportedFileAccess> s_emptyFileAccessesSet = new HashSet<ReportedFileAccess>();
        private static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromMinutes(SandboxConfiguration.DefaultProcessTimeoutInMinutes);

        private readonly SandboxedProcessOutputBuilder m_output;
        private readonly SandboxedProcessOutputBuilder m_error;
        private readonly AsyncProcessExecutor m_processExecutor;

        private DateTime m_reportsReceivedTime;
        private Exception m_dumpCreationException;

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
        /// Delegate type for <see cref="ProcessStarted"/> event.
        /// </summary>
        protected delegate void ProcessStartedHandler(int processId);

        /// <summary>
        /// Raised right after the process is started.
        /// </summary>

        protected event ProcessStartedHandler ProcessStarted;

        /// <summary>
        /// Indicates if the process has been force killed during execution.
        /// </summary>
        protected virtual bool Killed => m_processExecutor?.Killed ?? false;

        /// <summary>
        /// Underlying managed <see cref="Process"/> object.
        /// </summary>
        protected Process Process => m_processExecutor?.Process;

        /// <summary>
        /// Logging context from the <see cref="SandboxedProcessInfo"/> object passed to the constructor.
        /// </summary>
        protected LoggingContext LoggingContext { get; }

        /// <summary>
        /// Pip description from the <see cref="SandboxedProcessInfo"/> object passed to the constructor.
        /// </summary>
        protected string PipDescription { get; }

        /// <summary>
        /// Pip's semi-stable hash from the <see cref="SandboxedProcessInfo"/> object passed to the constructor.
        /// </summary>
        protected long PipSemiStableHash { get; }

        /// <summary>
        /// Returns the path table from the supplied <see cref="SandboxedProcessInfo"/>.
        /// </summary>
        protected PathTable PathTable { get; }

        /// <summary>
        /// Whether /logObservedFileAccesses has been requested
        /// </summary>
        protected bool ShouldReportFileAccesses { get; }

        /// <summary>
        /// Whether there were any failures regarding sandboxing (e.g., sandbox
        /// couldn't be created, some file access reports were dropped, etc.)
        /// </summary>
        protected virtual bool HasSandboxFailures => false;

        private string TimeoutDumpDirectory { get; }

        /// <remarks>
        /// IMPORTANT: For memory efficiency reasons don't keep a reference to <paramref name="info"/>
        ///            or its  <see cref="SandboxedProcessInfo.FileAccessManifest"/> property
        ///            (at least not after the process has been started)
        /// </remarks>
        public UnsandboxedProcess(SandboxedProcessInfo info)
        {
            Contract.Requires(info != null);

            Started = false;
            PathTable = info.PathTable;
            LoggingContext = info.LoggingContext;
            PipDescription = info.PipDescription;
            PipSemiStableHash = info.PipSemiStableHash;
            TimeoutDumpDirectory = info.TimeoutDumpDirectory;
            ShouldReportFileAccesses = info.FileAccessManifest?.ReportFileAccesses == true;

            info.Timeout = info.Timeout ?? DefaultProcessTimeout;

            m_output = new SandboxedProcessOutputBuilder(
                info.StandardOutputEncoding ?? Console.OutputEncoding,
                info.MaxLengthInMemory,
                info.FileStorage,
                SandboxedProcessFile.StandardOutput,
                info.StandardOutputObserver);

            m_error = new SandboxedProcessOutputBuilder(
                info.StandardErrorEncoding ?? Console.OutputEncoding,
                info.MaxLengthInMemory,
                info.FileStorage,
                SandboxedProcessFile.StandardError,
                info.StandardErrorObserver);

            m_processExecutor = new AsyncProcessExecutor(
                CreateProcess(info),
                info.Timeout ?? DefaultProcessTimeout,
                line => FeedStdOut(m_output, line),
                line => FeedStdErr(m_error, line),
                info.Provenance,
                msg => LogProcessState(msg));

            if (info.ProcessIdListener != null)
            {
                ProcessStarted += (pid) => info.ProcessIdListener(pid);
                m_processExecutor.Process.Exited += (sender, e) => info.ProcessIdListener(-m_processExecutor.ProcessId);
            }
        }

        /// <summary>
        /// Whether this process has been started (i.e., the <see cref="Start"/> method has been called on it).
        /// </summary>
        public bool Started { get; private set; }

        /// <summary>
        /// Difference between now and when the process was started.
        /// </summary>
        protected TimeSpan CurrentRunningTime()
        {
            Contract.Requires(Started);
            return DateTime.UtcNow.Subtract(m_processExecutor.StartTime);
        }

        /// <inheritdoc />
        public void Start()
        {
            Contract.Requires(!Started, "Process was already started.  Cannot start process more than once.");

            Started = true;
            m_processExecutor.Start();
            ProcessStarted?.Invoke(ProcessId);
        }

        /// <inheritdoc />
        public int ProcessId => m_processExecutor?.ProcessId ?? -1;

        /// <inheritdoc />
        public virtual void Dispose()
        {
            var startTime = m_processExecutor?.StartTime ?? DateTime.UtcNow;
            var exitTime = m_processExecutor?.ExitTime ?? DateTime.UtcNow;

            var lifetime = DateTime.UtcNow - startTime;
            var cpuTimes = GetCpuTimes();
            LogProcessState(
                $"Process Times: " +
                $"started = {startTime}, " +
                $"exited = {exitTime} (since start = {toSeconds(exitTime - startTime)}s), " +
                $"received reports = {m_reportsReceivedTime} (since start = {toSeconds(m_reportsReceivedTime - startTime)}s), " +
                $"life time = {toSeconds(lifetime)}s, " +
                $"user time = {toSeconds(cpuTimes.User)}s, " +
                $"system time = {toSeconds(cpuTimes.System)}s");
            SandboxedProcessFactory.Counters.AddToCounter(SandboxedProcessFactory.SandboxedProcessCounters.SandboxedProcessLifeTimeMs, (long)lifetime.TotalMilliseconds);
            m_processExecutor?.Dispose();

            static string toSeconds(TimeSpan ts)
            {
                return (ts.TotalMilliseconds / 1000.0).ToString("0.00");
            }
        }

        /// <inheritdoc />
        public string GetAccessedFileName(ReportedFileAccess reportedFileAccess) => null;

        /// <inheritdoc />
        public ulong? GetActivePeakWorkingSet()
        {
            try
            {
                if (Process == null || Process.HasExited)
                {
                    return null;
                }

                return Dispatch.GetActivePeakWorkingSet(Process.Handle, ProcessId);
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
        public async Task<SandboxedProcessResult> GetResultAsync()
        {
            Contract.Requires(Started);

            SandboxedProcessReports reports = null;

            await m_processExecutor.WaitForExitAsync();
            if (m_processExecutor.Killed)
            {
                // call here this.KillAsync() because a subclass may override it
                // to do some extra processing when a process is killed
                await KillAsync();
            }

            LogProcessState("Waiting for reports to be received");
            reports = await GetReportsAsync();
            m_reportsReceivedTime = DateTime.UtcNow;
            reports?.Freeze();

            await m_processExecutor.WaitForStdOutAndStdErrAsync();

            var fileAccesses = ShouldReportFileAccesses ? (reports?.FileAccesses ?? s_emptyFileAccessesSet) : null;

            return new SandboxedProcessResult
            {
                ExitCode                            = m_processExecutor.TimedOut ? ExitCodes.Timeout : Process.ExitCode,
                Killed                              = Killed,
                TimedOut                            = m_processExecutor.TimedOut,
                HasDetoursInjectionFailures         = HasSandboxFailures,
                JobAccountingInformation            = GetJobAccountingInfo(),
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
                DumpFileDirectory                   = TimeoutDumpDirectory,
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

            ProcessDumper.TryDumpProcessAndChildren(ProcessId, TimeoutDumpDirectory, out m_dumpCreationException);

            LogProcessState($"UnsandboxedProcess::KillAsync()");
            return m_processExecutor.KillAsync();
        }

        /// <summary>
        /// Creates a <see cref="Process"/>.
        /// </summary>
        protected virtual Process CreateProcess(SandboxedProcessInfo info)
        {
            Contract.Requires(!Started);

#if !PLATFORM_WIN
            var mode = GetFilePermissionsForFilePath(info.FileName, followSymlink: false);
            if (mode < 0)
            {
                ThrowBuildXLException($"Process creation failed: File '{info.FileName}' not found", new Win32Exception(0x2));
            }

            var filePermissions = checked((FilePermissions)mode);
            FilePermissions exePermission = FilePermissions.S_IXUSR;
            if (!filePermissions.HasFlag(exePermission))
            {
                SetFilePermissionsForFilePath(info.FileName, exePermission);
            }
#endif

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = info.FileName,
                    Arguments = info.Arguments,
                    WorkingDirectory = info.WorkingDirectory,
                    StandardErrorEncoding = m_output.Encoding,
                    StandardOutputEncoding = m_error.Encoding,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            process.StartInfo.EnvironmentVariables.Clear();
            if (info.EnvironmentVariables != null)
            {
                foreach (var envKvp in info.EnvironmentVariables.ToDictionary())
                {
                    process.StartInfo.EnvironmentVariables[envKvp.Key] = envKvp.Value;
                }
            }

            return process;
        }

        internal virtual void FeedStdOut(SandboxedProcessOutputBuilder b, string line)
            => FeedOutputBuilder(b, line);

        internal virtual void FeedStdErr(SandboxedProcessOutputBuilder b, string line)
            => FeedOutputBuilder(b, line);

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
            throw new BuildXLException($"[Pip{PipSemiStableHash:X} -- {PipDescription}] {message}", inner);
        }

        /// <nodoc />
        protected virtual bool ReportsCompleted() => true;

        /// <nodoc />
        protected void LogProcessState(string message)
        {
            if (LoggingContext != null)
            {
                string fullMessage = I($"Exited: {m_processExecutor?.ExitCompleted ?? false}, StdOut: {m_processExecutor?.StdOutCompleted ?? false}, StdErr: {m_processExecutor?.StdErrCompleted ?? false}, Reports: {ReportsCompleted()} :: {message}");
                Tracing.Logger.Log.LogDetoursDebugMessage(LoggingContext, PipSemiStableHash, PipDescription, fullMessage);
            }
        }

        /// <summary>
        /// Returns surviving child processes in the case when the pip finished and potential child
        /// processes didn't exit within an allotted time (<see cref="SandboxedProcessInfo.NestedProcessTerminationTimeout"/>)
        /// after the main pip parent process has already exited.
        /// </summary>
        protected virtual IEnumerable<ReportedProcess> GetSurvivingChildProcesses() => null;

        /// <summary>
        /// Returns any collected sandboxed process reports or null.
        /// </summary>
        internal virtual Task<SandboxedProcessReports> GetReportsAsync() => Task.FromResult<SandboxedProcessReports>(null);

        internal static void FeedOutputBuilder(SandboxedProcessOutputBuilder output, string line)
        {
            if (line != null)
            {
                output.AppendLine(line);
            }
        }

        /// <nodoc/>
        [NotNull]
        internal virtual CpuTimes GetCpuTimes()
        {
            // 'Dispatch.GetProcessResourceUsage()' doesn't work because the process has already exited
            return CpuTimes.Zeros;
        }

        /// <nodoc/>
        internal virtual JobObject.AccountingInformation GetJobAccountingInfo()
        {
            return default;
        }

        private ProcessTimes GetProcessTimes()
        {
            if (m_processExecutor.StartTime == default || m_processExecutor.ExitTime == default)
            {
                return new ProcessTimes(0, 0, 0, 0);
            }

            var cpuTimes = GetCpuTimes();
            return new ProcessTimes(
                creation: m_processExecutor.StartTime.ToFileTimeUtc(),
                exit: m_processExecutor.ExitTime.ToFileTimeUtc(),
                kernel: cpuTimes.System.Ticks,
                user: cpuTimes.User.Ticks);
        }
    }
}
