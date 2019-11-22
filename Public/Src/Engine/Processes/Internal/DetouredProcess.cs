// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Native.Streams;
using BuildXL.Pips.Operations;
using BuildXL.Processes.Containers;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Diagnostics;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Microsoft.Win32.SafeHandles;
#if FEATURE_SAFE_PROCESS_HANDLE
using SafeProcessHandle = Microsoft.Win32.SafeHandles.SafeProcessHandle;
#else
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
#endif

namespace BuildXL.Processes.Internal
{
    /// <summary>
    /// This class implements a managed abstraction of a detoured process creation
    /// </summary>
    /// <remarks>
    /// All public methods of this class are thread safe.
    /// </remarks>
    internal sealed class DetouredProcess : IDisposable
    {
        private readonly SemaphoreSlim m_syncSemaphore = TaskUtilities.CreateMutex();
        private readonly int m_bufferSize;
        private readonly string m_commandLine;
        private readonly StreamDataReceived m_errorDataReceived;
        private readonly StreamDataReceived m_outputDataReceived;
        private readonly Func<Task> m_processExitingAsync;
        private readonly Func<Task> m_processExited;
        private readonly Encoding m_standardErrorEncoding;
        private readonly Encoding m_standardInputEncoding;
        private readonly Encoding m_standardOutputEncoding;
        private readonly byte[] m_unicodeEnvironmentBlock;
        private readonly string m_workingDirectory;
        private bool m_disposed;
        private AsyncPipeReader m_errorReader;
        private JobObject m_job;
        private bool m_killed;
        private bool m_timedout;
        private bool m_hasDetoursFailures;
        private AsyncPipeReader m_outputReader;
        private SafeProcessHandle m_processHandle;
        private int m_processId;
        private SafeWaitHandleFromSafeHandle m_processWaitHandle;
        private RegisteredWaitHandle m_registeredWaitHandle;
        private StreamWriter m_standardInputWriter;
        private bool m_waiting;
        private bool m_starting;
        private bool m_started;
        private bool m_exited;
        private readonly TimeSpan? m_timeout;
        private ProcessTreeContext m_processInjector;
        private readonly bool m_disableConHostSharing;
        private long m_startUpTime;
        private readonly string m_timeoutDumpDirectory;
        [SuppressMessage("Microsoft.Reliability", "CA2006:UseSafeHandleToEncapsulateNativeResources")]
        private static readonly IntPtr s_consoleWindow = Native.Processes.Windows.ProcessUtilitiesWin.GetConsoleWindow();
        private readonly ContainerConfiguration m_containerConfiguration;
        private readonly bool m_setJobBreakawayOk;
        private readonly LoggingContext m_loggingContext;

#region public getters

        /// <summary>
        /// Whether this process has started. Once true, it will never become false.
        /// </summary>
        public bool HasStarted => Volatile.Read(ref m_started);

        /// <summary>
        /// Whether this process has exited. Once true, it will never become false. Implies <code>HasStarted</code>.
        /// </summary>
        public bool HasExited => Volatile.Read(ref m_exited);

        /// <summary>
        /// Retrieves the process id associated with this process.
        /// </summary>
        /// <remarks>
        /// This method can only be invoked after the process has started.
        /// </remarks>
        public int GetProcessId()
        {
            Contract.Requires(HasStarted);
            return Volatile.Read(ref m_processId);
        }

        /// <summary>
        /// Retrieves the job object associated with this process, if any.
        /// </summary>
        /// <remarks>
        /// This method can only be invoked after the process has started.
        /// </remarks>
        /// <returns>
        /// Result is null after this instance has been disposed, or if the OS doesn't supported monitoring nested
        /// processes in jobs.
        /// </returns>
        public JobObject GetJobObject()
        {
            Contract.Requires(HasStarted);
            return Volatile.Read(ref m_job);
        }

