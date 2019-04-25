// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace MsBuildGraphBuilderTool
{
    /// <summary>
    /// Enables sending out plain string messages through a named pipe
    /// </summary>
    /// <remarks>
    /// Allows the graph builder to report progress and warnings while the construction process runs
    /// </remarks>
    public class GraphBuilderReporter : IDisposable
    {
        private readonly string m_pipeName;
        private readonly ActionBlock<string> m_messageQueue;
        private readonly ConcurrentQueue<string> m_errors = new ConcurrentQueue<string>();
        private NamedPipeServerStream m_pipe;
        private StreamWriter m_streamWriter;

        private bool IsSuccessfullyInitialized => m_streamWriter != null && m_pipe != null;

        private bool m_triedToInitializePipe = false;

        /// <summary>
        /// The errors the reporter has found so far. Each of these errors are also
        /// printed to standard error.
        /// </summary>
        public IReadOnlyCollection<string> Errors => m_errors.ToArray();

        /// <nodoc/>
        public GraphBuilderReporter(string pipeName)
        {
            m_pipeName = pipeName;
            m_messageQueue = new ActionBlock<string>(
                message => SendMessageThroughPipe(message),
                new ExecutionDataflowBlockOptions
                {
                    // no cap for the queue, these are user-facing messages, so we don't expect that many
                    BoundedCapacity = DataflowBlockOptions.Unbounded,
                    // we just want a single thread to take care of dispatching messages through the pipe
                    MaxDegreeOfParallelism = 1,
                });
        }

        /// <summary>
        /// Reports a message in a fire-and-forget manner to a named pipe.
        /// </summary>
        /// <returns>
        /// Whether the message was successfully scheduled to be sent. Any problems initializing the pipe will just make the message to not be scheduled.
        /// </returns>
        public bool ReportMessage(string message)
        {
            // The pipe is initialized when the first message is reported
            if (!m_triedToInitializePipe)
            {
                InitializePipe();
            }

            // The pipe is already initialized, but it may have encountered some problems. In that case
            // message reporting is off, and no messages are sent.
            // The initialization problem is already reported at this point by TryCreateServerPipe
            if (!IsSuccessfullyInitialized)
            {
                return false;
            }

            var result = m_messageQueue.Post(message);
            Contract.Assert(result);

            return true;
        }

        private void InitializePipe()
        {
            m_triedToInitializePipe = true;

            if (!TryCreateServerPipe(m_pipeName, out m_pipe))
            {
                return;
            }

            m_streamWriter = new StreamWriter(m_pipe, Encoding.UTF8);
        }

        private void SendMessageThroughPipe(string message)
        {
            Contract.Assert(IsSuccessfullyInitialized, "The pipe hasn't been initialized.");
            Contract.Assert(!string.IsNullOrEmpty(message), "Message should not be null");

            // If the pipe is not connected yet, wait for a connection before sending out the first message
            // If a connection never happens on time, too bad, the message will never leave the queue and everything will be disposed on exit
            // The net effect is that progress won't be reported
            if (!m_pipe.IsConnected)
            {
                try
                {
                    m_pipe.WaitForConnection();
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
                {
                    // This happens when the pipe has been broken or closed. This means the graph construction process was terminated without a client connecting to it
                    // Just propagate the exception to the BuildXL log to not go completely silent about this, but this error is inconsequential.
                    LogError(ex.Message);
                    return;
                }

                m_streamWriter.AutoFlush = true;
            }

            m_streamWriter.WriteLine(message);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Caller is responsible for disposing")]
        private bool TryCreateServerPipe(string pipeName, out NamedPipeServerStream pipe)
        {
            try
            {
                pipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.Out,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous);
                return true;
            }
            catch (IOException ex)
            {
                // Write the exception to the standard error, but not make the whole construction process
                // fail because of this. This will be propagated to the BuildXL log.
                LogError(ex.Message);

                pipe = null;
                return false;
            }
        }

        public void Dispose()
        {
            // We complete the queue, so no new messages are accepted
            m_messageQueue.Complete();

            // If there are still some messages to process and we also got a client to connect successfully,
            // there is still a chance to deliver the pending messages successfully before disposing the queue
            // Otherwise, it is not worth trying
            if (m_messageQueue.InputCount > 0 && m_pipe.IsConnected)
            {
                // Let's wait for the queue completion, but also set a timeout, just in case the client is gone
                // or some other connection problem happens. We don't want to wait forever and make the caller unresponsive.
                // 100ms timeout should be enough
#pragma warning disable EPC13 // Suspiciously unobserved result.
                Task.WhenAny(m_messageQueue.Completion, Task.Delay(100)).GetAwaiter().GetResult();
#pragma warning restore EPC13 // Suspiciously unobserved result.
            }
            try
            {
                m_streamWriter?.Dispose();
            }
            catch (InvalidOperationException ex)
            {
                // This exception is thrown when no connections have been made yet. This may happen
                // if the client failed to connect. We just ignore this, but log to standard error so
                // it gets propagated to the BuildXL log
                LogError(ex.Message);
            }
            try
            {
                m_pipe?.Dispose();
            }
            catch (InvalidOperationException ex)
            {
                // This exception is thrown when no connections have been made yet. This may happen
                // if the client failed to connect. We just ignore this, but log to standard error so
                // it gets propagated to the BuildXL log
                LogError(ex.Message);
            }
        }

        private void LogError(string message)
        {
            m_errors.Enqueue(message);
            Console.Error.Write(message);
        }
    }
}
