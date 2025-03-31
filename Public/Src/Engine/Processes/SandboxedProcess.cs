// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Native.IO;
using BuildXL.Native.Streams;
using BuildXL.Processes.Internal;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core.Tasks;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.Windows.Memory;

#if !FEATURE_SAFE_PROCESS_HANDLE
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
#endif

#nullable enable

namespace BuildXL.Processes
{
    /// <summary>
    /// A process abstraction for BuildXL that can monitor environment interactions, in particular file accesses, that ensures
    /// that all file accesses are on an allowlist.
    /// Under the hood, the sandboxed process uses Detours.
    /// </summary>
    /// <remarks>
    /// All public static and instance methods of this class are thread safe.
    /// </remarks>
    public sealed class SandboxedProcess : ISandboxedProcess
    {
        private static BinaryPaths? s_binaryPaths;
        private static bool s_setMaxWorkingSetToPeakBeforeResume = false;
        private static readonly Guid s_payloadGuid = new("7CFDBB96-C3D6-47CD-9026-8FA863C52FEC");
        private static readonly UIntPtr s_defaultMin = new(204800);

        private readonly int m_bufferSize;

        private readonly PooledObjectWrapper<MemoryStream> m_fileAccessManifestStreamWrapper;
        private MemoryStream FileAccessManifestStream => m_fileAccessManifestStreamWrapper.Instance;
        private readonly FileAccessManifest m_fileAccessManifest;

        private readonly TaskSourceSlim<SandboxedProcessResult> m_resultTaskCompletionSource =
            TaskSourceSlim.Create<SandboxedProcessResult>();

        private readonly TextReader? m_standardInputReader;
        private readonly TimeSpan m_nestedProcessTerminationTimeout;
        private readonly LoggingContext m_loggingContext;
        private DetouredProcess? m_detouredProcess;
        private bool m_processStarted;
        private readonly SandboxedProcessOutputBuilder m_error;
        private readonly SandboxedProcessOutputBuilder m_output;
        private readonly SandboxedProcessTraceBuilder? m_traceBuilder;
        private readonly SandboxedProcessReports m_reports;
        private IAsyncPipeReader? m_reportReader;
        private readonly SemaphoreSlim m_reportReaderSemaphore = TaskUtilities.CreateMutex();
        private Dictionary<uint, ReportedProcess>? m_survivingChildProcesses;
        private readonly uint m_timeoutMins;
        private TaskSourceSlim<bool> m_standardInputTcs;
        private readonly PathTable m_pathTable;
        private readonly string[]? m_allowedSurvivingChildProcessNames;
        private readonly string? m_survivingPipProcessChildrenDumpDirectory;
        private readonly int m_numRetriesPipeReadOnCancel;

        private readonly Aggregation m_peakWorkingSet = new();
        private readonly Aggregation m_workingSet = new();