        /// <summary>
        /// Retrieves whether this process has exceeded its specified timeout.
        /// </summary>
        /// <remarks>
        /// This method can only be invoked after the process has started.
        /// </remarks>
        public bool TimedOut
        {
            get
            {
                Contract.Requires(HasStarted);
                return Volatile.Read(ref m_timedout);
            }
        }

        /// <summary>
        /// Retrieves whether an attempt was made to kill this process or a nested child process.
        /// </summary>
        /// <remarks>
        /// This method can only be invoked after the process has started.
        /// </remarks>
        public bool Killed
        {
            get
            {
                Contract.Requires(HasStarted);
                return Volatile.Read(ref m_killed);
            }
        }

        /// <summary>
        /// Retrieves whether there are failures in the detouring code.
        /// </summary>
        /// <remarks>
        /// This method can only be invoked after the process has started.
        /// </remarks>
        public bool HasDetoursInjectionFailures
        {
            get
            {
                Contract.Requires(HasStarted);
                return Volatile.Read(ref m_hasDetoursFailures);
            }
        }

        /// <summary>
        /// Path of the memory dump created if a process times out. This may be null if the process did not time out
        /// or if capturing the dump failed
        /// </summary>
        public string DumpFileDirectory { get; private set; }

        /// <summary>
        /// Exception describing why creating a memory dump may have failed.
        /// </summary>
        public Exception DumpCreationException { get; private set; }

        /// <summary>
        /// Tries to kill the process and all child processes.
        /// </summary>
        /// <remarks>
        /// It's okay to call this method at any time; however, before process start and after process termination or disposing of
        /// this instance, it does nothing.
        /// </remarks>
        public void Kill(int exitCode)
        {
            // Notify the injected that the process is being killed
            m_processInjector?.OnKilled();

            var processHandle = m_processHandle;
            if (processHandle != null && !processHandle.IsInvalid)
            {
                // Ignore result, as there is a race with regular process termination that we cannot do anything about.
                m_killed = true;

                // No job object means that we are on an old OS; let's just terminate this process (we can't reliably terminate all child processes)
                Analysis.IgnoreResult(Native.Processes.ProcessUtilities.TerminateProcess(processHandle, exitCode));
            }

            JobObject jobObject = m_job;
            if (jobObject != null)
            {
                // Ignore result, as there is a race with regular shutdown.
                m_killed = true;

                Analysis.IgnoreResult(jobObject.Terminate(exitCode));
            }
        }

        /// <summary>
        /// Gets the process exit code.
        /// </summary>
        /// <remarks>
        /// This method can only be invoked after the process has exited.");
        /// The result is undefined if this instance has been disposed.
        /// </remarks>
        public int GetExitCode()
        {
            Contract.Requires(HasExited);
            using (m_syncSemaphore.AcquireSemaphore())
            {
                if (m_disposed)
                {
                    return -1;
                }

                int exitCode;
                if (!Native.Processes.ProcessUtilities.GetExitCodeProcess(m_processHandle, out exitCode))
                {
                    throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "Unable to get exit code.");
                }

                return exitCode;
            }
        }

        /// <summary>
        /// Gets process time information (start, exit, user-time, etc.) for the primary process. This accounts for the process started
        /// directly but not for any child processes.
        /// </summary>
        /// <remarks>
        /// This method can only be invoked after the process has started but before disposal.
        /// Note that aggregate accounting is available by querying the wrapping job object, if present;
        /// see <see cref="GetJobObject"/>.
        /// </remarks>
        public ProcessTimes GetTimesForPrimaryProcess()
        {
            Contract.Requires(HasStarted);
            using (m_syncSemaphore.AcquireSemaphore())
            {
                Contract.Assume(!m_disposed);
                Contract.Assume(m_processHandle != null, "Process not yet started.");

                long creation, exit, kernel, user;
                if (!Native.Processes.ProcessUtilities.GetProcessTimes(m_processHandle.DangerousGetHandle(), out creation, out exit, out kernel, out user))
                {
                    throw new NativeWin32Exception(Marshal.GetLastWin32Error(), "Unable to get times.");
                }

                return new ProcessTimes(creation: creation, exit: exit, kernel: kernel, user: user);
            }
        }

