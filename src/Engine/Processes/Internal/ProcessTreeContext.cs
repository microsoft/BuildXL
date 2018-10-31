// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Native.Streams;
using BuildXL.Processes.Internal;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
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
        private AsyncPipeReader m_injectionRequestReader;
        private bool m_stopping;

        private LoggingContext m_loggingContext;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public ProcessTreeContext(Guid payloadGuid, SafeHandle reportPipe, ArraySegment<byte> payload, string dllNameX64, string dllNameX86, LoggingContext loggingContext)
        {
            // We cannot create this object in a wow64 process
            Contract.Assume(
                !ProcessUtilities.IsWow64Process(),
                "ProcessTreeContext:ctor - Cannot run injection server in a wow64 32 bit process");
            SafeFileHandle childHandle = null;

            m_loggingContext = loggingContext;

            // This object will be the server for the tree. CreateSourceFile the pipe server.
            try
            {
                SafeFileHandle injectorHandle;

                // Create a pipe for the requests
                Pipes.CreateInheritablePipe(Pipes.PipeInheritance.InheritWrite, Pipes.PipeFlags.ReadSideAsync, out injectorHandle, out childHandle);

                // Create the injector. This will duplicate the handles.
                Injector = ProcessUtilities.CreateProcessInjector(payloadGuid, childHandle, reportPipe, dllNameX86, dllNameX64, payload);

                // Create the request reader. We don't start listening until requested
                var injectionRequestFile = AsyncFileFactory.CreateAsyncFile(
                    injectorHandle,
                    FileDesiredAccess.GenericRead,
                    ownsHandle: true,
                    kind: FileKind.Pipe);
                m_injectionRequestReader = new AsyncPipeReader(injectionRequestFile, InjectCallback, Encoding.Unicode, BufferSize);
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

        public async Task Stop()
        {
            PrepareToStop();

            // Wait until reader is done
            if (m_injectionRequestReader != null)
            {
                await m_injectionRequestReader.WaitUntilEofAsync();
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
            if (items.Length != 4)
            {
                ReportFailedInjection(0, "Partial string received.");
                return false;
            }

            // The last part is the process id. It can be incomplete...
            uint processId;
            if (!uint.TryParse(items[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out processId))
            {
                ReportFailedInjection(0, "Partial string received.");
                return false;
            }

            // If it is correct, it must contain a valid process id.
            Contract.Assume(processId != 0, "Brokered injection request is incorrect -- target process id is 0");

            // The first item is the event to signal in case of success.
            var eventPathSuccess = items[0];
            Contract.Assume(eventPathSuccess.Length > 0);

            // The second item is the event to signal in case of failure.
            var eventPathFailure = items[1];
            Contract.Assume(eventPathFailure.Length > 0);

            // The third argument is a flag that indicates whether the handles are inherited
            bool inheritedHandles;
            bool succeeded = bool.TryParse(items[2], out inheritedHandles);
            if (!succeeded)
            {
                ReportFailedInjection(processId, "Failure parsing a boolean.");
            }

            Contract.Assume(succeeded, "Brokered injection request is malformed -- cannot parse inheritance flag");

            // Once one injection fails, all others also fail.
            succeeded &= !HasDetoursInjectionFailures;
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

                    uint injectionError = Injector.Inject(processId, inheritedHandles);
                    if (injectionError != 0)
                    {
                        ReportFailedInjection(processId, injectionError.ToString("X8", CultureInfo.InvariantCulture));
                        succeeded = false;
                    }
                }
            }

            string eventName = succeeded ? eventPathSuccess : eventPathFailure;
            EventWaitHandle e;

            // Inject data & DLLs and map the drives and signal the caller indicating that we are done
            if (EventWaitHandle.TryOpenExisting(eventName, out e))
            {
                e.Set();
                e.Dispose();
            }
            else if (succeeded)
            {
                ReportFailedInjection(processId, string.Format(CultureInfo.InvariantCulture, "Cannot open event '{0}'", eventName));
            }

            return true;
        }

        private void ReportFailedInjection(uint processId, string error)
        {
            if (Volatile.Read(ref m_stopping))
            {
                return; // Ignore the error it it happened after the stopping
            }

            HasDetoursInjectionFailures = true;

            BuildXL.Processes.Tracing.Logger.Log.BrokeredDetoursInjectionFailed(m_loggingContext != null ? m_loggingContext : Events.StaticContext, processId, error);
        }
    }
}