        internal SandboxedProcess(SandboxedProcessInfo info)
        {
            Contract.Requires(!info.Timeout.HasValue || info.Timeout.Value >= TimeSpan.Zero);
            Contract.Requires(!info.CreateSandboxTraceFile
                || (info.FileAccessManifest.ReportFileAccesses && info.FileAccessManifest.LogProcessData && info.FileAccessManifest.ReportProcessArgs),
                "Trace file is enabled, but some of the required options in the file access manifest are not.");

            // there could be a race here, but it just doesn't matter
            s_binaryPaths ??= new BinaryPaths(); // this can take a while; performs I/O

            // If unspecified make the injection timeout the DefaultProcessTimeoutInMinutes. Also, make it no less than DefaultProcessTimeoutInMinutes.
            m_timeoutMins = info.Timeout.HasValue ? ((uint)info.Timeout.Value.TotalMinutes) : Defaults.ProcessTimeoutInMinutes;
            if (m_timeoutMins < Defaults.ProcessTimeoutInMinutes)
            {
                m_timeoutMins = Defaults.ProcessTimeoutInMinutes;
            }

            m_fileAccessManifest = info.FileAccessManifest;
            m_fileAccessManifestStreamWrapper = Pools.MemoryStreamPool.GetInstance();
            m_bufferSize = SandboxedProcessInfo.BufferSize;
            m_allowedSurvivingChildProcessNames = info.AllowedSurvivingChildProcessNames;
            m_nestedProcessTerminationTimeout = info.NestedProcessTerminationTimeout;
            m_loggingContext = info.LoggingContext;
            m_survivingPipProcessChildrenDumpDirectory = info.SurvivingPipProcessChildrenDumpDirectory;
            m_numRetriesPipeReadOnCancel = info.NumRetriesPipeReadOnCancel;

            Encoding inputEncoding = info.StandardInputEncoding ?? Console.InputEncoding;
            m_standardInputReader = info.StandardInputReader;

            m_pathTable = info.PathTable;

            Encoding outputEncoding = info.StandardOutputEncoding ?? Console.OutputEncoding;
            m_output = new SandboxedProcessOutputBuilder(
                    outputEncoding,
                    info.MaxLengthInMemory,
                    info.FileStorage,
                    SandboxedProcessFile.StandardOutput,
                    info.StandardOutputObserver);
            Encoding errorEncoding = info.StandardErrorEncoding ?? Console.OutputEncoding;
            m_error = new SandboxedProcessOutputBuilder(
                    errorEncoding,
                    info.MaxLengthInMemory,
                    info.FileStorage,
                    SandboxedProcessFile.StandardError,
                    info.StandardErrorObserver);

            m_traceBuilder = info.CreateSandboxTraceFile
                ? new SandboxedProcessTraceBuilder(info.FileStorage, info.PathTable)
                : null;

            m_reports = new SandboxedProcessReports(
                    m_fileAccessManifest,
                    info.PathTable,
                    info.PipSemiStableHash,
                    info.PipDescription,
                    info.LoggingContext,
                    info.FileName,
                    info.DetoursEventListener,
                    info.SidebandWriter,
                    info.FileSystemView,
                    m_traceBuilder);

            m_detouredProcess =
                new DetouredProcess(
                    SandboxedProcessInfo.BufferSize,
                    info.GetCommandLine(),
                    info.WorkingDirectory,
                    info.GetUnicodeEnvironmentBlock(),
                    inputEncoding,
                    errorEncoding,
                    m_error.HookOutputStream ? m_error.AppendLine : null,
                    outputEncoding,
                    m_output.HookOutputStream ? m_output.AppendLine : null,
                    OnProcessExitingAsync,
                    OnProcessExitedAsync,
                    info.Timeout,
                    info.DisableConHostSharing,
                    info.LoggingContext,
                    info.TimeoutDumpDirectory,
                    // If there is any process configured to breakway from the sandbox, then we need to allow
                    // this to happen at the job object level
                    setJobBreakawayOk: m_fileAccessManifest.ProcessesCanBreakaway,
                    info.CreateJobObjectForCurrentProcess,
                    info.DiagnosticsEnabled,
                    m_numRetriesPipeReadOnCancel,
                    DebugReport,
                    info.ExternallyProvidedJobObject);
        }

        /// <inheritdoc />
        public string GetAccessedFileName(ReportedFileAccess reportedFileAccess) => reportedFileAccess.GetPath(m_pathTable);

        /// <inheritdoc />
        public int GetLastMessageCount() => m_reports.GetLastMessageCount();

        /// <inheritdoc />
        public int GetLastConfirmedMessageCount() => m_reports.GetLastConfirmedMessageCount();

        /// <inheritdoc />
        public int ProcessId { get; private set; }

        /// <inheritdoc />
        public Task<SandboxedProcessResult> GetResultAsync() => m_resultTaskCompletionSource.Task;

