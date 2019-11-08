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
using BuildXL.Interop;
using BuildXL.Interop.MacOS;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using JetBrains.Annotations;
using static BuildXL.Interop.MacOS.Sandbox;
using static BuildXL.Processes.SandboxedProcessFactory;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Processes
{
    /// <summary>
    /// Implementation of <see cref="ISandboxedProcess"/> that relies on our kernel extension for Mac
    /// </summary>
    public sealed class SandboxedProcessMac : UnsandboxedProcess
    {
        private class PerfAggregator
        {
            internal PerformanceCollector.Aggregation KernelTimeMs { get; } = new PerformanceCollector.Aggregation();
            internal PerformanceCollector.Aggregation UserTimeMs { get; } = new PerformanceCollector.Aggregation();
            internal PerformanceCollector.Aggregation JobKernelTimeMs { get; } = new PerformanceCollector.Aggregation();
            internal PerformanceCollector.Aggregation JobUserTimeMs { get; } = new PerformanceCollector.Aggregation();
            internal PerformanceCollector.Aggregation PeakMemoryBytes { get; } = new PerformanceCollector.Aggregation();
            internal PerformanceCollector.Aggregation DiskBytesRead { get; } = new PerformanceCollector.Aggregation();
            internal PerformanceCollector.Aggregation DiskBytesWritten { get; } = new PerformanceCollector.Aggregation();
        }

        /// <summary>
        /// Interval at which the process is probed for its counters (e.g., CPU time, memory usage, etc.)
        /// </summary>
        public static readonly TimeSpan PerfProbeInternal = TimeSpan.FromSeconds(2);

        private readonly SandboxedProcessReports m_reports;

        private readonly ActionBlock<AccessReport> m_pendingReports;

        private readonly CancellableTimedAction m_perfCollector;
        private readonly PerfAggregator m_perfAggregator;

        private IEnumerable<ReportedProcess> m_survivingChildProcesses = null;

        private PipKextStats? m_pipKextStats = null;

        private long m_processKilledFlag = 0;

        private ulong m_processExitTimeNs = ulong.MaxValue;

        private bool HasProcessExitBeenReceived => m_processExitTimeNs != ulong.MaxValue;

        private readonly CancellationTokenSource m_timeoutTaskCancelationSource = new CancellationTokenSource();

        private long PipId { get; }

        private ISandboxConnection SandboxConnection { get; }

        private TimeSpan ChildProcessTimeout { get; }

        private TimeSpan? ReportQueueProcessTimeoutForTests { get; }

        /// <summary>
        /// Accumulates the time (in microseconds) access reports spend in the report queue
        /// </summary>
        private long m_sumOfReportQueueTimesUs;

        /// <summary>
        /// Accumulates the time (in microseconds) access reports need from kernel callbacks, over creation to the moment they are enqueued
        /// </summary>
        private long m_sumOfReportCreationTimesUs;

        /// <summary>
        /// Timeout period for inactivity from the sandbox kernel extension.
        /// </summary>
        internal TimeSpan ReportQueueProcessTimeout => SandboxConnection.IsInTestMode
            ? ReportQueueProcessTimeoutForTests ?? TimeSpan.FromSeconds(100)
            : TimeSpan.FromMinutes(45);

        private Task m_processTreeTimeoutTask;

        /// <summary>
        /// Allowed surviving child process names.
        /// </summary>
        private string[] AllowedSurvivingChildProcessNames { get; }

        private bool IgnoreReportedAccesses { get; }

        /// <see cref="ISandboxConnection.MeasureCpuTimes"/>
        private bool MeasureCpuTime { get; }

        /// <summary>
        /// Absolute path to the executable file.
        /// </summary>
        public string ExecutableAbsolutePath => Process.StartInfo.FileName;

        /// <nodoc />
        public SandboxedProcessMac(SandboxedProcessInfo info, bool ignoreReportedAccesses = false, bool? overrideMeasureTime = null)
            : base(info)
        {
            Contract.Requires(info.FileAccessManifest != null);
            Contract.Requires(info.SandboxConnection != null);

            PipId = info.FileAccessManifest.PipId;
            SandboxConnection = info.SandboxConnection;
            ChildProcessTimeout = info.NestedProcessTerminationTimeout;
            AllowedSurvivingChildProcessNames = info.AllowedSurvivingChildProcessNames;
            ReportQueueProcessTimeoutForTests = info.ReportQueueProcessTimeoutForTests;
            IgnoreReportedAccesses = ignoreReportedAccesses;

            MeasureCpuTime = overrideMeasureTime.HasValue
                ? overrideMeasureTime.Value
                : info.SandboxConnection.MeasureCpuTimes;

            m_perfAggregator = new PerfAggregator();

            m_perfCollector = new CancellableTimedAction(
                callback: UpdatePerfCounters,
                intervalMs: (int)PerfProbeInternal.TotalMilliseconds);

            m_reports = new SandboxedProcessReports(
                info.FileAccessManifest,
                info.PathTable,
                info.PipSemiStableHash,
                info.PipDescription,
                info.LoggingContext,
                info.DetoursEventListener,
                info.SidebandWriter);

            m_pendingReports = new ActionBlock<AccessReport>(
                HandleAccessReport,
                new ExecutionDataflowBlockOptions
                {
                    EnsureOrdered = true,
                    BoundedCapacity = DataflowBlockOptions.Unbounded,
                    MaxDegreeOfParallelism = 1, // Must be one, otherwise SandboxedPipExecutor will fail asserting valid reports
                });

            // install a 'ProcessStarted' handler that informs the sandbox of the newly started process
            ProcessStarted += (pid) => OnProcessStartedAsync(info).GetAwaiter().GetResult();
        }

        private void UpdatePerfCounters()
        {
            if (Process.HasExited)
            {
                return;
            }

            var buffer = new Process.ProcessResourceUsage();

            // get processor times for the root process itself
            int errCode = Interop.MacOS.Process.GetProcessResourceUsage(ProcessId, ref buffer, includeChildProcesses: false);
            if (errCode == 0)
            {
                m_perfAggregator.KernelTimeMs.RegisterSample(buffer.SystemTimeNs / 1000);
                m_perfAggregator.UserTimeMs.RegisterSample(buffer.UserTimeNs / 1000);
            }

            // get processor times for the process tree
            errCode = Interop.MacOS.Process.GetProcessResourceUsage(ProcessId, ref buffer, includeChildProcesses: true);
            if (errCode == 0)
            {
                m_perfAggregator.JobKernelTimeMs.RegisterSample(buffer.SystemTimeNs / 1000);
                m_perfAggregator.JobUserTimeMs.RegisterSample(buffer.UserTimeNs / 1000);
                m_perfAggregator.DiskBytesRead.RegisterSample(buffer.DiskBytesRead);
                m_perfAggregator.DiskBytesWritten.RegisterSample(buffer.DiskBytesWritten);
            }

            // get memory usage for the process tree
            m_perfAggregator.PeakMemoryBytes.RegisterSample(Dispatch.GetActivePeakWorkingSet(default, ProcessId));
        }

        /// <inheritdoc />
        protected override System.Diagnostics.Process CreateProcess(SandboxedProcessInfo info)
        {
            var process = base.CreateProcess(info);

            process.StartInfo.FileName = "/bin/sh";
            process.StartInfo.Arguments = string.Empty;
            process.StartInfo.RedirectStandardInput = true;

            return process;
        }

        /// <summary>
        /// Called right after the process starts executing.
        ///
        /// Since we set the process file name to be /bin/sh and its arguments to be empty (<see cref="CreateProcess"/>),
        /// the process will effectively start in a "suspended" mode, with /bin/sh just waiting for some content to be
        /// piped to its standard input.  Therefore, in this handler we first notify the sandbox of the new process (so that
        /// it starts tracking it) and then just send the actual process command line to /bin/sh via its standard input.
        /// </summary>
        private async Task OnProcessStartedAsync(SandboxedProcessInfo info)
        {
            // Generate "Process Created" report because the rest of the system expects to see it before any other file access reports
            //
            // IMPORTANT: do this before notifying sandbox kernel extension, because otherwise it can happen that a report
            //            from the extension is received before the "process created" report is handled, causing
            //            a "Should see a process creation before its accesses" assertion exception.
            ReportProcessCreated();

            // Allow read access for /bin/sh
            info.FileAccessManifest.AddPath(
                AbsolutePath.Create(PathTable, Process.StartInfo.FileName),
                mask: FileAccessPolicy.MaskNothing,
                values: FileAccessPolicy.AllowReadAlways);

            if (MeasureCpuTime)
            {
                m_perfCollector.Start();
            }

            string processStdinFileName = await FlushStandardInputToFileIfNeededAsync(info);

            if (!SandboxConnection.NotifyPipStarted(info.FileAccessManifest, this))
            {
                ThrowCouldNotStartProcess("Failed to notify kernel extension about process start, make sure the extension is loaded");
            }

            try
            {
                await FeedStdInAsync(info, processStdinFileName);
                m_processTreeTimeoutTask = ProcessTreeTimeoutTask();
            }
            catch (IOException e)
            {
                // IOException can happen if the process is forcefully killed while we're feeding its std in.
                // When that happens, instead of crashing, just make sure the process is killed.
                LogProcessState($"IOException caught while feeding the standard input: {e.ToString()}");
                await KillAsync();
            }
            finally
            {
                // release the FileAccessManifest memory
                // NOTE: just by not keeping any references to 'info' should make the FileAccessManifest object 
                //       unreachable and thus available for garbage collection.  We call Release() here explicitly 
                //       just to emphasise the importance of reclaiming this memory.
                info.FileAccessManifest.Release();
            }
        }

        /// <inheritdoc />
        protected override IEnumerable<ReportedProcess> GetSurvivingChildProcesses()
        {
            return m_survivingChildProcesses;
        }

        /// <inheritdoc />
        protected override bool Killed => Interlocked.Read(ref m_processKilledFlag) > 0;

        /// <inheritdoc />
        public override async Task KillAsync()
        {
            LogProcessState("SandboxedProcessMac::KillAsync");

            // In the case that the process gets shut down by either its timeout or e.g. SandboxedProcessPipExecutor
            // detecting resource usage issues and calling KillAsync(), we flag the process with m_processKilled so we
            // don't process any more kernel reports that get pushed into report structure asynchronously!
            long incrementedValue = Interlocked.Increment(ref m_processKilledFlag);

            // Make sure this is done no more than once.
            if (incrementedValue == 1)
            {
                await base.KillAsync();
                KillAllChildProcesses();
                await m_pendingReports.Completion;
            }
        }

        /// <summary>
        /// Waits for all child processes to finish within a timeout limit and then termiantes all still running children after that point.
        /// After all the children have been taken care of, the method waits for pending report processing to finish, then returns the
        /// collected reports.
        /// </summary>
        internal override async Task<SandboxedProcessReports> GetReportsAsync()
        {
            if (!Killed)
            {
                var awaitedTask = await Task.WhenAny(m_pendingReports.Completion, m_processTreeTimeoutTask);
                if (awaitedTask == m_processTreeTimeoutTask)
                {
                    LogProcessState("Waiting for reports timed out; any surviving processes will be forcefully killed.");
                    await KillAsync();
                }
            }

            // in any case must wait for pending reports to complete, because we must not freeze m_reports before that happens
            await m_pendingReports.Completion;

            // at this point this pip is done executing (it's only left to construct SandboxedProcessResult,
            // which is done by the base class) so notify the sandbox kernel extension connection manager about it.
            SandboxConnection.NotifyProcessFinished(PipId, this);

            return IgnoreReportedAccesses ? null : m_reports;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            m_perfCollector.Cancel();
            m_perfCollector.Join();
            m_perfCollector.Dispose();
            m_timeoutTaskCancelationSource.Cancel();

            var reportCount = Counters.GetCounterValue(SandboxedProcessCounters.AccessReportCount);
            if (reportCount > 0)
            {
                Counters.AddToCounter(SandboxedProcessCounters.SumOfAccessReportAvgQueueTimeUs, m_sumOfReportQueueTimesUs / reportCount);
                Counters.AddToCounter(SandboxedProcessCounters.SumOfAccessReportAvgCreationTimeUs, m_sumOfReportCreationTimesUs / reportCount);
            }

            if (!Killed)
            {
                // Try to kill all processes once the parent gets disposed, so we clean up all used
                // system resources appropriately
                KillAllChildProcesses();
            }

            if (m_pipKextStats != null)
            {
                var statsJson = Newtonsoft.Json.JsonConvert.SerializeObject(m_pipKextStats.Value);
                LogProcessState($"Process Kext Stats: {statsJson}");
            }

            base.Dispose();
        }

        /// <summary>
        /// This method reads some collections from the non-thread-safe <see cref="m_reports"/> object.
        /// The callers must make sure that when they call this method no concurrent modifications are
        /// being done to <see cref="m_reports"/>.
        /// </summary>
        private IReadOnlyList<ReportedProcess> GetCurrentlyActiveChildProcesses()
        {
            return m_reports.GetActiveProcesses().Where(p => p.ProcessId != ProcessId).ToList();
        }

        private void KillAllChildProcesses()
        {
            NotifyPipTerminated(PipId, CoalesceProcesses(GetCurrentlyActiveChildProcesses()));
        }

        private bool ShouldWaitForSurvivingChildProcesses()
        {
            // Wait for surviving child processes if no allowable process names are explicitly specified.
            if (AllowedSurvivingChildProcessNames == null || AllowedSurvivingChildProcessNames.Length == 0)
            {
                return true;
            }

            // Otherwise, wait if there are any alive processes that are not explicitly allowed to survivie
            var aliveProcessesNames = CoalesceProcesses(GetCurrentlyActiveChildProcesses()).Select(p => Path.GetFileName(p.Path));
            return aliveProcessesNames
                .Except(AllowedSurvivingChildProcessNames)
                .Any();
        }

        /// <nodoc />
        protected override bool ReportsCompleted() => m_pendingReports.Completion.IsCompleted;

        /// <summary>
        /// Must not be blocking and should return as soon as possible
        /// </summary>
        internal void PostAccessReport(AccessReport report)
        {
            m_pendingReports.Post(report);
        }

        private void NotifyPipTerminated(long pipId, IEnumerable<ReportedProcess> survivingChildProcesses)
        {
            // TODO: bundle this into a single message
            var distinctProcessIds = new HashSet<uint>(survivingChildProcesses.Select(p => p.ProcessId));
            foreach (var processId in distinctProcessIds)
            {
                SandboxConnection.NotifyPipProcessTerminated(pipId, (int)processId);
            }
        }

        private async Task FeedStdInAsync(SandboxedProcessInfo info, [CanBeNull] string processStdinFileName)
        {
            string redirectedStdin      = processStdinFileName != null ? $" < {processStdinFileName}" : string.Empty;
            string escapedArguments     = info.Arguments.Replace(Environment.NewLine, "\\" + Environment.NewLine);

            string line = I($"exec {info.FileName} {escapedArguments} {redirectedStdin}");

            LogProcessState("Feeding stdin: " + line);
            await Process.StandardInput.WriteLineAsync(line);
            Process.StandardInput.Close();
        }

        internal override void FeedStdErr(SandboxedProcessOutputBuilder builder, string line)
        {
            FeedOutputBuilder(builder, line);
        }

        /// <nodoc />
        [NotNull]
        internal override CpuTimes GetCpuTimes()
        {
            return !MeasureCpuTime
                ? base.GetCpuTimes()
                : new CpuTimes(
                    user: TimeSpan.FromMilliseconds(m_perfAggregator.UserTimeMs.Latest),
                    system: TimeSpan.FromMilliseconds(m_perfAggregator.KernelTimeMs.Latest));
        }

        // <inheritdoc />
        internal override JobObject.AccountingInformation GetJobAccountingInfo()
        {
            if (!MeasureCpuTime)
            {
                return base.GetJobAccountingInfo();
            }
            else
            {
                IOCounters ioCounters;
                ProcessMemoryCounters memoryCounters;

                try
                {
                    ioCounters = new IOCounters(new IO_COUNTERS()
                    {
                        ReadOperationCount = 1,
                        WriteOperationCount = 1,
                        ReadTransferCount = Convert.ToUInt64(m_perfAggregator.DiskBytesRead.Total),
                        WriteTransferCount = Convert.ToUInt64(m_perfAggregator.DiskBytesWritten.Total)
                    });

                    memoryCounters = new ProcessMemoryCounters(0, Convert.ToUInt64(m_perfAggregator.PeakMemoryBytes.Maximum), 0);
                }
                catch(OverflowException ex)
                {
                    LogProcessState($"Overflow exception caught while calculating AccountingInformation:{Environment.NewLine}{ex.ToString()}");

                    ioCounters = new IOCounters(new IO_COUNTERS()
                    {
                        ReadOperationCount = 0,
                        WriteOperationCount = 0,
                        ReadTransferCount = 0,
                        WriteTransferCount = 0
                    });

                    memoryCounters = new ProcessMemoryCounters(0, 0, 0);
                }

                return new JobObject.AccountingInformation
                {
                    IO = ioCounters,
                    MemoryCounters = memoryCounters,
                    KernelTime = TimeSpan.FromMilliseconds(m_perfAggregator.JobKernelTimeMs.Latest),
                    UserTime = TimeSpan.FromMilliseconds(m_perfAggregator.JobUserTimeMs.Latest),
                };
            }
        }

        private void ReportProcessCreated()
        {
            var report = new Sandbox.AccessReport
            {
                Operation      = FileOperation.OpProcessStart,
                Pid            = Process.Id,
                PipId          = PipId,
                PathOrPipStats = Sandbox.AccessReport.EncodePath(Process.StartInfo.FileName),
                Status         = (uint)FileAccessStatus.Allowed
            };

            ReportFileAccess(ref report);
        }

        /// <summary>
        /// This function checks for timeout conditions and once met, times out a process tree. On normal process execution the sandbox kernel extension
        /// sends a process tree completion event, which we use to cleanup and flag an execution as successful. It also indicates that the root process and all
        /// of its children have exited and are no longer running. There are several edge-cases to consider if this is not the case though:
        /// 1) The sandbox kernel extension report queue stops receiving events in user-space while processes are still executing in the build engine, thus the
        ///    process tree complete event nor any other signal is ever received. This case is handled by comparing the time since the last event was received on any queue
        ///    to m_reportQueueProcessTimeout and timing out a process complete tree if that timeout threshold has been reached.
        /// 2) If a root processes has exited and its process exit event has been dequeued from the report queue, a child timeout has to be scheduled. This makes sure that
        ///    all children have enough time to execute as they could potentially outlive their parent prior to terminating them. To prevent indefinite waiting,
        ///    there is a NestedProcessTerminationTimeout defined which defaults to 30s or a value passed in by the frontend and setup by a user. Once we know the root process
        ///    has exited and other access reports in the queues are older than that event, we can safely schedule a process tree timeout with the aforementioned value.
        /// </summary>
        /// <returns>Task that signals if a process tree should be timed out</returns>
        private Task ProcessTreeTimeoutTask()
        {
            var processTreeTimeoutSource = new TaskCompletionSource<Unit>();

            Task.Run(async () =>
            {
                while (true)
                {
                    if (m_timeoutTaskCancelationSource.IsCancellationRequested)
                    {
                        break;
                    }

                    var minEnqueueTime = SandboxConnection.MinReportQueueEnqueueTime;

                    // Ensure we time out if the sandbox stops sending any events within ReportQueueProcessTimeout
                    if (minEnqueueTime != 0 &&
                        SandboxConnection.CurrentDrought >= ReportQueueProcessTimeout &&
                        CurrentRunningTime() >= ReportQueueProcessTimeout)
                    {
                        LogProcessState("Process timed out due to inactivity from sandbox kernel extension report queue: " +
                                        $"the process has been running for {CurrentRunningTime().TotalSeconds} seconds " +
                                        $"and no reports have been received for over {ReportQueueProcessTimeout.TotalSeconds} seconds!");

                        m_pendingReports.Complete();
                        m_survivingChildProcesses = CoalesceProcesses(GetCurrentlyActiveChildProcesses());

                        await KillAsync();
                        processTreeTimeoutSource.SetResult(Unit.Void);
                        break;
                    }

                    if (HasProcessExitBeenReceived && !Killed)
                    {
                        // Proceed if the event queue is beyond the point when ProcessExit was received
                        if (m_processExitTimeNs <= minEnqueueTime)
                        {
                            bool shouldWait = ShouldWaitForSurvivingChildProcesses();
                            if (shouldWait)
                            {
                                await Task.Delay(ChildProcessTimeout);
                            }

                            LogProcessState($"Process timed out because nested process termination timeout limit was reached.");
                            m_survivingChildProcesses = CoalesceProcesses(GetCurrentlyActiveChildProcesses());
                            processTreeTimeoutSource.SetResult(Unit.Void);
                            break;
                        }
                        else
                        {
                            LogProcessState($"Process exited but still waiting for reports :: exit time = {m_processExitTimeNs}, " +
                                $"min enqueue time = {minEnqueueTime}, current drought = {SandboxConnection.CurrentDrought.TotalMilliseconds}ms");
                        }
                    }

                    await Task.Delay(SandboxConnection.IsInTestMode ? 5 : 250);
                }
            }, m_timeoutTaskCancelationSource.Token).IgnoreErrors();

            return processTreeTimeoutSource.Task.IgnoreErrors();
        }

        private void UpdateAverageTimeSpentInReportQueue(AccessReportStatistics stats)
        {
            m_sumOfReportCreationTimesUs += (long) (stats.EnqueueTime - stats.CreationTime) / 1000;
            m_sumOfReportQueueTimesUs += (long) (stats.DequeueTime - stats.EnqueueTime) / 1000;
        }

        private void HandleAccessReport(AccessReport report)
        {
            if (ShouldReportFileAccesses)
            {
                LogProcessState("Access report received: " + AccessReportToString(report));
            }

            Counters.IncrementCounter(SandboxedProcessCounters.AccessReportCount);
            using (Counters.StartStopwatch(SandboxedProcessCounters.HandleAccessReportDuration))
            {
                UpdateAverageTimeSpentInReportQueue(report.Statistics);

                // caching path existence checks would speed things up, but it would not be semantically sound w.r.t. our unit tests
                // TODO: check if the tests are overspecified because in practice BuildXL doesn't really rely much on the outcome of this check
                var reportPath = report.DecodePath();

                // Set the process exit time once we receive it from the sandbox kernel extension report queue
                if (report.Operation == FileOperation.OpProcessExit && report.Pid == Process.Id)
                {
                    m_processExitTimeNs = report.Statistics.EnqueueTime;
                }

                var pathExists = true;

                // special handling for MAC_LOOKUP:
                //   - don't report for existent paths (because for those paths other reports will follow)
                //   - otherwise, set report.RequestAccess to Probe (because the Sandbox reports 'Lookup', but hat BXL expects 'Probe'),
                if (report.Operation == FileOperation.OpMacLookup)
                {
                    pathExists = FileUtilities.Exists(reportPath);
                    if (pathExists)
                    {
                        return;
                    }
                    else
                    {
                        report.RequestedAccess = (uint)RequestedAccess.Probe;
                    }
                }
                // special handling for directory rename:
                //   - scenario: a pip writes a bunch of files into a directory (e.g., 'out.tmp') and then renames that directory (e.g., to 'out')
                //   - up to this point we know about the writes into the 'out.tmp' directory
                //   - once 'out.tmp' is renamed to 'out', we need to explicitly update all previously reported paths under 'out.tmp'
                //       - since we cannot rewrite the past and directly mutate previously reported paths, we simply enumerate
                //         the content of the renamed directory and report all the files in there as writes
                //       - (this is exactly how this is done on Windows, except that it's implemented in the Detours layer)
                else if (report.Operation == FileOperation.OpKAuthMoveDest &&
                         report.Status == (uint)FileAccessStatus.Allowed &&
                         FileUtilities.DirectoryExistsNoFollow(reportPath))
                {
                    FileUtilities.EnumerateFiles(
                        directoryPath: reportPath,
                        recursive: true,
                        pattern: "*",
                        (dir, fileName, attrs, length) =>
                        {
                            AccessReport reportClone = report;
                            reportClone.Operation = FileOperation.OpKAuthWriteFile;
                            reportClone.PathOrPipStats = AccessReport.EncodePath(Path.Combine(dir, fileName));
                            ReportFileAccess(ref reportClone);
                        });
                }

                // our sandbox kernel extension currently doesn't detect file existence, so do it here instead
                if (report.Error == 0 && !pathExists)
                {
                    report.Error = ReportedFileAccess.ERROR_PATH_NOT_FOUND;
                }

                if (report.Operation == FileOperation.OpProcessTreeCompleted)
                {
                    m_pipKextStats = report.DecodePipKextStats();
                    m_pendingReports.Complete();
                }
                else
                {
                    ReportFileAccess(ref report);
                }
            }
        }

        private void ReportFileAccess(ref AccessReport report)
        {
            if (ReportsCompleted())
            {
                return;
            }

            if (IgnoreReportedAccesses &&
                report.Operation != FileOperation.OpProcessStart &&
                report.Operation != FileOperation.OpProcessExit)
            {
                return;
            }

            m_reports.ReportFileAccess(ref report, ReportProvider);
        }

        /// <summary>
        /// If <see cref="SandboxedProcessInfo.StandardInputReader"/> is set, it flushes the content of that reader
        /// to a file in the process's working directory and returns the absolute path of that file; otherwise returns null.
        /// </summary>
        private async Task<string> FlushStandardInputToFileIfNeededAsync(SandboxedProcessInfo info)
        {
            if (info.StandardInputReader == null)
            {
                return null;
            }

            string stdinFileName = Path.Combine(Process.StartInfo.WorkingDirectory, info.PipSemiStableHash + ".stdin");
            string stdinText = await info.StandardInputReader.ReadToEndAsync();
            Encoding encoding = info.StandardInputEncoding ?? Console.InputEncoding;
            byte[] stdinBytes = encoding.GetBytes(stdinText);
            bool stdinFileWritten = await FileUtilities.WriteAllBytesAsync(stdinFileName, stdinBytes);
            if (!stdinFileWritten)
            {
                ThrowCouldNotStartProcess($"failed to flush standard input to file '{stdinFileName}'");
            }

            // Allow read from the created stdin file
            info.FileAccessManifest.AddPath(AbsolutePath.Create(PathTable, stdinFileName), mask: FileAccessPolicy.MaskNothing, values: FileAccessPolicy.AllowRead);

            return stdinFileName;
        }

        private static readonly int s_maxFileAccessStatus = Enum.GetValues(typeof(FileAccessStatus)).Cast<FileAccessStatus>().Max(e => (int)e);
        private static readonly int s_maxRequestedAccess = Enum.GetValues(typeof(RequestedAccess)).Cast<RequestedAccess>().Max(e => (int)e);

        private bool ReportProvider(
            ref AccessReport report, out uint processId, out ReportedFileOperation operation, out RequestedAccess requestedAccess, out FileAccessStatus status,
            out bool explicitlyReported, out uint error, out Usn usn, out DesiredAccess desiredAccess, out ShareMode shareMode, out CreationDisposition creationDisposition,
            out FlagsAndAttributes flagsAndAttributes, out AbsolutePath manifestPath, out string path, out string enumeratePattern, out string processArgs, out string errorMessage)
        {
            var errorMessages = new List<string>();
            checked
            {
                processId = (uint)report.Pid;

                if (!SandboxedProcessReports.FileAccessReportLine.Operations.TryGetValue(report.DecodeOperation(), out operation))
                {
                    errorMessages.Add($"Unknown operation '{report.DecodeOperation()}'");
                }

                requestedAccess = (RequestedAccess)report.RequestedAccess;
                if (report.RequestedAccess > s_maxRequestedAccess)
                {
                    errorMessages.Add($"Illegal value for 'RequestedAccess': {requestedAccess}; maximum allowed: {(int)RequestedAccess.All}");
                }

                status = (FileAccessStatus)report.Status;
                if (report.Status > s_maxFileAccessStatus)
                {
                    errorMessages.Add($"Illegal value for 'Status': {status}");
                }

                bool isWrite = (report.RequestedAccess & (byte)RequestedAccess.Write) != 0;

                explicitlyReported  = report.ExplicitLogging > 0;
                error               = report.Error;
                usn                 = ReportedFileAccess.NoUsn;
                desiredAccess       = isWrite ? DesiredAccess.GENERIC_WRITE : DesiredAccess.GENERIC_READ;
                shareMode           = ShareMode.FILE_SHARE_READ;
                creationDisposition = CreationDisposition.OPEN_ALWAYS;
                flagsAndAttributes  = 0;
                path                = report.DecodePath();
                enumeratePattern    = string.Empty;
                processArgs         = string.Empty;

                AbsolutePath.TryCreate(PathTable, path, out manifestPath);

                errorMessage = errorMessages.Any()
                    ? $"Illegal access report: '{AccessReportToString(report)}' :: {string.Join(";", errorMessages)}"
                    : string.Empty;

                return errorMessage == string.Empty;
            }
        }

        internal static string AccessReportToString(AccessReport report)
        {
            var operation       = report.DecodeOperation();
            var pid             = report.Pid.ToString("X");
            var requestedAccess = report.RequestedAccess;
            var status          = report.Status;
            var explicitLogging = report.ExplicitLogging != 0 ? 1 : 0;
            var error           = report.Error;
            var path            = report.DecodePath();
            var processTime     = (long)(report.Statistics.EnqueueTime - report.Statistics.CreationTime) / 1000;
            var queueTime       = (long)(report.Statistics.DequeueTime - report.Statistics.EnqueueTime) / 1000;

            return
                I($"{operation}:{pid}|{requestedAccess}|{status}|{explicitLogging}|{error}|{path}|e:{report.Statistics.EnqueueTime}|h:{processTime}us|q:{queueTime}us");
        }
    }
}
