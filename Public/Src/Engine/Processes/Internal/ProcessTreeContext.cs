// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Native.Streams;
using BuildXL.Processes.Internal;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Processes
{
    /// <summary>
    /// This ProcessTreeContext object creates a native injector object that can make a child
    /// process usable by BuildXL. The ProcessTreeContext is created by DetouredProcess object
    /// when creating a top of a process tree. The injector knows how to:
    /// - map drives to match the mapping in effect when this object was created (if any)
    /// - inject appropriate dll for the detours
    /// - copy payload into the child process memory (with BuildXL policy and auxiliary info,
    ///   including information that can be used to recreate the injector in the child.
    /// In 64 bit processes the created injector can do the same to its children. The 32 bit
    /// processes can do everything, but the drive mapping to other 32 bit processes and cannot
    /// do any of the above to 64 bit children. When the injector cannot do it itself, it can
    /// request the top-of-the-tree process (which contains the ProcessTreeContext) to do the
    /// injection. In order to do that, a server is created to listen to requests from the child
    /// processes. When such requests (containing a newly created process id) are received, the
    /// injector is called to update the process with the required info:
    /// </summary>
    internal sealed class ProcessTreeContext : IDisposable
    {
        private const int BufferSize = 4096;

        private IAsyncPipeReader m_injectionRequestReader;
        private bool m_stopping;

        /// <summary>
        /// Represents the predefined delays, in milliseconds, before each retry on signalling done event after detours injection.
        /// </summary>
        /// <remarks>
        /// Unlike the retries for detours injection, the delays for triggering signal are longer, and are sufficient long to ensure
        /// that the done events have been created and are ready to be opened.
        /// </remarks>
        private readonly int[] m_signalInjectionDelaysMs = [1000, 2000, 4000];

        private readonly Action<string> m_debugReporter;
        private readonly LoggingContext m_loggingContext;
        private readonly Action m_onBrokeredInjectionFailure;
        private readonly long m_pipSemiStableHash;
        private readonly int m_injectorPipeStopTimeoutMs;

        public ProcessTreeContext(
            Guid payloadGuid,
            SafeHandle reportPipe,
            ArraySegment<byte> payload,
            string dllNameX64,
            string dllNameX86,
            int numRetriesPipeReadOnCancel,
            Action<string> debugReporter,
            LoggingContext loggingContext,
            long pipSemiStableHash,
            int injectorPipeStopTimeoutMs,
            Action onBrokeredInjectionFailure = null)
        {
            // We cannot create this object in a wow64 process
            Contract.Assume(!ProcessUtilities.IsWow64Process(), "ProcessTreeContext:ctor - Cannot run injection server in a wow64 32 bit process");
            Contract.Requires(loggingContext != null);
            Contract.Requires(injectorPipeStopTimeoutMs > 0);

            m_debugReporter = debugReporter;
            m_loggingContext = loggingContext;
            m_onBrokeredInjectionFailure = onBrokeredInjectionFailure;
            m_pipSemiStableHash = pipSemiStableHash;
            m_injectorPipeStopTimeoutMs = injectorPipeStopTimeoutMs;
            SafeFileHandle childHandle = null;
            NamedPipeServerStream serverStream = null;

            bool useManagedPipeReader = !PipeReaderFactory.ShouldUseLegacyPipeReader();

            // This object will be the server for the tree. CreateSourceFile the pipe server.
            try
            {
                SafeFileHandle injectorHandle = null;

                if (useManagedPipeReader)
                {
                    serverStream = Pipes.CreateNamedPipeServerStream(
                        PipeDirection.In,
                        PipeOptions.Asynchronous,
                        PipeOptions.None,
                        out childHandle);
                }
                else
                {
                    // Create a pipe for the requests
                    Pipes.CreateInheritablePipe(Pipes.PipeInheritance.InheritWrite, Pipes.PipeFlags.ReadSideAsync, out injectorHandle, out childHandle);
                }

                // Create the injector. This will duplicate the handles.
                Injector = ProcessUtilities.CreateProcessInjector(payloadGuid, childHandle, reportPipe, dllNameX86, dllNameX64, payload);

                if (useManagedPipeReader)
                {
                    m_injectionRequestReader = PipeReaderFactory.CreateManagedPipeReader(
                        serverStream,
                        InjectCallback,
                        Encoding.Unicode,
                        BufferSize);
                }
                else
                {
                    // Create the request reader. We don't start listening until requested
                    var injectionRequestFile = AsyncFileFactory.CreateAsyncFile(
                        injectorHandle,
                        FileDesiredAccess.GenericRead,
                        ownsHandle: true,
                        kind: FileKind.Pipe);
                    m_injectionRequestReader = new AsyncPipeReader(
                        injectionRequestFile,
                        InjectCallback,
                        Encoding.Unicode,
                        BufferSize,
                        numOfRetriesOnCancel: numRetriesPipeReadOnCancel,
                        debugPipeReporter: new AsyncPipeReader.DebugReporter(debugMsg => debugReporter?.Invoke($"InjectionRequestReader: {debugMsg}")));
                }
            }
            catch (Exception exception)
            {
                if (Injector != null)
                {
                    Injector.Dispose();
                    Injector = null;
                }

                if (m_injectionRequestReader != null)
                {
                    m_injectionRequestReader.Dispose();
                    m_injectionRequestReader = null;
                }

                throw new BuildXLException("Process Tree Context injector could not be created", exception);
            }
            finally
            {
                // Release memory. Since the child handle is duplicated, it can be released
                
                if (childHandle != null && !childHandle.IsInvalid)
                {
                    childHandle.Dispose();
                }
            }
        }

        public void Listen()
        {
            Contract.Assume(m_injectionRequestReader != null);
            m_injectionRequestReader.BeginReadLine();
        }

        public async Task StopAsync()
        {
            PrepareToStop();

            // The injector pipe is a kernel object whose read side EOFs only after every writer-end
            // handle is released. If a non-self process still holds a writer end (e.g. a
            // CREATE_BREAKAWAY_FROM_JOB descendant, or a process that received the writer via
            // DuplicateHandle), an unbounded await here parks indefinitely. SandboxedProcess.Dispose
            // chains into this synchronously, so the stall translates to a permanently leaked
            // pip-execution slot.
            //
            // Bound the wait. On timeout, forcibly disconnect the server end via TryDisconnect; that
            // EOFs the reader regardless of remaining writer handles in the kernel. PrepareToStop
            // already set m_stopping=true above, so any post-disconnect InjectCallback short-circuits.
            //
            // Snapshot the reader: defensive against a future caller invoking Dispose concurrently with
            // StopAsync (which nulls m_injectionRequestReader after disposing the reader).
            IAsyncPipeReader reader = m_injectionRequestReader;
            if (reader == null)
            {
                return;
            }

            Task completion = reader.CompletionAsync(true);

            if (completion.IsCompleted)
            {
                // Fast path: reader already drained. Avoid allocating a CTS / Task.Delay timer.
                await completion;
                return;
            }

            using var delayCts = new CancellationTokenSource();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Task winner = await Task.WhenAny(completion, Task.Delay(m_injectorPipeStopTimeoutMs, delayCts.Token));

            if (winner == completion)
            {
                // Cancel the delay so its timer doesn't linger.
#if NETCOREAPP
                await delayCts.CancelAsync();
#else
                delayCts.Cancel();
#endif
                await completion;
                return;
            }

            // Timeout. Forcibly EOF the reader and observe the eventual completion.
            bool disconnectSucceeded = reader.TryDisconnect();

            Tracing.Logger.Log.InjectorPipeStopAsyncTimedOut(
                m_loggingContext,
                m_pipSemiStableHash,
                m_injectorPipeStopTimeoutMs,
                stopwatch.ElapsedMilliseconds,
                disconnectSucceeded);

            // Always observe `completion`, even when TryDisconnect returned false (legacy reader, or
            // a swallowed teardown race); otherwise the pipe handle leaks. Reader exceptions on this
            // path are expected (writer was forcibly torn down) and not actionable beyond the telemetry
            // above.
            try
            {
                await completion;
            }
            catch (Exception ex)
            {
#pragma warning disable EPC12 // Diagnostic-only swallow; full ToString preserves the failure context.
                m_debugReporter?.Invoke($"InjectionRequestReader: completion observed exception after disconnect: {ex.ToStringDemystified()}");
#pragma warning restore EPC12
            }
        }

        public readonly IProcessInjector Injector;

        public bool HasDetoursInjectionFailures { get; private set; }

        /// <summary>
        /// Called to indicate that the process was killed.
        /// </summary>
        /// <remarks>
        /// Sets the stopping flag so subsequent <see cref="InjectCallback"/> invocations short-circuit,
        /// then forcibly disconnects the injector pipe to unblock any in-flight <see cref="StopAsync"/>
        /// drain. Best-effort (TryDisconnect does not throw); no-op for legacy anonymous-pipe readers.
        /// Order matters: m_stopping must be set before disconnect so any final callback the reader
        /// flushes is short-circuited rather than processed.
        /// </remarks>
        public void OnKilled()
        {
            // Stop processing additional messages.
            Volatile.Write(ref m_stopping, true);

            // Volatile read defends against a concurrent Dispose nulling the field.
            Volatile.Read(ref m_injectionRequestReader)?.TryDisconnect();
        }

        public void Dispose()
        {
            // We dispose the injector first since it must have a write-handle to the pipe (to give to children).
            // EOF can't be reached until all writable handles are closed.
            // Requests after injector-dispose turn into no-ops (synchronized with a lock), and so the caller should take care to call Stop()
            // only after all processes have exited.
            if (Injector != null)
            {
                PrepareToStop();
            }

            if (m_injectionRequestReader != null)
            {
                m_injectionRequestReader.Dispose();
                m_injectionRequestReader = null;
            }
        }

        private void PrepareToStop()
        {
            // Stop processing additional messages.
            Volatile.Write(ref m_stopping, true);

            // At this time all processes have exited and the only pipe handle is held by the injector.
            // Dispose the injector to unblock the pipe reader.
            lock (Injector)
            {
                if (!Injector.IsDisposed)
                {
                    Injector.Dispose();
                }
            }
        }

        /// <summary>
        /// Callback invoked when a new injection request is recieved
        /// </summary>
        private bool InjectCallback(string data)
        {
            if (data == null)
            {
                // EOF
                return true;
            }

            // We are done!
            if (Volatile.Read(ref m_stopping))
            {
                return true;
            }

            string[] items = data.Split(',');
            Contract.Assert(items.Length == 4, $"Brokered injection request is malformed -- expected 4 parts separated by commas, but got '{data}'");

            uint processId;
            if (!uint.TryParse(items[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out processId))
            {
                Contract.Assert(false, $"Brokered injection request is malformed -- cannot parse process id '{items[3]}'");
            }

            // If it is correct, it must contain a valid process id.
            Contract.Assert(processId != 0, "Brokered injection request is incorrect -- target process id is 0");

            // The first item is the event to signal in case of success.
            var eventPathSuccess = items[0];
            Contract.Assert(!string.IsNullOrEmpty(eventPathSuccess));

            // The second item is the event to signal in case of failure.
            var eventPathFailure = items[1];
            Contract.Assert(!string.IsNullOrEmpty(eventPathFailure));

            // The third argument is a flag that indicates whether the handles are inherited
            bool inheritedHandles;
            if (!bool.TryParse(items[2], out inheritedHandles))
            {
                Contract.Assert(false, $"Brokered injection request is malformed -- cannot parse inheritance flag '{items[2]}'");
            }

            bool succeeded = false;
            lock (Injector)
            {
                if (Injector.IsDisposed)
                {
                    // Stop just called. Ignore the request.
                    Contract.Assert(m_stopping);
                    return true;
                }

                // Inject data & DLLs and map the drives.
                // Do not retry process injection on the same process handle on failure. This is because the target process may
                // have been updated with the Detours dll. Trying to inject the same process again will result in ERROR_INVALID_OPERATION.
                // Instead of retrying process injection, retry the process creation as a whole.
                uint injectionError = Injector.Inject(processId, inheritedHandles);
                succeeded = injectionError == NativeIOConstants.ErrorSuccess;
                if (!succeeded)
                {
                    ReportFailedInjection(processId, injectionError.ToString("X8", CultureInfo.InvariantCulture));
                }
            }

            string eventName = succeeded ? eventPathSuccess : eventPathFailure;

            Possible<EventWaitHandle> signalEventResult = TrySignalEventWithFallback(eventName, out bool parentProcessIsAlive);
            if (signalEventResult.Succeeded)
            {
                EventWaitHandle e = signalEventResult.Result;
                e.Set();
                e.Dispose();
            }
            else
            {
                ReportFailedInjection(processId, signalEventResult.Failure.DescribeIncludingInnerFailures());

                // If the parent process that requested the injection is still alive we don't want to kill the pip here
                // because inside the native code we have an internal retry for remote injection which is preferred 
                // to killing the whole process tree from the managed side.
                // If the internal retry fails, we already return a special error code to retry the pip at a higher layer.
                if (!parentProcessIsAlive)
                {
                    // Kill the process tree early so the pip can be retried sooner rather than
                    // waiting for the full execution to complete with an unreliable process tree.
                    m_onBrokeredInjectionFailure?.Invoke();
                }
            }

            return signalEventResult.Succeeded;
        }

        private Possible<EventWaitHandle> TrySignalEventWithFallback(string eventName, out bool parentProcessIsAlive)
        {
            parentProcessIsAlive = true;
            EventWaitHandle eventWaitHandle;

            foreach (var delay in m_signalInjectionDelaysMs)
            {
#pragma warning disable CA1416 // This code only runs on Windows
                if (EventWaitHandle.TryOpenExisting(eventName, out eventWaitHandle))
#pragma warning restore CA1416
                {
                    return eventWaitHandle;
                }

                Thread.Sleep(delay);
            }

            try
            {
#pragma warning disable CA1416 // This code only runs on Windows
                eventWaitHandle = EventWaitHandle.OpenExisting(eventName);
#pragma warning restore CA1416
                return eventWaitHandle;
            }
            catch (Exception ex)
            {
                // Need to get error code because when `WaitHandleCannotBeOpenedException` is thrown, it is not clear if the event does not exist, or if the name is invalid, or if it is inaccessible.
                int errorCode = Marshal.GetLastWin32Error();

                // CODESYNC: Public/Src/Sandbox/Windows/DetoursServices/DetouredProcessInjector.cpp
                // The event name embeds the process id of the parent and child processes.
                // Lets check if they are alive here before throwing
                // Format: Global\wwwwwwww-xxxxxxxx-yyyyyyyyyyyyyyyy-z
                var parts = eventName.Substring(7).Split('-');
                var parentProcessExitCode = ExitCodes.UninitializedIntProcessExitCode;
                var childProcessExitCode = ExitCodes.UninitializedIntProcessExitCode;
                int childPid = -1;
                int parentPid = -1;

                if (parts.Length >= 3
                    && int.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out childPid)
                    && int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parentPid))
                {
                    // Parent and child are separated here so that we can get a signal when one of them throws.
                    try
                    {
                        using var parent = Process.GetProcessById(parentPid);
                        parentProcessExitCode = parent.HasExited ? parent.ExitCode : ExitCodes.Running;
                    }
                    catch (Exception)
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                    {
                        // Best effort. Ignore exceptions here.
                        parentProcessIsAlive = false;
                    }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
                    
                    try
                    {
                        using var child = Process.GetProcessById(childPid);
                        childProcessExitCode = child.HasExited ? child.ExitCode : ExitCodes.Running;
                    }
                    catch (Exception)
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                    {
                        // Best effort. Ignore exceptions here.
                    }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler

                }

                return new Failure<string>($"Failed to open event '{eventName}' for parent process {parentPid}:{parentProcessExitCode} and child process {childPid}:{childProcessExitCode} after multiple delays with error code '{errorCode}': {ex}");
            }
        }

        private void ReportFailedInjection(uint processId, string error)
        {
            if (Volatile.Read(ref m_stopping))
            {
                return; // Ignore the error it it happened after the stopping
            }

            HasDetoursInjectionFailures = true;
            Tracing.Logger.Log.BrokeredDetoursInjectionFailed(m_loggingContext, processId, error);
            m_debugReporter?.Invoke($"Detours (remote) injection failed for process {processId}: {error}");
        }
    }
}