        /// <inheritdoc />
        public EmptyWorkingSetResult TryEmptyWorkingSet(bool isSuspend)
        {
            if (!IsDetouredProcessUsable)
            {
                return EmptyWorkingSetResult.None;
            }

            var result = EmptyWorkingSetResult.Success;
            var suspendSuccess = true;
            var emptyWorkingSetSuccess = true;

            if (isSuspend)
            {
                // We start measuring here to avoid being considered timed-out while we are doing the visitation below
                m_detouredProcess?.StartMeasuringSuspensionTime();
            }
            var visitResult = TryVisitJobObjectProcesses((processHandle, pid) =>
            {
                emptyWorkingSetSuccess &= Interop.Windows.Memory.EmptyWorkingSet(processHandle.DangerousGetHandle());

                if (isSuspend)
                {
                    try
                    {
                        suspendSuccess &= Interop.Windows.Process.Suspend(System.Diagnostics.Process.GetProcessById((int)pid));
                    }
                    catch (Exception e)
                    {
                        suspendSuccess = false;
                        Tracing.Logger.Log.ResumeOrSuspendException(m_loggingContext, "Suspend", e.ToStringDemystified());
                    }
                }
            });

            if (visitResult == VisitJobObjectResult.TerminatedBeforeVisitation)
            {
                // We tried to race with process termination and lost
                return EmptyWorkingSetResult.None;
            }

            if (visitResult == VisitJobObjectResult.Failed)
            {
                result |= EmptyWorkingSetResult.EmptyWorkingSetFailed;

                if (isSuspend)
                {
                    result |= EmptyWorkingSetResult.SuspendFailed;
                }

                return result;
            }

            if (!emptyWorkingSetSuccess)
            {
                result |= EmptyWorkingSetResult.EmptyWorkingSetFailed;
            }

            if (isSuspend && !suspendSuccess)
            {
                m_detouredProcess?.StopMeasuringSuspensionTime(); // Not very important as we will be cancelled after this failure
                result |= EmptyWorkingSetResult.SuspendFailed;
            }

            return result;
        }

        /// <inheritdoc />
        public bool TryResumeProcess()
        {
            if (!IsDetouredProcessUsable)
            {
                return false;
            }

            var success = true;
            var visitResult = TryVisitJobObjectProcesses((processHandle, pid) =>
            {
                ulong peakWorkingSet = 0;
                if (s_setMaxWorkingSetToPeakBeforeResume)
                {
                    // If maxLimitMultiplier is not zero, retrieve the memory counters before empty the working set.
                    // Those memory counters will be used when setting the maxworkingsetsize of the process.
                    var memoryUsage = GetMemoryUsageCounters(processHandle.DangerousGetHandle());
                    peakWorkingSet = memoryUsage?.PeakWorkingSetSize ?? 0;
                }

                if (peakWorkingSet != 0)
                {
                    SetProcessWorkingSetSizeEx(
                        processHandle.DangerousGetHandle(),
                        s_defaultMin, // the default on systems with 4k pages
                        new UIntPtr(peakWorkingSet),
                        WorkingSetSizeFlags.MaxEnable | WorkingSetSizeFlags.MinDisable);
                }

                try
                {
                    success &= Interop.Windows.Process.Resume(System.Diagnostics.Process.GetProcessById((int)pid));
                }
                catch (Exception e)
                {
                    Tracing.Logger.Log.ResumeOrSuspendException(m_loggingContext, "Resume", e.ToStringDemystified());
                    success = false;
                }
            });

            // Whether or not there's failure, stop measuring suspension time.
            m_detouredProcess?.StopMeasuringSuspensionTime();

            return (visitResult != VisitJobObjectResult.Failed) && success;
        }

        /// <inheritdoc />
        public ProcessMemoryCountersSnapshot? GetMemoryCountersSnapshot()
        {
            try
            {
                if (!IsDetouredProcessUsable)
                {
                    return null;
                }

                ulong lastPeakWorkingSet = 0;
                ulong lastWorkingSet = 0;
                bool isCollectedData = false;

                var visitResult = TryVisitJobObjectProcesses((processHandle, _) =>
                {
                    var memoryUsage = Interop.Windows.Memory.GetMemoryUsageCounters(processHandle.DangerousGetHandle());
                    if (memoryUsage != null)
                    {
                        isCollectedData = true;
                        lastPeakWorkingSet += memoryUsage.PeakWorkingSetSize;
                        lastWorkingSet += memoryUsage.WorkingSetSize;
                    }
                });

                if (visitResult != VisitJobObjectResult.Success)
                {
                    return null;
                }

                if (isCollectedData)
                {
                    m_peakWorkingSet.RegisterSample(lastPeakWorkingSet);
                    m_workingSet.RegisterSample(lastWorkingSet);
                }

                return ProcessMemoryCountersSnapshot.CreateFromBytes(
                    lastPeakWorkingSet,
                    lastWorkingSet,
                    Convert.ToUInt64(m_workingSet.Average));
            }
            catch (NullReferenceException)
            {
                // Somewhere above there is a NRE but the stack doesn't match a line number and there is no obvious bug.
                // Removed BuildXL.Tracing.UnexpectedCondition.Log() call because BuildXL.Tracing reference is removed from this project.
                return null;
            }
        }