        /// <summary>
        /// Writes to standard input.
        /// </summary>
        /// <remarks>
        /// If the process has exited or this instance has been disposed, then this method does nothing.
        /// </remarks>
        public async Task WriteStandardInputLineAsync(string line)
        {
            Contract.Requires(HasStarted);
            using (await m_syncSemaphore.AcquireAsync())
            {
                if (m_standardInputWriter != null)
                {
                    await m_standardInputWriter.WriteLineAsync(line);
                }
            }
        }

        /// <summary>
        /// Closes the standard input.
        /// </summary>
        /// <remarks>
        /// If the process has exited or this instance has been disposed, then this method does nothing.
        /// </remarks>
        public void CloseStandardInput()
        {
            Contract.Requires(HasStarted);
            using (var releaser = m_syncSemaphore.AcquireSemaphore())
            {
                InternalCloseStandardInput(releaser);
            }
        }

#endregion

        public DetouredProcess(
            int bufferSize,
            string commandLine,
            string workingDirectory,
            byte[] unicodeEnvironmentBlock,
            Encoding standardInputEncoding,
            Encoding standardErrorEncoding,
            StreamDataReceived errorDataReceived,
            Encoding standardOutputEncoding,
            StreamDataReceived outputDataReceived,
            Func<Task> processExitingAsync,
            Func<Task> processExited,
            TimeSpan? timeout,
            bool disableConHostSharing,
            LoggingContext loggingContext,
            string timeoutDumpDirectory,
            ContainerConfiguration containerConfiguration,
            bool setJobBreakawayOk)
        {
            Contract.Requires(bufferSize >= 128);
            Contract.Requires(!string.IsNullOrEmpty(commandLine));
            Contract.Requires(standardInputEncoding != null);
            Contract.Requires(standardErrorEncoding != null);
            Contract.Requires(standardOutputEncoding != null);
            Contract.Requires(!timeout.HasValue || timeout.Value <= Process.MaxTimeout);

            m_bufferSize = bufferSize;
            m_commandLine = commandLine;
            m_workingDirectory = workingDirectory;
            m_unicodeEnvironmentBlock = unicodeEnvironmentBlock;
            m_standardInputEncoding = standardInputEncoding;
            m_standardErrorEncoding = standardErrorEncoding;
            m_errorDataReceived = errorDataReceived;
            m_standardOutputEncoding = standardOutputEncoding;
            m_outputDataReceived = outputDataReceived;
            m_processExitingAsync = processExitingAsync;
            m_processExited = processExited;
            m_timeout = timeout;
            m_disableConHostSharing = disableConHostSharing;
            m_containerConfiguration = containerConfiguration;
            m_setJobBreakawayOk = setJobBreakawayOk;
            if (m_workingDirectory != null && m_workingDirectory.Length == 0)
            {
                m_workingDirectory = Directory.GetCurrentDirectory();
            }

            m_loggingContext = loggingContext;
            m_timeoutDumpDirectory = timeoutDumpDirectory;
        }

