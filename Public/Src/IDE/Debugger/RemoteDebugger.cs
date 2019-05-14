// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net.Sockets;
using BuildXL.FrontEnd.Script.Debugger.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using VSCode.DebugAdapter;
using VSCode.DebugProtocol;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// Base class for communicating with a client (front-end) debugger.
    ///
    /// Overrides the <code cref="SendMessage"/> method just to make it 'synchronized',
    /// because the <code cref="SendMessage"/> method may be called from multiple
    /// threads, i.e., the thread handling requests from the client, as well as the
    /// DScript evaluation threads.
    ///
    /// Doesn't use or have any state; note that the super class may.
    /// </summary>
    public sealed class RemoteDebugger : RemoteClientDebugger, IDebugger
    {
        private readonly DebuggerState m_state;
        private readonly Socket m_clientSocket;

        private Logger Logger => m_state.Logger;

        private LoggingContext LoggingContext => m_state.LoggingContext;

        /// <inheritdoc/>
        public override ISession Session { get; }

        /// <nodoc/>
        public RemoteDebugger(DebuggerState state, PathTranslator buildXLToUserPathTranslator, Socket clientSocket)
            : base(true, false)
        {
            m_state = state;
            m_clientSocket = clientSocket;
            Session = new DebugSession(state, buildXLToUserPathTranslator, this);
        }

        /// <summary>
        /// Starts an asynchronous communication with the remote client.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
        public async void StartAsync()
        {
            var stream = new NetworkStream(m_clientSocket);
            try
            {
                await StartAsync(stream, stream);
            }
            catch (Exception e)
            {
                Logger.ReportDebuggerClientGenericError(LoggingContext, e.GetLogEventMessage(), e.StackTrace);
                ShutDown();
            }
            finally
            {
                if (stream != null)
                {
#pragma warning disable AsyncFixer02
                    stream.Dispose();
#pragma warning restore AsyncFixer02
                }
            }
        }

        /// <inheritdoc/>
        public void ShutDown()
        {
            Session.Disconnect(new DisconnectCommand());
            SendMessage(new TerminatedEvent());
            Stop();
        }

        /// <inheritdoc/>
        protected sealed override void Dispatch(string request)
        {
            Logger.ReportDebuggerRequestReceived(LoggingContext, request);
            base.Dispatch(request);
        }

        /// <inheritdoc/>
        protected sealed override void DispatchRequest(IRequest request)
        {
            try
            {
                base.DispatchRequest(request);
            }
            catch (DebuggerException e)
            {
                SendErrorResponse(request, e.Message);
            }
        }

        /// <summary>
        /// Adds synchronization around the base method call.  Uses <code>this</code> as the lock.
        /// </summary>
        protected sealed override void SendMessage(IProtocolMessage message)
        {
            Logger.ReportDebuggerMessageSent(LoggingContext, message.Type, message.ToString());

            lock (this)
            {
                base.SendMessage(message);
            }
        }
    }
}