        private bool IsDetouredProcessUsable => m_detouredProcess != null && m_detouredProcess.IsRunning;

        private VisitJobObjectResult TryVisitJobObjectProcesses(Action<SafeProcessHandle, uint> actionForProcess)
        {
            // Callers of this method check IsDetouredProcessUsable before calling,
            // but there is technically a chance that we are disposed (so m_detouredProcess is null)
            // between that check and this call. In that case we just return TerminatedBeforeVisitation.
            return m_detouredProcess?.TryVisitJobObjectProcesses(actionForProcess) ?? VisitJobObjectResult.TerminatedBeforeVisitation;
        }

        /// <inheritdoc />
        public long GetDetoursMaxHeapSize()
        {
            return m_reports.MaxDetoursHeapSize;
        }

        /// <summary>
        /// Disposes this instance
        /// </summary>
        /// <remarks>
        /// This may block on process clean-up, if a process is still running at Dispose-time. This ensures
        /// that all pipes and outstanding I/O are finished when Dispose returns.
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_detouredProcess")]
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_reportReader")]
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_reportReaderSemaphore")]
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_error")]
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_output")]
        public void Dispose()
        {
            if (m_processStarted)
            {
                InternalKill();
                m_resultTaskCompletionSource.Task.Wait();
            }

            m_detouredProcess?.Dispose();
            m_detouredProcess = null;

            m_output.Dispose();
            m_error.Dispose();

            m_fileAccessManifestStreamWrapper.Dispose();
        }

        /// <summary>
        /// Kill sandboxed process
        /// </summary>
        /// <remarks>
        /// Also kills all nested processes; if the process hasn't already finished by itself, the Result task gets canceled.
        /// </remarks>
        private void InternalKill()
        {
            Contract.Assume(m_processStarted);

            // this will cause OnProcessExited to be invoked, which will in turn call SetResult(...).
            // This allows Dispose to wait on process teardown.
            m_detouredProcess?.Kill(ExitCodes.Killed);
        }

        /// <inheritdoc/>
        public Task KillAsync()
        {
            InternalKill();
            return GetResultAsync(); // ignore result, we just want to wait until process has terminated
        }

