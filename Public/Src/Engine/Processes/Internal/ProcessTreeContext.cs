// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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

        /// <summary>
        /// Maximum number of retries to inject detours into a process.
        /// </summary>
        /// <remarks>
        /// The number of retries follows the pattern in DetoursServices.cpp, i.e., BUILDXL_DETOURS_INJECT_PROCESS_RETRY_COUNT.
        /// These numbers are chosen based on experience and they do not have to sync with each other.
        /// </remarks>
        private const int MaxDetoursInjectRetries = 5;
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

        public ProcessTreeContext(
            Guid payloadGuid,
            SafeHandle reportPipe,
            ArraySegment<byte> payload,
            string dllNameX64,
            string dllNameX86,
            int numRetriesPipeReadOnCancel,
            Action<string> debugReporter,
            LoggingContext loggingContext)
        {
            // We cannot create this object in a wow64 process
            Contract.Assume(!ProcessUtilities.IsWow64Process(), "ProcessTreeContext:ctor - Cannot run injection server in a wow64 32 bit process");
            Contract.Requires(loggingContext != null);

            m_debugReporter = debugReporter;
            m_loggingContext = loggingContext;
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

            // Wait until reader is done
            if (m_injectionRequestReader != null)
            {
                await m_injectionRequestReader.CompletionAsync(true);
            }
        }

        public readonly IProcessInjector Injector;

        public bool HasDetoursInjectionFailures { get; private set; }

        /// <summary>
        /// Called to indicate that the process was killed
        /// </summary>
        public void OnKilled()
        {
            // Stop processing additional messages.
            Volatile.Write(ref m_stopping, true);
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

            // Once one injection fails, all others also fail.
            bool succeeded = !HasDetoursInjectionFailures;
            if (succeeded)
            {
                lock (Injector)
                {
                    if (Injector.IsDisposed)
                    {
                        // Stop just called. Ignore the request.
                        Contract.Assert(m_stopping);
                        return true;
                    }

                    int retryCount = 0;
                    uint injectionError = NativeIOConstants.ErrorSuccess;
                    while (retryCount < MaxDetoursInjectRetries)
                    {
                        injectionError = Injector.Inject(processId, inheritedHandles);
                        succeeded = injectionError == NativeIOConstants.ErrorSuccess;

                        // Retry only if the error is partial copy.
                        if (injectionError != NativeIOConstants.ErrorPartialCopy)
                        {
                            break;
                        }

                        // Sleep for a while and retry.
                        Thread.Sleep((retryCount + 1) * 100);
                        ++retryCount;
                    }

                    if (!succeeded)
                    {
                        ReportFailedInjection(processId, injectionError.ToString("X8", CultureInfo.InvariantCulture));
                    }
                }
            }

            string eventName = succeeded ? eventPathSuccess : eventPathFailure;

            Possible<EventWaitHandle> signalEventResult = TrySignalEventWithFallback(eventName);
            if (signalEventResult.Succeeded)
            {
                EventWaitHandle e = signalEventResult.Result;
                e.Set();
                e.Dispose();
            }
            else
            {
                ReportFailedInjection(processId, signalEventResult.Failure.DescribeIncludingInnerFailures());
            }

            return signalEventResult.Succeeded;
        }

        private Possible<EventWaitHandle> TrySignalEventWithFallback(string eventName)
        {
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
                return new Failure<string>($"Failed to open event '{eventName}' after multiple delays with error code '{errorCode}': {ex}");
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