        /// <summary>
        /// Starts the process. An <paramref name="inheritableReportHandle"/> may be provided, in which case
        /// that handle will be inherited to the new process.
        /// </summary>
        /// <remarks>
        /// Start may be only called once on an instance, and not after this instance was disposed.
        /// A provided <paramref name="inheritableReportHandle"/> will be closed after process creation
        /// (since it should then be owned by the child process).
        /// </remarks>
        /// <exception cref="BuildXLException">Thrown if creating or detouring the process fails.</exception>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public void Start(
            Guid payloadGuid,
            ArraySegment<byte> payloadData,
            SafeFileHandle inheritableReportHandle,
            string dllNameX64,
            string dllNameX86)
        {
            using (m_syncSemaphore.AcquireSemaphore())
            {
                if (m_starting || m_disposed)
                {
                    throw new InvalidOperationException("Cannot invoke start process more than once or after this instance has been Disposed.");
                }

                m_starting = true;

                // The process creation flags
                // We use CREATE_DEFAULT_ERROR_MODE to ensure that the hard error mode of the child process (i.e., GetErrorMode)
                // is deterministic. Inheriting error mode is the default, but there may be some concurrent operation that temporarily
                // changes it (process global). The CLR has been observed to do so.
                // We use CREATE_NO_WINDOW in case BuildXL is attached to a console windows to prevent child processes from messing up
                // the console window. If BuildXL itself is started without a console window the flag is not set to prevent creating
                // extra conhost.exe processes.
                int creationFlags =
                    ((s_consoleWindow == IntPtr.Zero && !m_disableConHostSharing) ?
                        0 : Native.Processes.ProcessUtilities.CREATE_NO_WINDOW) | Native.Processes.ProcessUtilities.CREATE_DEFAULT_ERROR_MODE;

                SafeFileHandle standardInputWritePipeHandle = null;
                SafeFileHandle standardOutputReadPipeHandle = null;
                SafeFileHandle standardErrorReadPipeHandle = null;

                try
                {
                    // set up the environment block parameter
                    var environmentHandle = default(GCHandle);
                    var payloadHandle = default(GCHandle);

                    SafeFileHandle hStdInput = null;
                    SafeFileHandle hStdOutput = null;
                    SafeFileHandle hStdError = null;
                    SafeThreadHandle threadHandle = null;
                    try
                    {
                        IntPtr environmentPtr = IntPtr.Zero;
                        if (m_unicodeEnvironmentBlock != null)
                        {
                            creationFlags |= Native.Processes.ProcessUtilities.CREATE_UNICODE_ENVIRONMENT;
                            environmentHandle = GCHandle.Alloc(m_unicodeEnvironmentBlock, GCHandleType.Pinned);
                            environmentPtr = environmentHandle.AddrOfPinnedObject();
                        }

                        Pipes.CreateInheritablePipe(
                            Pipes.PipeInheritance.InheritRead,
                            Pipes.PipeFlags.WriteSideAsync,
                            readHandle: out hStdInput,
                            writeHandle: out standardInputWritePipeHandle);
                        Pipes.CreateInheritablePipe(
                            Pipes.PipeInheritance.InheritWrite,
                            Pipes.PipeFlags.ReadSideAsync,
                            readHandle: out standardOutputReadPipeHandle,
                            writeHandle: out hStdOutput);
                        Pipes.CreateInheritablePipe(
                            Pipes.PipeInheritance.InheritWrite,
                            Pipes.PipeFlags.ReadSideAsync,
                            readHandle: out standardErrorReadPipeHandle,
                            writeHandle: out hStdError);

                        // We want a per-process job primarily. If nested job support is not available, then we make sure to not have a BuildXL-level job.
                        if (JobObject.OSSupportsNestedJobs)
                        {
                            JobObject.SetTerminateOnCloseOnCurrentProcessJob();
                        }

                        // Initialize the injector
                        m_processInjector = new ProcessTreeContext(payloadGuid, inheritableReportHandle, payloadData, dllNameX64, dllNameX86, m_loggingContext);

                        // If path remapping is enabled then we wrap the job object in a container, so the filter drivers get
                        // configured (and they get cleaned up when the container is disposed)
                        if (m_containerConfiguration.IsIsolationEnabled)
                        {
                            m_job = new Container(
                                name: null,
                                containerConfiguration: m_containerConfiguration,
                                loggingContext: m_loggingContext);
                        }
                        else
                        {
                            m_job = new JobObject(null);
                        }

                        // We want the effects of SEM_NOGPFAULTERRORBOX on all children (but can't set that with CreateProcess).
                        // That's not set otherwise (even if set in this process) due to CREATE_DEFAULT_ERROR_MODE above.
                        m_job.SetLimitInformation(terminateOnClose: true, failCriticalErrors: false, allowProcessesToBreakAway: m_setJobBreakawayOk);

                        m_processInjector.Listen();

                        if (m_containerConfiguration.IsIsolationEnabled)
                        {
                            // After calling SetLimitInformation, start up the container if present
                            // This will throw if the container is not set up properly
                            m_job.StartContainerIfPresent();
                        }

                        // The call to the CreateDetouredProcess below will add a newly created process to the job.
                        System.Diagnostics.Stopwatch m_startUpTimeWatch = System.Diagnostics.Stopwatch.StartNew();
                        var detouredProcessCreationStatus =
                            Native.Processes.ProcessUtilities.CreateDetouredProcess(
                                m_commandLine,
                                creationFlags,
                                environmentPtr,
                                m_workingDirectory,
                                hStdInput,
                                hStdOutput,
                                hStdError,
                                m_job,
                                m_processInjector.Injector,
                                m_containerConfiguration.IsIsolationEnabled,
                                out m_processHandle,
                                out threadHandle,
                                out m_processId,
                                out int errorCode);
                        m_startUpTimeWatch.Stop();
                        m_startUpTime = m_startUpTimeWatch.ElapsedMilliseconds;

                        if (detouredProcessCreationStatus != CreateDetouredProcessStatus.Succeeded)
                        {
                            // TODO: Indicating user vs. internal errors (and particular phase failures e.g. adding to job object or injecting detours)
                            //       is good progress on the transparency into these failures. But consider making this indication visible beyond this
                            //       function without throwing exceptions; consider returning a structured value or logging events.
                            string message;
                            if (detouredProcessCreationStatus.IsDetoursSpecific())
                            {
                                message = string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Internal error during process creation: {0:G}",
                                    detouredProcessCreationStatus);
                            }
                            else if (detouredProcessCreationStatus == CreateDetouredProcessStatus.ProcessCreationFailed)
                            {
                                message = "Process creation failed";
                            }
                            else
                            {
                                message = string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Process creation failed: {0:G}",
                                    detouredProcessCreationStatus);
                            }

                            throw new BuildXLException(
                                message,
                                new NativeWin32Exception(errorCode));
                        }

                        // TODO: We should establish good post-conditions for CreateDetouredProcess. As a temporary measure, it would be nice
                        //       to determine if we are sometimes getting invalid process handles with retVal == true. So for now we differentiate
                        //       that possible case with a unique error string.
                        if (m_processHandle.IsInvalid)
                        {
                            throw new BuildXLException("Unable to start or detour a process (process handle invalid)", new NativeWin32Exception(errorCode));
                        }
                    }
                    finally
                    {
                        if (environmentHandle.IsAllocated)
                        {
                            environmentHandle.Free();
                        }

                        if (payloadHandle.IsAllocated)
                        {
                            payloadHandle.Free();
                        }

                        if (hStdInput != null && !hStdInput.IsInvalid)
                        {
                            hStdInput.Dispose();
                        }

                        if (hStdOutput != null && !hStdOutput.IsInvalid)
                        {
                            hStdOutput.Dispose();
                        }

                        if (hStdError != null && !hStdError.IsInvalid)
                        {
                            hStdError.Dispose();
                        }

                        if (inheritableReportHandle != null && !inheritableReportHandle.IsInvalid)
                        {
                            inheritableReportHandle.Dispose();
                        }

                        if (threadHandle != null && !threadHandle.IsInvalid)
                        {
                            threadHandle.Dispose();
                        }
                    }

                    var standardInputStream = new FileStream(standardInputWritePipeHandle, FileAccess.Write, m_bufferSize, isAsync: true);
                    m_standardInputWriter = new StreamWriter(standardInputStream, m_standardInputEncoding, m_bufferSize) { AutoFlush = true };

                    var standardOutputFile = AsyncFileFactory.CreateAsyncFile(
                        standardOutputReadPipeHandle,
                        FileDesiredAccess.GenericRead,
                        ownsHandle: true,
                        kind: FileKind.Pipe);
                    m_outputReader = new AsyncPipeReader(standardOutputFile, m_outputDataReceived, m_standardOutputEncoding, m_bufferSize);
                    m_outputReader.BeginReadLine();

                    var standardErrorFile = AsyncFileFactory.CreateAsyncFile(
                        standardErrorReadPipeHandle,
                        FileDesiredAccess.GenericRead,
                        ownsHandle: true,
                        kind: FileKind.Pipe);
                    m_errorReader = new AsyncPipeReader(standardErrorFile, m_errorDataReceived, m_standardErrorEncoding, m_bufferSize);
                    m_errorReader.BeginReadLine();

                    Contract.Assert(!m_processHandle.IsInvalid);
                    m_processWaitHandle = new SafeWaitHandleFromSafeHandle(m_processHandle);

                    m_waiting = true;

                    TimeSpan timeout = m_timeout ?? Timeout.InfiniteTimeSpan;
                    m_registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                        m_processWaitHandle,
                        CompletionCallback,
                        null,
                        timeout,
                        true);

                    m_started = true;
                }
                catch (Exception)
                {
                    // Dispose pipe handles in case they are not assigned to streams.
                    if (m_standardInputWriter == null)
                    {
                        standardInputWritePipeHandle?.Dispose();
                    }

                    if (m_outputReader == null)
                    {
                        standardOutputReadPipeHandle?.Dispose();
                    }

                    if (m_errorReader == null)
                    {
                        standardErrorReadPipeHandle?.Dispose();
                    }

                    throw;
                }
            }
        }

        private void StopWaiting(TaskUtilities.SemaphoreReleaser semaphoreReleaser)
        {
            Contract.Requires(semaphoreReleaser.IsValid && semaphoreReleaser.CurrentCount == 0);
            Analysis.IgnoreArgument(semaphoreReleaser);

            if (m_waiting)
            {
                m_waiting = false;
                m_registeredWaitHandle.Unregister(null);
                m_processWaitHandle.Dispose();
                m_processWaitHandle = null;
                m_registeredWaitHandle = null;
            }
        }

        private async Task WaitUntilErrorAndOutputEof(bool cancel, TaskUtilities.SemaphoreReleaser semaphoreReleaser)
        {
            Contract.Requires(semaphoreReleaser.IsValid && semaphoreReleaser.CurrentCount == 0);
            if (m_outputReader != null)
            {
                if (!m_killed && !cancel)
                {
                    await m_outputReader.WaitUntilEofAsync();
                }

                m_outputReader.Dispose();
                m_outputReader = null;
            }

            if (m_errorReader != null)
            {
                if (!m_killed && !cancel)
                {
                    await m_errorReader.WaitUntilEofAsync();
                }

                m_errorReader.Dispose();
                m_errorReader = null;
            }

            InternalCloseStandardInput(semaphoreReleaser);
        }

        private void InternalCloseStandardInput(TaskUtilities.SemaphoreReleaser semaphoreReleaser)
        {
            Contract.Requires(semaphoreReleaser.IsValid && semaphoreReleaser.CurrentCount == 0);
            Analysis.IgnoreArgument(semaphoreReleaser);

            if (m_standardInputWriter != null)
            {
                m_standardInputWriter.Dispose();
                m_standardInputWriter = null;
            }
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "FailFast")]
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "DetouredProcess")]
        [SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        private async void CompletionCallback(object context, bool timedOut)
        {
            if (timedOut)
            {
                Volatile.Write(ref m_timedout, true);

                // Attempt to dump the timed out process before killing it
                if (!m_processHandle.IsInvalid && !m_processHandle.IsClosed && m_workingDirectory != null)
                {
                    DumpFileDirectory = m_timeoutDumpDirectory;

                    Exception dumpCreationException;

                    try
                    {
                        Directory.CreateDirectory(DumpFileDirectory);
                    }
                    catch (Exception ex)
                    {
                        DumpCreationException = ex;
                    }

                    if (DumpCreationException == null &&
                        !ProcessDumper.TryDumpProcessAndChildren(
                            parentProcessId: m_processId,
                            dumpDirectory: DumpFileDirectory,
                            primaryDumpCreationException: out dumpCreationException))
                    {
                        DumpCreationException = dumpCreationException;
                    }
                }

                Kill(ExitCodes.Timeout);

                using (await m_syncSemaphore.AcquireAsync())
                {
                    if (m_processWaitHandle != null)
                    {
                        await m_processWaitHandle;
                        m_exited = true;
                    }
                }
            }
            else
            {
                m_exited = true;
            }

            using (var semaphoreReleaser = await m_syncSemaphore.AcquireAsync())
            {
                Contract.Assume(m_waiting, "CompletionCallback should only be triggered once.");
                StopWaiting(semaphoreReleaser);
            }

            try
            {
                await Task.Run(
                    async () =>
                    {
                        // Before waiting on anything, we call the processExiting callback.
                        // This callback happens to be responsible for triggering or forcing
                        // cleanup of all processes in this job. We can't finish waiting on pipe EOF
                        // (error, output, report, and process-injector pipes) until all handles
                        // to the write-sides are closed.
                        Func<Task> processExiting = m_processExitingAsync;
                        if (processExiting != null)
                        {
                            await processExiting();
                        }

                        using (var semaphoreReleaser = await m_syncSemaphore.AcquireAsync())
                        {
                            // Error and output pipes: Finish reading and then expect EOF (see above).
                            await WaitUntilErrorAndOutputEof(false, semaphoreReleaser);

                            // Stop process injection service. This finishes reading the injector control pipe (for injection requests).
                            // Since we don't get to the 'end' of the pipe until all child-processes holding on to it exit, we must
                            // perform this wait after processExiting() above.
                            if (m_processInjector != null)
                            {
                                // Stop() discards all unhandled requests. That is only safe to do since we are assuming that all processes
                                // in the job have exited (so those requests aren't relevant anymore)
                                await m_processInjector.Stop();
                                m_hasDetoursFailures = m_processInjector.HasDetoursInjectionFailures;
                                m_processInjector.Dispose();
                                m_processInjector = null;
                            }
                        }

                        // Now, callback for additional cleanup (can safely wait on extra pipes, such as the SandboxedProcess report pipe,
                        // if processExiting causes process tree teardown; see above).
                        var processExited = m_processExited;
                        if (processExited != null)
                        {
                            await processExited();
                        }
                    });
            }
            catch (Exception exception)
            {
                // Something above may fail and that has to be observed. Unfortunately, throwing a normal exception in a continuation
                // just means someone has to observe the continuation. So we tug on some bootstraps by killing the process here.
                // TODO: It'd be nice if we had a FailFast equivalent that went through AppDomain.UnhandledExceptionEvent for logging.
                ExceptionHandling.OnFatalException(exception, "FailFast in DetouredProcess completion callback");
            }
        }

        /// <summary>
        /// Releases all resources associated with this process.
        /// </summary>
        /// <remarks>
        /// This function can be called at any time, and as often as desired.
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_errorReader", Justification = "Disposed in WaitUntilErrorAndOutputEof")]
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_outputReader", Justification = "Disposed in WaitUntilErrorAndOutputEof")]
        public void Dispose()
        {
            if (!m_disposed)
            {
                using (var semaphoreReleaser = m_syncSemaphore.AcquireSemaphore())
                {
                    StopWaiting(semaphoreReleaser);
                    WaitUntilErrorAndOutputEof(true, semaphoreReleaser).GetAwaiter().GetResult();

                    if (m_processInjector != null)
                    {
                        // We may have already called Stop() in CompletionCallback, but that's okay.
                        m_processInjector.Stop().GetAwaiter().GetResult();
                        m_processInjector.Dispose();
                        m_processInjector = null;
                    }

                    if (m_processHandle != null)
                    {
                        m_processHandle.Dispose();
                        m_processHandle = null;
                    }

                    if (m_job != null)
                    {
                        m_job.Dispose();
                        m_job = null;
                    }
                }

                m_syncSemaphore.Dispose();

                m_disposed = true;
            }
        }

        public long StartTime { get { return m_startUpTime; } }
    }
}