        /// <inheritdoc />
        /// <remarks>
        /// <exception cref="BuildXLException">Thrown if creating or detouring the process fails.</exception>
        /// </remarks>
        [SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods",
            MessageId = "System.Runtime.InteropServices.SafeHandle.DangerousGetHandle", Justification = "We need to get the file handle.")]
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Objects are embedded in other objects.")]
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public void Start()
        {
            Contract.Assume(!m_processStarted);

            Encoding reportEncoding = Encoding.Unicode;
            SafeFileHandle? childHandle = null;
            DetouredProcess detouredProcess = m_detouredProcess!;

            bool useManagedPipeReader = !PipeReaderFactory.ShouldUseLegacyPipeReader();

            using (m_reportReaderSemaphore.AcquireSemaphore())
            {
                NamedPipeServerStream? pipeStream = null;
                SafeFileHandle? reportHandle = null;

                try
                {
                    if (useManagedPipeReader)
                    {
                        pipeStream = Pipes.CreateNamedPipeServerStream(
                            PipeDirection.In,
                            PipeOptions.Asynchronous,
                            PipeOptions.None,
                            out childHandle);
                    }
                    else
                    {
                        Pipes.CreateInheritablePipe(
                            Pipes.PipeInheritance.InheritWrite,
                            Pipes.PipeFlags.ReadSideAsync,
                            readHandle: out reportHandle,
                            writeHandle: out childHandle);
                    }

                    var setup = new FileAccessSetup
                    {
                        ReportPath = "#" + childHandle.DangerousGetHandle().ToInt64(),
                        DllNameX64 = s_binaryPaths!.DllNameX64,
                        DllNameX86 = s_binaryPaths!.DllNameX86,
                    };

                    bool debugFlagsMatch = true;
                    var manifestBytes = new ArraySegment<byte>();
                    manifestBytes = m_fileAccessManifest.GetPayloadBytes(m_loggingContext, setup, FileAccessManifestStream, m_timeoutMins, ref debugFlagsMatch);
                    if (!debugFlagsMatch)
                    {
                        throw new BuildXLException("Mismatching build type for BuildXL and DetoursServices.dll.");
                    }

                    m_standardInputTcs = TaskSourceSlim.Create<bool>();
                    detouredProcess.Start(
                        s_payloadGuid,
                        manifestBytes,
                        childHandle,
                        s_binaryPaths!.DllNameX64,
                        s_binaryPaths!.DllNameX86);

                    // At this point, we believe calling 'kill' will result in an eventual callback for job teardown.
                    // This knowledge is significant for ensuring correct cleanup if we did vs. did not start a process;
                    // if started, we expect teardown to happen eventually and clean everything up.
                    m_processStarted = true;

                    ProcessId = detouredProcess.GetProcessId();
                }
                catch (AccessViolationException)
                {
                    int ramPercent = 0, availableRamMb = 0, availablePageFileMb = 0, totalPageFileMb = 0;

                    var memoryStatusEx = new MEMORYSTATUSEX();
                    if (GlobalMemoryStatusEx(memoryStatusEx))
                    {
                        ramPercent = (int)memoryStatusEx.dwMemoryLoad;
                        availableRamMb = new OperatingSystemHelper.FileSize(memoryStatusEx.ullAvailPhys).MB;
                        availablePageFileMb = new OperatingSystemHelper.FileSize(memoryStatusEx.ullAvailPageFile).MB;
                        totalPageFileMb = new OperatingSystemHelper.FileSize(memoryStatusEx.ullTotalPageFile).MB;
                    }

                    string memUsage = $"RamPercent: {ramPercent}, AvailableRamMb: {availableRamMb}, AvailablePageFileMb: {availablePageFileMb}, TotalPageFileMb: {totalPageFileMb}";
                    Native.Tracing.Logger.Log.DetouredProcessAccessViolationException(m_loggingContext, m_reports.PipDescription + " - " + memUsage);
                    throw;
                }
                finally
                {
                    // Note that in the success path, childHandle should already be closed (by Start).
                    if (childHandle != null && !childHandle.IsInvalid)
                    {
                        childHandle.Dispose();
                    }
                }

                StreamDataReceived reportLineReceivedCallback = ReportLineReceived;

                if (useManagedPipeReader)
                {
                    m_reportReader = PipeReaderFactory.CreateManagedPipeReader(
                        pipeStream,
                        message => reportLineReceivedCallback(message),
                        reportEncoding,
                        m_bufferSize);
                }
                else
                {
                    var reportFile = AsyncFileFactory.CreateAsyncFile(
                        reportHandle,
                        FileDesiredAccess.GenericRead,
                        ownsHandle: true,
                        kind: FileKind.Pipe);
                    m_reportReader = new AsyncPipeReader(
                        reportFile,
                        reportLineReceivedCallback,
                        reportEncoding,
                        m_bufferSize,
                        numOfRetriesOnCancel: m_numRetriesPipeReadOnCancel,
                        debugPipeReporter: new AsyncPipeReader.DebugReporter(errorMsg => DebugReport($"ReportReader: {errorMsg}")));
                }

                m_reportReader.BeginReadLine();
            }

            // don't wait, we want feeding in of standard input to happen asynchronously
            Analysis.IgnoreResult(FeedStandardInputAsync(detouredProcess, m_standardInputReader, m_standardInputTcs));
        }

        private bool ReportLineReceived(string data)
        {
            SandboxedProcessFactory.Counters.IncrementCounter(SandboxedProcessFactory.SandboxedProcessCounters.AccessReportCount);
            using (SandboxedProcessFactory.Counters.StartStopwatch(SandboxedProcessFactory.SandboxedProcessCounters.HandleAccessReportDuration))
            {
                return m_reports.ReportLineReceived(data);
            }
        }

