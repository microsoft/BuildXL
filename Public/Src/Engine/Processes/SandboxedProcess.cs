// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Native.Streams;
using BuildXL.Pips.Operations;
using BuildXL.Processes.Internal;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.Windows.Memory;
using static BuildXL.Utilities.OperatingSystemHelper;
#if !FEATURE_SAFE_PROCESS_HANDLE
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
#endif

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
        private const int MaxProcessPathLength = 1024;
        private static BinaryPaths s_binaryPaths;
        private static readonly Guid s_payloadGuid = new Guid("7CFDBB96-C3D6-47CD-9026-8FA863C52FEC");
        private static readonly UIntPtr s_defaultMin = new UIntPtr(204800);

        private readonly int m_bufferSize;

        private readonly PooledObjectWrapper<MemoryStream> m_fileAccessManifestStreamWrapper;
        private MemoryStream FileAccessManifestStream => m_fileAccessManifestStreamWrapper.Instance;
        private FileAccessManifest m_fileAccessManifest;

        private readonly TaskSourceSlim<SandboxedProcessResult> m_resultTaskCompletionSource =
            TaskSourceSlim.Create<SandboxedProcessResult>();

        private readonly TextReader m_standardInputReader;
        private readonly TimeSpan m_nestedProcessTerminationTimeout;
        private readonly LoggingContext m_loggingContext;
        private DetouredProcess m_detouredProcess;
        private bool m_processStarted;
        private SandboxedProcessOutputBuilder m_error;
        private SandboxedProcessOutputBuilder m_output;
        private readonly SandboxedProcessTraceBuilder m_traceBuilder;
        private SandboxedProcessReports m_reports;
        private IAsyncPipeReader m_reportReader;
        private readonly SemaphoreSlim m_reportReaderSemaphore = TaskUtilities.CreateMutex();
        private Dictionary<uint, ReportedProcess> m_survivingChildProcesses;
        private readonly uint m_timeoutMins;
        private TaskSourceSlim<bool> m_standardInputTcs;
        private readonly PathTable m_pathTable;
        private readonly string[] m_allowedSurvivingChildProcessNames;
        private readonly string m_survivingPipProcessChildrenDumpDirectory;
        private readonly int m_numRetriesPipeReadOnCancel;

        private readonly PerformanceCollector.Aggregation m_peakWorkingSet = new PerformanceCollector.Aggregation();
        private readonly PerformanceCollector.Aggregation m_workingSet = new PerformanceCollector.Aggregation();

        private readonly PerformanceCollector.Aggregation m_peakCommitSize = new PerformanceCollector.Aggregation();
        private readonly PerformanceCollector.Aggregation m_commitSize = new PerformanceCollector.Aggregation();

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "We own these objects.")]
        internal SandboxedProcess(SandboxedProcessInfo info)
        {
            Contract.Requires(info != null);
            Contract.Requires(!info.Timeout.HasValue || info.Timeout.Value <= Process.MaxTimeout);
            Contract.Requires(!info.CreateSandboxTraceFile 
                || info.FileAccessManifest.ReportFileAccesses && info.FileAccessManifest.LogProcessData && info.FileAccessManifest.ReportProcessArgs, 
                "Trace file is enabled, but some of the required options in the file access manifest are not.");

            // there could be a race here, but it just doesn't matter
            if (s_binaryPaths == null)
            {
                s_binaryPaths = new BinaryPaths(); // this can take a while; performs I/O
            }

            // If unspecified make the injection timeout the DefaultProcessTimeoutInMinutes. Also, make it no less than DefaultProcessTimeoutInMinutes.
            m_timeoutMins = info.Timeout.HasValue ? ((uint)info.Timeout.Value.TotalMinutes) : SandboxConfiguration.DefaultProcessTimeoutInMinutes;
            if (m_timeoutMins < SandboxConfiguration.DefaultProcessTimeoutInMinutes)
            {
                m_timeoutMins = SandboxConfiguration.DefaultProcessTimeoutInMinutes;
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

            m_reports = m_fileAccessManifest != null ?
                new SandboxedProcessReports(
                    m_fileAccessManifest,
                    info.PathTable,
                    info.PipSemiStableHash,
                    info.PipDescription,
                    info.LoggingContext,
                    info.DetoursEventListener,
                    info.SidebandWriter,
                    info.FileSystemView,
                    m_traceBuilder) : null;

            Contract.Assume(inputEncoding != null);
            Contract.Assert(errorEncoding != null);
            Contract.Assert(outputEncoding != null);

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
                    info.ContainerConfiguration,
                    // If there is any process configured to breakway from the sandbox, then we need to allow
                    // this to happen at the job object level
                    setJobBreakawayOk: m_fileAccessManifest.ProcessesCanBreakaway,
                    info.CreateJobObjectForCurrentProcess,
                    info.DiagnosticsEnabled,
                    m_numRetriesPipeReadOnCancel,
                    DebugPipeConnection);
        }

        /// <inheritdoc />
        public string GetAccessedFileName(ReportedFileAccess reportedFileAccess)
        {
            return reportedFileAccess.GetPath(m_pathTable);
        }

        /// <inheritdoc />
        public int GetLastMessageCount()
        {
            if (m_reports == null)
            {
                return 0; // We didn't count the messages.
            }

            return m_reports.GetLastMessageCount();
        }

        /// <inheritdoc />
        public int ProcessId { get; private set; }

        /// <inheritdoc />
        public async Task<SandboxedProcessResult> GetResultAsync()
        {
            SandboxedProcessResult result = await m_resultTaskCompletionSource.Task;

            // await yield to make sure we are not blocking the thread that called us
            await Task.Yield();
            return result;
        }

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
                m_detouredProcess.StartMeasuringSuspensionTime();
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
                if (EngineEnvironmentSettings.SetMaxWorkingSetToPeakBeforeResume)
                {
                    // If maxLimitMultiplier is not zero, retrieve the memory counters before empty the working set.
                    // Those memory counters will be used when setting the maxworkingsetsize of the process.
                    var memoryUsage = Interop.Windows.Memory.GetMemoryUsageCounters(processHandle.DangerousGetHandle());
                    peakWorkingSet = memoryUsage?.PeakWorkingSetSize ?? 0;
                }

                if (peakWorkingSet != 0)
                {
                    Interop.Windows.Memory.SetProcessWorkingSetSizeEx(
                        processHandle.DangerousGetHandle(),
                        s_defaultMin, // the default on systems with 4k pages
                        new UIntPtr(peakWorkingSet),
                        Interop.Windows.Memory.WorkingSetSizeFlags.MaxEnable | Interop.Windows.Memory.WorkingSetSizeFlags.MinDisable);
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
                ulong lastPeakCommitSize = 0;
                ulong lastWorkingSet = 0;
                ulong lastCommitSize = 0;
                bool isCollectedData = false;

                var visitResult = TryVisitJobObjectProcesses((processHandle, _) =>
                {
                    var memoryUsage = Interop.Windows.Memory.GetMemoryUsageCounters(processHandle.DangerousGetHandle());
                    if (memoryUsage != null)
                    {
                        isCollectedData = true;
                        lastPeakWorkingSet += memoryUsage.PeakWorkingSetSize;
                        lastPeakCommitSize += memoryUsage.PeakPagefileUsage;
                        lastWorkingSet += memoryUsage.WorkingSetSize;
                        lastCommitSize += memoryUsage.PagefileUsage;
                    }
                });

                if (visitResult != VisitJobObjectResult.Success)
                {
                    return null;
                }

                if (isCollectedData)
                {
                    m_peakWorkingSet.RegisterSample(lastPeakWorkingSet);
                    m_peakCommitSize.RegisterSample(lastPeakCommitSize);

                    m_workingSet.RegisterSample(lastWorkingSet);
                    m_commitSize.RegisterSample(lastCommitSize);
                }

                return ProcessMemoryCountersSnapshot.CreateFromBytes(
                    lastPeakWorkingSet,
                    lastWorkingSet,
                    Convert.ToUInt64(m_workingSet.Average),
                    lastPeakCommitSize,
                    lastCommitSize);
            }
            catch (NullReferenceException ex)
            {
                // Somewhere above there is a NRE but the stack doesn't match a line number and there is no obvious bug.
                BuildXL.Tracing.UnexpectedCondition.Log(m_loggingContext, "Null reference exception when collecting MemoryCountersSnapshot: " + ex.ToString());
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
            if (m_reports != null)
            {
                return m_reports.MaxDetoursHeapSize;
            }

            return 0L;
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

            m_output?.Dispose();
            m_output = null;

            m_error?.Dispose();
            m_error = null;

            m_reports = null;

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
            SafeFileHandle childHandle = null;
            DetouredProcess detouredProcess = m_detouredProcess;

            bool useManagedPipeReader = !PipeReaderFactory.ShouldUseLegacyPipeReader();

            using (m_reportReaderSemaphore.AcquireSemaphore())
            {
                NamedPipeServerStream pipeStream = null;
                SafeFileHandle reportHandle = null;

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
                        DllNameX64 = s_binaryPaths.DllNameX64,
                        DllNameX86 = s_binaryPaths.DllNameX86,
                    };

                    bool debugFlagsMatch = true;
                    ArraySegment<byte> manifestBytes = new ArraySegment<byte>();
                    if (m_fileAccessManifest != null)
                    {
                        manifestBytes = m_fileAccessManifest.GetPayloadBytes(m_loggingContext, setup, FileAccessManifestStream, m_timeoutMins, ref debugFlagsMatch);
                    }

                    if (!debugFlagsMatch)
                    {
                        throw new BuildXLException("Mismatching build type for BuildXL and DetoursServices.dll.");
                    }

                    m_standardInputTcs = TaskSourceSlim.Create<bool>();
                    detouredProcess.Start(
                        s_payloadGuid,
                        manifestBytes,
                        childHandle,
                        s_binaryPaths.DllNameX64,
                        s_binaryPaths.DllNameX86);

                    // At this point, we believe calling 'kill' will result in an eventual callback for job teardown.
                    // This knowledge is significant for ensuring correct cleanup if we did vs. did not start a process;
                    // if started, we expect teardown to happen eventually and clean everything up.
                    m_processStarted = true;

                    ProcessId = detouredProcess.GetProcessId();
                }
                catch (AccessViolationException)
                {
                    int ramPercent = 0, availableRamMb = 0, availablePageFileMb = 0, totalPageFileMb = 0;

                    MEMORYSTATUSEX memoryStatusEx = new MEMORYSTATUSEX();
                    if (GlobalMemoryStatusEx(memoryStatusEx))
                    {
                        ramPercent = (int)memoryStatusEx.dwMemoryLoad;
                        availableRamMb = new FileSize(memoryStatusEx.ullAvailPhys).MB;
                        availablePageFileMb = new FileSize(memoryStatusEx.ullAvailPageFile).MB;
                        totalPageFileMb = new FileSize(memoryStatusEx.ullTotalPageFile).MB;
                    }

                    string memUsage = $"RamPercent: {ramPercent}, AvailableRamMb: {availableRamMb}, AvailablePageFileMb: {availablePageFileMb}, TotalPageFileMb: {totalPageFileMb}";
                    Native.Tracing.Logger.Log.DetouredProcessAccessViolationException(m_loggingContext, (m_reports?.PipDescription ?? "") + " - " + memUsage);
                    throw;
                }
                finally
                {
                    // release memory
                    m_fileAccessManifest = null;

                    // Note that in the success path, childHandle should already be closed (by Start).
                    if (childHandle != null && !childHandle.IsInvalid)
                    {
                        childHandle.Dispose();
                    }
                }

                StreamDataReceived reportLineReceivedCallback = m_reports == null ? null : ReportLineReceived;

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
                        debugPipeReporter: new AsyncPipeReader.DebugReporter(errorMsg => DebugPipeConnection($"ReportReader: {errorMsg}")));
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

        private void DebugPipeConnection(string data) => m_reports.ReportLineReceived($"{(int)ReportType.DebugMessage},{data}");

        private static async Task FeedStandardInputAsync(DetouredProcess detouredProcess, TextReader reader, TaskSourceSlim<bool> stdInTcs)
        {
            try
            {
                // We always have a redirected handle, and we always close it eventually (even if we have no actual input to feed)
                if (reader != null)
                {
                    while (true)
                    {
                        string line = await reader.ReadLineAsync();

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

        private async Task WaitUntilReportEof(bool cancel)
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
            JobObject jobObject = m_detouredProcess.GetJobObject();
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
            await WaitUntilReportEof(m_detouredProcess.Killed);

            // Ensure no further modifications to the report
            m_reports?.Freeze();

            // We can get extended accounting information (peak memory, etc. rolled up for the entire process tree) if this process was wrapped in a job.
            JobObject.AccountingInformation? jobAccountingInformation = null;
            JobObject jobObject = m_detouredProcess.GetJobObject();

            if (jobObject != null)
            {
                var accountingInfo = jobObject.GetAccountingInformation();

                // Only overwrite memory counters if <see cref="GetMemoryCountersSnapshot"/> did get triggered previously. This isn't the case if the
                // detours sandbox is used outside of BuildXL (currently only the scheduler calls this). The <see cref="JobObject.GetAccountingInformation"/>
                // function does populate memory counters for the process tree if possible, so don't overwrite them with empty aggregator values.
                if (m_peakWorkingSet.Count > 0 || m_workingSet.Count > 0 || m_peakCommitSize.Count > 0 || m_commitSize.Count > 0)
                {
                    accountingInfo.MemoryCounters = Pips.ProcessMemoryCounters.CreateFromBytes(
                        peakWorkingSet: Convert.ToUInt64(m_peakWorkingSet.Maximum),
                        averageWorkingSet: Convert.ToUInt64(m_workingSet.Average),
                        peakCommitSize: Convert.ToUInt64(m_peakCommitSize.Maximum),
                        averageCommitSize: Convert.ToUInt64(m_commitSize.Average));
                }

                jobAccountingInformation = accountingInfo;
            }

            ProcessTimes primaryProcessTimes = m_detouredProcess.GetTimesForPrimaryProcess();

            IOException standardInputException = null;

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
            int exitCode = 0;
            if (m_reports?.MessageProcessingFailure != null)
            {
                exitCode = ExitCodes.MessageProcessingFailure;
            }
            else
            {
                Contract.Assert(m_detouredProcess.HasExited, "Detoured process has not been marked as exited");
                exitCode = m_detouredProcess.GetExitCode();
            }

            SandboxedProcessResult result =
                new SandboxedProcessResult
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
                    AllUnexpectedFileAccesses = m_reports?.FileUnexpectedAccesses,
                    FileAccesses = m_reports?.FileAccesses,
                    DetouringStatuses = m_reports?.ProcessDetoursStatuses,
                    ExplicitlyReportedFileAccesses = m_reports?.ExplicitlyReportedFileAccesses,
                    Processes = m_reports?.Processes,
                    DumpFileDirectory = m_detouredProcess.DumpFileDirectory,
                    DumpCreationException = m_detouredProcess.DumpCreationException,
                    StandardInputException = standardInputException,
                    MessageProcessingFailure = m_reports?.MessageProcessingFailure,
                    ProcessStartTime = m_detouredProcess.StartTime,
                    HasReadWriteToReadFileAccessRequest = m_reports?.HasReadWriteToReadFileAccessRequest ?? false,
                    DiagnosticMessage = m_detouredProcess.Diagnostics
                };

            SetResult(result);
        }

        private Dictionary<uint, ReportedProcess> GetSurvivingChildProcesses(JobObject jobObject)
        {
            if (!jobObject.TryGetProcessIds(m_loggingContext, out uint[] survivingChildProcessIds) || survivingChildProcessIds.Length == 0)
            {
                return null;
            }

            var survivingChildProcesses = new Dictionary<uint, ReportedProcess>();

            foreach (uint processId in survivingChildProcessIds)
            {
                using (SafeProcessHandle processHandle = ProcessUtilities.OpenProcess(
                    ProcessSecurityAndAccessRights.PROCESS_QUERY_INFORMATION |
                    ProcessSecurityAndAccessRights.PROCESS_VM_READ,
                    false,
                    processId))
                {
                    if (!jobObject.ContainsProcess(processHandle))
                    {
                        // We are too late: process handle is invalid because it closed already,
                        // or process id got reused by another process.
                        continue;
                    }

                    if (!ProcessUtilities.GetExitCodeProcess(processHandle, out int exitCode))
                    {
                        // we are too late: process id got reused by another process
                        continue;
                    }

                    using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
                    {
                        StringBuilder sb = wrap.Instance;
                        if (sb.Capacity < MaxProcessPathLength)
                        {
                            sb.Capacity = MaxProcessPathLength;
                        }

                        if (ProcessUtilities.GetModuleFileNameEx(processHandle, IntPtr.Zero, sb, (uint)sb.Capacity) <= 0)
                        {
                            // we are probably too late
                            continue;
                        }

                        // Attempt to read the process arguments (command line) from the process
                        // memory. This is not fatal if it does not succeed.
                        string processArgs = string.Empty;

                        var basicInfoSize = (uint)Marshal.SizeOf<Native.Processes.Windows.ProcessUtilitiesWin.PROCESS_BASIC_INFORMATION>();
                        var basicInfoPtr = Marshal.AllocHGlobal((int)basicInfoSize);
                        uint basicInfoReadLen;
                        try
                        {
                            if (Native.Processes.Windows.ProcessUtilitiesWin.NtQueryInformationProcess(
                                processHandle,
                                Native.Processes.Windows.ProcessUtilitiesWin.ProcessInformationClass.ProcessBasicInformation,
                                basicInfoPtr, basicInfoSize, out basicInfoReadLen) == 0)
                            {
                                Native.Processes.Windows.ProcessUtilitiesWin.PROCESS_BASIC_INFORMATION basicInformation = Marshal.PtrToStructure<Native.Processes.Windows.ProcessUtilitiesWin.PROCESS_BASIC_INFORMATION>(basicInfoPtr);
                                Contract.Assert(basicInformation.UniqueProcessId == processId);

                                // NativeMethods.ReadProcessStructure and NativeMethods.ReadUnicodeString handle null\zero addresses
                                // passed into them. Since these are all value types, then there is no need to do any type
                                // of checking as passing zero through will just result in an empty process args string.
                                var peb = Native.Processes.Windows.ProcessUtilitiesWin.ReadProcessStructure<Native.Processes.Windows.ProcessUtilitiesWin.PEB>(processHandle, basicInformation.PebBaseAddress);
                                var processParameters = Native.Processes.Windows.ProcessUtilitiesWin.ReadProcessStructure<Native.Processes.Windows.ProcessUtilitiesWin.RTL_USER_PROCESS_PARAMETERS>(processHandle, peb.ProcessParameters);
                                processArgs = Native.Processes.Windows.ProcessUtilitiesWin.ReadProcessUnicodeString(processHandle, processParameters.CommandLine);
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(basicInfoPtr);
                        }

                        string path = sb.ToString();
                        survivingChildProcesses.Add(processId, new ReportedProcess(processId, path, processArgs));
                        DumpSurvivingPipProcessChildren((int)processId);
                    }
                }
            }

            return survivingChildProcesses;
        }

        private void DumpSurvivingPipProcessChildren(int processId)
        {
            if (m_survivingPipProcessChildrenDumpDirectory == null)
            {
                Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(m_loggingContext, processId.ToString(), $"Failed due to missing SurvivingPipProcessChildrenDumpDirectory.");
                return;
            }

            if (TryGetProcessById(processId, out var childProcess))
            {
                string dumpPath = Path.Combine(m_survivingPipProcessChildrenDumpDirectory, $"SurvivingChildProcess_{processId}.zip");
                if (!ProcessDumper.TryDumpProcess(childProcess, dumpPath, out Exception dumpException, compress: true))
                {
                    Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(m_loggingContext, childProcess.ProcessName, $"Failed with Exception: {dumpException?.Message}");
                }
                else
                {
                    Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(m_loggingContext, childProcess.ProcessName, $"Succeeded at path: {dumpPath}");
                }
            }
        }

        private bool TryGetProcessById(int pid, out System.Diagnostics.Process process)
        {
            process = null;
            try
            {
                process = System.Diagnostics.Process.GetProcessById(pid);
                // Process.GetProcessById returns an object that is not fully initialized. Instead, the fields are populated the first time they are accessed.
                // Because of that if a process exits before the object is initialized, reading any of the properties will result in an InvalidOperationException
                // exception. Force initialization be querying the process name. Local function is used here just to make sure that the compiler does not
                // optimize away the property access.
                doNothing(process.ProcessName);
                return true;
            }
            catch (ArgumentException aEx)
            {
                Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(m_loggingContext, pid.ToString(), $"Failed with Exception: {aEx.Message}");
            }
            catch (InvalidOperationException ioEx)
            {
                Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(m_loggingContext, pid.ToString(), $"Failed with Exception: {ioEx.Message}");
            }
            return false;

            void doNothing(string _)
            {
                // no-op
            }
        }

        private bool ShouldWaitForSurvivingChildProcesses(JobObject jobObject)
        {
            Contract.Requires(jobObject != null);

            if (m_allowedSurvivingChildProcessNames == null || m_allowedSurvivingChildProcessNames.Length == 0)
            {
                // Wait for surviving child processes if no allowable process names are explicitly specified.
                return true;
            }

            Dictionary<uint, ReportedProcess> survivingChildProcesses = GetSurvivingChildProcesses(jobObject);

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

        private void HandleSurvivingChildProcesses(JobObject jobObject)
        {
            m_survivingChildProcesses = GetSurvivingChildProcesses(jobObject);

            if (m_survivingChildProcesses != null && m_survivingChildProcesses.Count > 0)
            {
                m_detouredProcess.Kill(ExitCodes.KilledSurviving);
            }
        }

        private void SetResult(SandboxedProcessResult result)
        {
            Contract.Requires(m_detouredProcess != null);

            m_resultTaskCompletionSource.SetResult(result);
        }

        /// <summary>
        /// Start a sandboxed process asynchronously. The result will only be available once the process terminates.
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if the process creation fails in a recoverable manner due do some obscure problem detected by the underlying
        /// ProcessCreate call.
        /// </exception>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Object lives on via task result.")]
        public static async Task<SandboxedProcess> StartAsync(SandboxedProcessInfo info)
        {
            var process = await SandboxedProcessFactory.StartAsync(info, forceSandboxing: true);
            return (SandboxedProcess)process;
        }
    }
}
