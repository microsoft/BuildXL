// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Interop.MacOS;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Processes
{
    /// <summary>
    /// Implementation of <see cref="ISandboxedProcess"/> that relies on our kernel extension for Mac
    /// </summary>
    public sealed class SandboxedProcessMacKext : UnSandboxedProcess
    {
        private readonly SandboxedProcessReports m_reports;

        private readonly ActionBlock<Sandbox.AccessReport> m_pendingReports;

        private IEnumerable<ReportedProcess> m_survivingChildProcesses;

        private long m_processTreeCompletedAckOperationCount = 0;

        private long m_processKilledFlag = 0;

        private readonly SandboxedProcessInfo m_processInfo;

        /// <summary>
        /// Returns the associated PipId
        /// </summary>
        private long PipId => ProcessInfo.FileAccessManifest.PipId;

        /// <nodoc />
        public SandboxedProcessMacKext(SandboxedProcessInfo info)
            : base(info)
        {
            Contract.Requires(info.FileAccessManifest != null);
            Contract.Requires(info.SandboxedKextConnection != null);

            m_processInfo = info;

            m_reports = new SandboxedProcessReports(
                info.FileAccessManifest,
                info.PathTable,
                info.PipSemiStableHash,
                info.PipDescription,
                info.LoggingContext,
                info.DetoursEventListener);

            m_pendingReports = new ActionBlock<Sandbox.AccessReport>(
                (Action<Sandbox.AccessReport>)HandleKextReport,
                new ExecutionDataflowBlockOptions
                {
#if FEATURE_CORECLR
                    EnsureOrdered = true,
#endif
                    BoundedCapacity = ExecutionDataflowBlockOptions.Unbounded,
                    MaxDegreeOfParallelism = 1, // Must be one, otherwise SandboxedPipExecutor will fail asserting valid reports
                });
        }

        /// <inheritdoc />
        public override void Start() => StartAsync().GetAwaiter().GetResult();

        private async Task StartAsync()
        {
            base.CreateAndSetUpProcess();

            Process.StartInfo.FileName = "/bin/sh";
            Process.StartInfo.Arguments = string.Empty;
            Process.StartInfo.RedirectStandardInput = true;
            Process.Start();
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();

            // Generate "Process Created" report because the rest of the system expects to see it before any other file access reports
            //
            // IMPORTANT: do this before notifying sandbox kernel extension, because otherwise it can happen that a report
            //            from the extension is received before the "process created" report is handled, causing
            //            a "Should see a process creation before its accesses" assertion exception.
            ReportProcessCreated();

            // Allow read access for /bin/sh
            ProcessInfo.FileAccessManifest.AddPath(
                AbsolutePath.Create(PathTable, Process.StartInfo.FileName),
                mask: FileAccessPolicy.MaskNothing,
                values: FileAccessPolicy.AllowReadAlways);

            if (!m_processInfo.SandboxedKextConnection.NotifyKextPipStarted(ProcessInfo.FileAccessManifest, this))
            {
                ThrowCouldNotStartProcess("Failed to notify kernel extension about process start, make sure the extension is loaded");
            }

            // feed standard input
            await FeedStdInAsync();

            SetProcessStartedExecuting();
            SetProcessStartedExecuting();
        }

        /// <inheritdoc />
        protected override bool Killed => Interlocked.Read(ref m_processKilledFlag) > 0;

        /// <inheritdoc />
        public override Task KillAsync()
        {
            // In the case that the process gets shut down by either its timeout or e.g. SandboxedProcessPipExecutor
            // detecting resource usage issues and calling KillAsync(), we flag the process with m_processKilled so we
            // don't process any more kernel reports that get pushed into report structure asynchronously!
            Interlocked.Increment(ref m_processKilledFlag);
            m_pendingReports.Complete();

            KillAllChildProcesses();
            return base.KillAsync();
        }

        /// <inheritdoc />
        protected override IEnumerable<ReportedProcess> GetSurvivingChildProcesses() => m_survivingChildProcesses;

        /// <summary>
        /// Waits for all child processes to finish within a timeout limit and then termiantes all still running children after that point.
        /// After all the children have been taken care of, the method waits for pending report processing to finish, then returns the
        /// collected reports.
        /// </summary>
        internal override async Task<SandboxedProcessReports> GetReportsAsync()
        {
            if (!Killed)
            {
                var waitTimeMs = ProcessInfo.NestedProcessTerminationTimeout.TotalMilliseconds * m_processInfo.SandboxedKextConnection.NumberOfKextConnections;
                var waitTime = TimeSpan.FromMilliseconds(waitTimeMs);
                var childProcessTimeoutTask = Task.Delay(ProcessInfo.NestedProcessTerminationTimeout);
                var awaitedTask = await Task.WhenAny(childProcessTimeoutTask, m_pendingReports.Completion);

                if (awaitedTask == childProcessTimeoutTask)
                {
                    m_survivingChildProcesses = CoalesceProcesses(m_reports.GetCurrentlyActiveProcesses());
                    await KillAsync();
                }
            }

            // in any case must wait for pending reports to complete, because we must not freeze m_reports before that happens
            await m_pendingReports.Completion;

            // at this point this pip is done executing (it's only left to construct SandboxedProcessResult,
            // which is done by the base class) so notify the sandbox kernel extension connection manager about it.
            m_processInfo.SandboxedKextConnection.NotifyKextProcessFinished(PipId, this);

            return m_reports;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (!Killed)
            {
                // Try to kill all processes once the parent gets disposed, so we clean up all used
                // system resources appropriately
                KillAllChildProcesses();
            }

            base.Dispose();
        }

        private void KillAllChildProcesses()
        {
            NotifyKextPipTerminated(PipId, CoalesceProcesses(m_reports.GetCurrentlyActiveProcesses()));
        }

        /// <nodoc />
        protected override bool ReportsCompleted() => m_pendingReports.Completion.IsCompleted;

        /// <summary>
        /// Must not be blocking and should return as soon as possible
        /// </summary>
        internal void PostAccessReport(Sandbox.AccessReport report)
        {
            m_pendingReports.Post(report);
        }

        private static void NotifyKextPipTerminated(long pipId, IEnumerable<ReportedProcess> survivingChildProcesses)
        {
            // TODO: bundle this into a single message
            var distinctProcessIds = new HashSet<uint>(survivingChildProcesses.Select(p => p.ProcessId));
            foreach (var processId in distinctProcessIds)
            {
                Sandbox.SendPipProcessTerminated(pipId, (int)processId);
            }
        }

        private async Task FeedStdInAsync()
        {
            string processStdinFileName = await FlushStandardInputToFileIfNeededAsync();
            string redirectedStdin = processStdinFileName != null ? $" < {processStdinFileName}" : string.Empty;
            string escapedArguments = ProcessInfo.Arguments.Replace(Environment.NewLine, "\\" + Environment.NewLine);

            await Process.StandardInput.WriteLineAsync(I($"exec {ProcessInfo.FileName} {escapedArguments} {redirectedStdin}"));

            Process.StandardInput.Close();
        }

        private void ReportProcessCreated()
        {
            var pidHex           = Process.Id.ToString("X");
            var fileAccess       = (int)ReportType.FileAccess;
            var pipIdHex         = PipId.ToString("X");
            var desiredAccessHex = DesiredAccess.GENERIC_READ.ToString("X");
            var dispositionHex   = CreationDisposition.OPEN_EXISTING.ToString("X");
            var procArgs         = ProcessInfo.FileAccessManifest.ReportProcessArgs
                ? I($"{ProcessInfo.FileName} {ProcessInfo.Arguments}")
                : string.Empty;

            var reportLine = I($"{fileAccess},Process:{pidHex}|1|1|0|0|{pipIdHex}|{desiredAccessHex}|0|{dispositionHex}|0|0|{ProcessInfo.FileName}||{procArgs}");
            m_reports.ReportLineReceived(reportLine);
        }

        private void HandleKextReport(Sandbox.AccessReport report)
        {
            // TODO: m_reports should be able to receive AccessReport object so that we don't have
            //       here render it to string only to be parsed again by SandboxedProcessReports
            string reportLine = AccessReportToString(report, out string operation, out _, out bool pathExists);

            // don't report MAC_LOOKUP probes for existent paths (because for those paths other reports will follow)
            if (operation == OpNames.OpMacLookup && pathExists)
            {
                return;
            }

            if (reportLine.Contains(OpNames.OpProcessTreeCompleted))
            {
                // We make sure that we get the ProcessTreeCompletedAckOperation as often as there are event queue workers,
                // this makes sure there are no more reports left in the queues for this process.
                Interlocked.Increment(ref m_processTreeCompletedAckOperationCount);
                if (Interlocked.Read(ref m_processTreeCompletedAckOperationCount) == m_processInfo.SandboxedKextConnection.NumberOfKextConnections)
                {
                    m_pendingReports.Complete();
                }
            }
            else
            {
                if (!Killed)
                {
                    m_reports.ReportLineReceived(reportLine);
                }
            }
        }

        /// <summary>
        /// If <see cref="SandboxedProcessInfo.StandardInputReader"/> is set, it flushes the content of that reader
        /// to a file in the process's working directory and returns the absolute path of that file; otherwise returns null.
        /// </summary>
        private async Task<string> FlushStandardInputToFileIfNeededAsync()
        {
            if (ProcessInfo.StandardInputReader == null)
            {
                return null;
            }

            string stdinFileName = Path.Combine(Process.StartInfo.WorkingDirectory, ProcessInfo.PipSemiStableHash + ".stdin");
            string stdinText = await ProcessInfo.StandardInputReader.ReadToEndAsync();
            Encoding encoding = ProcessInfo.StandardInputEncoding ?? Console.InputEncoding;
            byte[] stdinBytes = encoding.GetBytes(stdinText);
            bool stdinFileWritten = await FileUtilities.WriteAllBytesAsync(stdinFileName, stdinBytes);
            if (!stdinFileWritten)
            {
                ThrowCouldNotStartProcess($"failed to flush standard input to file '{stdinFileName}'");
            }

            // Allow read from the created stdin file
            ProcessInfo.FileAccessManifest.AddPath(AbsolutePath.Create(PathTable, stdinFileName), mask: FileAccessPolicy.MaskNothing, values: FileAccessPolicy.AllowRead);

            return stdinFileName;
        }

        private string AccessReportToString(Sandbox.AccessReport report, out string operation, out string path, out bool pathExists)
        {
            operation = Encoding.UTF8.GetString(report.Operation).TrimEnd('\0');
            path = Encoding.UTF8.GetString(report.Path).TrimEnd('\0');
            pathExists = File.Exists(path) || Directory.Exists(path);

            var type = report.Type;
            var pid = report.Pid.ToString("X");
            var requestedAccess = report.RequestedAccess;
            var status = report.Status;
            var explicitLogging = report.ExplicitLogging != 0 ? 1 : 0;
            var error = report.Error;
            var usn = ReportedFileAccess.NoUsn.Value.ToString("X");
            var desiredAccess = report.DesiredAccess.ToString("X");
            var shareMode = report.ShareMode;
            var disposition = report.Disposition;
            var flagsAndAttributes = report.FlagsAndAttributes.ToString("X");
            var pathId = (AbsolutePath.TryCreate(PathTable, path, out var absPath) ? absPath.RawValue : 0).ToString("X");

            // our sandbox kernel extension currently doesn't detect file existence, so do it here instead
            if (error == 0 && !pathExists)
            {
                error = ReportedFileAccess.ERROR_PATH_NOT_FOUND;
            }

            return
                I($"{type},{operation}:{pid}|{requestedAccess}|{status}|{explicitLogging}") +
                I($"|{error}|{usn}|{desiredAccess}|{shareMode}|{disposition}|{flagsAndAttributes}|{pathId}|{path}|");
        }
    }
}