        private void DebugReport(string data) => m_reports.ReportLineReceived($"{(int)ReportType.DebugMessage},{data}");

        private static async Task FeedStandardInputAsync(DetouredProcess detouredProcess, TextReader? reader, TaskSourceSlim<bool> stdInTcs)
        {
            try
            {
                // We always have a redirected handle, and we always close it eventually (even if we have no actual input to feed)
                if (reader != null)
                {
                    while (true)
                    {
                        string? line = await reader.ReadLineAsync();
                        if (line == null)
                        {
                            break;
                        }

                        await detouredProcess.WriteStandardInputLineAsync(line);
                    }
                }

                detouredProcess.CloseStandardInput();
                stdInTcs.TrySetResult(true);
            }
            catch (Exception e)
            {
                stdInTcs.TrySetException(e);
            }
        }

        private async Task WaitUntilReportEofAsync(bool cancel)
        {
            using (await m_reportReaderSemaphore.AcquireAsync())
            {
                if (m_reportReader != null)
                {
                    await m_reportReader.CompletionAsync(!cancel);

                    m_reportReader.Dispose();
                    m_reportReader = null;
                }
            }
        }

        private async Task OnProcessExitingAsync()
        {
            JobObject? jobObject = m_detouredProcess?.GetJobObject();
            if (jobObject != null)
            {
                if (ShouldWaitForSurvivingChildProcesses(jobObject))
                {
                    // If there are any remaining child processes, we wait for a moment.
                    // This should be a rare case, and even if we have to wait it should typically only be for a fraction of a second, so we simply wait synchronously (blocking the current thread).
                    if (!(await jobObject.WaitAsync(m_loggingContext, m_nestedProcessTerminationTimeout)))
                    {
                        HandleSurvivingChildProcesses(jobObject);
                    }
                }
                else
                {
                    HandleSurvivingChildProcesses(jobObject);
                }
            }
        }

        private async Task OnProcessExitedAsync()
        {
            // Wait until all incoming report messages from the detoured process have been handled.
            await WaitUntilReportEofAsync(m_detouredProcess!.Killed);

            // Ensure no further modifications to the report
            m_reports.Freeze();

            // We can get extended accounting information (peak memory, etc. rolled up for the entire process tree) if this process was wrapped in a job.
            JobObject.AccountingInformation? jobAccountingInformation = null;
            JobObject? jobObject = m_detouredProcess.GetJobObject();

            if (jobObject != null)
            {
                var accountingInfo = jobObject.GetAccountingInformation();

                // Only overwrite memory counters if <see cref="GetMemoryCountersSnapshot"/> did get triggered previously. This isn't the case if the
                // detours sandbox is used outside of BuildXL (currently only the scheduler calls this). The <see cref="JobObject.GetAccountingInformation"/>
                // function does populate memory counters for the process tree if possible, so don't overwrite them with empty aggregator values.
                if (m_peakWorkingSet.Count > 0 || m_workingSet.Count > 0)
                {
                    accountingInfo.MemoryCounters = Pips.ProcessMemoryCounters.CreateFromBytes(
                        peakWorkingSet: Convert.ToUInt64(m_peakWorkingSet.Maximum),
                        averageWorkingSet: Convert.ToUInt64(m_workingSet.Average));
                }

                jobAccountingInformation = accountingInfo;
            }

            ProcessTimes primaryProcessTimes = m_detouredProcess.GetTimesForPrimaryProcess();

            IOException? standardInputException = null;

            try
            {
                await m_standardInputTcs.Task;
            }
            catch (IOException ex)
            {
                standardInputException = ex;
            }

            // Construct result; note that the process is expected to have exited at this point, even if we decided to forcefully kill it
            // (this callback is always a result of the process handle being signaled).
            int exitCode;
            if (m_reports.MessageProcessingFailure != null && !m_fileAccessManifest.DisableDetours)
            {
                exitCode = ExitCodes.MessageProcessingFailure;
            }
            else
            {
                Contract.Assert(m_detouredProcess.HasExited, "Detoured process has not been marked as exited");
                exitCode = m_detouredProcess.GetExitCode();
            }

            var result = new SandboxedProcessResult
            {
                // If there is a message parsing failure, fail the pip.
                ExitCode = exitCode,
                Killed = m_detouredProcess.Killed,
                TimedOut = m_detouredProcess.TimedOut,
                HasDetoursInjectionFailures = m_detouredProcess.HasDetoursInjectionFailures,
                SurvivingChildProcesses = m_survivingChildProcesses?.Values.ToArray(),
                PrimaryProcessTimes = primaryProcessTimes,
                JobAccountingInformation = jobAccountingInformation,
                StandardOutput = m_output.Freeze(),
                StandardError = m_error.Freeze(),
                TraceFile = m_traceBuilder?.Freeze(),
                AllUnexpectedFileAccesses = m_reports.FileUnexpectedAccesses,
                FileAccesses = m_reports.FileAccesses,
                DetouringStatuses = m_reports.ProcessDetoursStatuses,
                ExplicitlyReportedFileAccesses = m_reports.ExplicitlyReportedFileAccesses,
                Processes = m_reports.Processes,
                DumpFileDirectory = m_detouredProcess.DumpFileDirectory,
                DumpCreationException = m_detouredProcess.DumpCreationException,
                StandardInputException = standardInputException,
                MessageProcessingFailure = m_reports.MessageProcessingFailure,
                ProcessStartTime = m_detouredProcess.StartTime,
                HasReadWriteToReadFileAccessRequest = m_reports.HasReadWriteToReadFileAccessRequest,
                DiagnosticMessage = m_detouredProcess.Diagnostics
            };

            SetResult(result);
        }

