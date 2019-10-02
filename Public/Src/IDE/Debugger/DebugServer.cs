// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using VSCode.DebugProtocol;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// Singleton.
    ///
    /// Creates a server socket and listens for an incoming connection
    /// from a single debug client (front-end, IDE).  As soon as a client is
    /// connected, a debug session (<code cref="DebugSession"/>) is created, which
    /// handles all subsequent communication with the client on its own thread, while
    /// the server thread immediately dies.
    ///
    /// This singleton server can be started only once.
    /// </summary>
    public sealed class DebugServer : IService
    {
        private const int True = 1;
        private const int False = 0;

        /// <summary>Default server TCP/IP port to listen on.</summary>
        public static readonly int DefaultDebugPort = 41177;

        private readonly Tracing.Logger m_logger;
        private readonly LoggingContext m_loggingContext;
        private readonly TcpListener m_serverSocket;
        private readonly Func<IDebugger, ISession> m_sessionFactory;

        private int m_serverStarted;
        private int m_serverShutDown;

        /// <summary>Port used by this server to listen for connections.</summary>
        public int Port { get; }

        private bool ServerShutDown
        {
            get { return Volatile.Read(ref m_serverShutDown) == True; }
            set { Volatile.Write(ref m_serverShutDown, value ? True : False); }
        }

        /// <nodoc/>
        public DebugServer(LoggingContext loggingContext, int port, Func<IDebugger, ISession> sessionFactory)
        {
            m_logger = Tracing.Logger.CreateLogger();
            m_loggingContext = loggingContext;
            m_sessionFactory = sessionFactory;

            Port = port;
            m_serverSocket = new TcpListener(IPAddress.Parse("127.0.0.1"), port);

            m_serverStarted = False;
            m_serverShutDown = False;
        }

        /// <summary>
        /// Asynchronously starts a task that runs until a debug client is connected.
        ///
        /// Must not be started twice; if so, <code cref="ContractException"/> is thrown.
        ///
        /// Creates a server socket and listens for a single connection from a single
        /// debug client.  As soon as a client is connected, a debugger
        /// (<code cref="IDebugger"/>) is created (which handles all subsequent
        /// communication with the client).
        /// </summary>
        public async Task<IDebugger> StartAsync()
        {
            int oldServerStarted = Interlocked.CompareExchange(ref m_serverStarted, True, False);
            Contract.Assert(oldServerStarted == False, "Attempted to start debug server twice.");

            IDebugger errorResult = null;

            try
            {
                m_serverSocket.Start();
                m_logger.ReportDebuggerServerStarted(m_loggingContext, Port);
                var clientSocket = await m_serverSocket.AcceptSocketAsync();
                m_logger.ReportDebuggerClientConnected(m_loggingContext);
                var remoteDebugger = new RemoteDebugger(m_loggingContext, clientSocket, m_sessionFactory);
                remoteDebugger.StartAsync();
                ShutDown();
                return remoteDebugger;
            }
            catch (SocketException e)
            {
                if (!ServerShutDown)
                {
                    m_logger.ReportDebuggerCannotOpenSocketError(m_loggingContext, e.Message);
                }

                return errorResult;
            }
            catch (Exception e)
            {
                if (!ServerShutDown)
                {
                    m_logger.ReportDebuggerServerGenericError(m_loggingContext, e.GetLogEventMessage(), e.StackTrace);
                }

                return errorResult;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        ///     Stops the server socket.
        /// </remarks>
        public void ShutDown()
        {
            if (m_serverStarted == False || ServerShutDown || m_serverSocket == null)
            {
                return;
            }

            ServerShutDown = true;
            m_logger.ReportDebuggerServerShutDown(m_loggingContext);
            try
            {
                m_serverSocket.Stop();
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // don't care about these
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }
    }
}