        private bool ShouldWaitForSurvivingChildProcesses(JobObject jobObject)
        {
            if (m_allowedSurvivingChildProcessNames == null || m_allowedSurvivingChildProcessNames.Length == 0)
            {
                // Wait for surviving child processes if no allowable process names are explicitly specified.
                return true;
            }

            Dictionary<uint, ReportedProcess>? survivingChildProcesses = GetSurvivingChildProcesses(jobObject, shouldDumpProcess: false);

            if (survivingChildProcesses != null && survivingChildProcesses.Count > 0)
            {
                foreach (string processPath in survivingChildProcesses.Select(kvp => kvp.Value.Path))
                {
                    bool allowed = false;

                    foreach (string allowedProcessName in m_allowedSurvivingChildProcessNames)
                    {
                        if (string.Equals(Path.GetFileName(processPath), allowedProcessName, OperatingSystemHelper.PathComparison))
                        {
                            allowed = true;
                            break;
                        }
                    }

                    if (!allowed)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private Dictionary<uint, ReportedProcess>? GetSurvivingChildProcesses(JobObject jobObject, bool shouldDumpProcess = false) =>
            JobObjectProcessDumper.GetAndOptionallyDumpProcesses(
                jobObject: jobObject,
                loggingContext: m_loggingContext,
                survivingPipProcessDumpDirectory: m_survivingPipProcessChildrenDumpDirectory,
                dumpProcess: shouldDumpProcess,
                excludedDumpProcessNames: m_allowedSurvivingChildProcessNames ?? [],
                out Exception? _);

        private void HandleSurvivingChildProcesses(JobObject jobObject)
        {
            m_survivingChildProcesses = GetSurvivingChildProcesses(jobObject, shouldDumpProcess: true);

            if (m_survivingChildProcesses != null && m_survivingChildProcesses.Count > 0)
            {
                m_detouredProcess!.Kill(ExitCodes.KilledSurviving);
            }
        }

        private void SetResult(SandboxedProcessResult result)
        {
            Contract.Requires(m_detouredProcess != null);

            m_resultTaskCompletionSource.SetResult(result);
        }

        internal static void SetMaxWorkingSetToPeakBeforeResume(bool setPeak) => s_setMaxWorkingSetToPeakBeforeResume = setPeak;

        /// <summary>
        /// Start a sandboxed process asynchronously. The result will only be available once the process terminates.
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if the process creation fails in a recoverable manner due do some obscure problem detected by the underlying
        /// ProcessCreate call.
        /// </exception>
        public static async Task<SandboxedProcess> StartAsync(SandboxedProcessInfo info)
        {
            var process = await SandboxedProcessFactory.StartAsync(info, forceSandboxing: true);
            return (SandboxedProcess)process;
        }
    }
}
