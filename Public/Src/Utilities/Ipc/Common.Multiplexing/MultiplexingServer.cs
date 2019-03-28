// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ipc.Common.Multiplexing
{
    /// <summary>
    /// This server can process multiple clients concurrently, as well as multiple
    /// requests from a single client (over a shared client stream).
    ///
    /// This server uses a single <see cref="GenericServer{TClient}"/> object (client
    /// listener) for accepting/processing clients; for each accepted client it creates
    /// another <see cref="GenericServer{Request}"/> object for accepting/processing
    /// requests from that client.
    ///
    /// The data flow is:
    ///
    ///                                                      ||    AcceptRequestAsync()
    ///                                                      ||            | 1
    ///                                                      ||            V
    ///                                                      ||    +-------+-------+
    ///                                                      ||    | GenericServer |
    ///                              maxConcurrentClients    ||    |   {Request}   |
    ///                                                 \    ||    +-------+-------+
    ///                                                 |    ||            | maxConcurrentRequestsPerClient
    ///                            +----------------+   |    ||            V
    /// ClientProvider.       1    | GenericServer  |   |    ||      ExecuteRequest()
    /// AcceptClientAsync() --/--> |   {TClient}    | --/--> ||            |
    ///                            +----------------+        ||            V
    ///                                                      ||     +------+------+
    ///                                                      ||     | ActionBlock |
    ///                                                      ||     | {Response}  |
    ///                                                      ||     +------+------+
    ///                                                      ||            | 1
    ///                                                      ||            V
    ///                                                      ||    SendResponseAsync()
    ///
    /// This server completes when the "client listener" server completes.
    ///
    /// When a server for processing requests from a particular client fails (e.g., because
    /// that client disconnected abruptly), this server continues to run and serve other clients.
    /// </summary>
    /// <typeparam name="TClient">Type of the client.</typeparam>
    public sealed class MultiplexingServer<TClient> : IServer where TClient : IDisposable
    {
        private readonly IConnectivityProvider<TClient> m_connectivityProvider;
        private readonly IServerConfig m_clientHandlingConfig;
        private readonly IServerConfig m_requestHandlingConfig;

        // outer client listening/handling
        private readonly GenericServer<TClient> m_clientListener;

        /// <summary>Arbitrary name only for descriptive purposes.</summary>
        public string Name { get; }

        /// <nodoc/>
        internal ConcurrentQueue<Exception> Diagnostics => m_clientListener.Diagnostics;

        private ILogger Logger { get; }

        private IIpcOperationExecutor m_executor;

        /// <nodoc/>
        public MultiplexingServer([CanBeNull]string name, [CanBeNull]ILogger logger, IConnectivityProvider<TClient> connectivityProvider, int maxConcurrentClients, int maxConcurrentRequestsPerClient)
        {
            Contract.Requires(connectivityProvider != null);
            Contract.Requires(maxConcurrentClients > 0);
            Contract.Requires(maxConcurrentRequestsPerClient > 0);

            Name = name ?? GetType().Name;
            Logger = logger ?? VoidLogger.Instance;

            m_connectivityProvider = connectivityProvider;

            m_clientHandlingConfig = new ServerConfig { Logger = Logger, MaxConcurrentClients = maxConcurrentClients };
            m_requestHandlingConfig = new ServerConfig { Logger = Logger, MaxConcurrentClients = maxConcurrentRequestsPerClient };

            m_clientListener = new GenericServer<TClient>(
                name: Name + ".ClientHandler",
                config: m_clientHandlingConfig,
                listener: (token) => connectivityProvider.AcceptClientAsync(token));
        }

        /// <summary>
        /// Starts the client listener server.
        /// </summary>
        public void Start(IIpcOperationExecutor executor)
        {
            Contract.Requires(executor != null);

            m_executor = executor;
            m_clientListener.Start(ClientHandlerAsync);
        }

        /// <nodoc/>
        public void Dispose()
        {
            Logger.Verbose("{0} Disposing...", Name);
            m_clientListener.Dispose();
            m_connectivityProvider.Dispose();
            Logger.Verbose("{0} Disposed.", Name);
        }

        #region IServer Implementation Through m_clientListener field

        /// <see cref="GenericServer{TClient}.Completion"/>
        public Task Completion => m_clientListener.Completion;

        /// <see cref="GenericServer{TClient}.Config"/>
        public IServerConfig Config => m_clientListener.Config;

        /// <see cref="GenericServer{TClient}.Complete"/>
        public void RequestStop() => m_clientListener.Complete();
        #endregion

        #region Client Handling Methods
        private async Task ClientHandlerAsync(TClient client)
        {
            using (client)
            using (var requestHandler = new RequestHandler(this, client))
            {
                requestHandler.StartServer();
                Analysis.IgnoreResult(await Task.WhenAny(m_clientListener.ClientListenerBlockCompletion, requestHandler.Completion));
                requestHandler.RequestStop();
                await requestHandler.Completion;
            }
        }
        #endregion

        /// <summary>
        /// Inner class for handling requests from a given client.
        /// </summary>
        private sealed class RequestHandler : IDisposable
        {
            private readonly MultiplexingServer<TClient> m_parent;
            private readonly TClient m_client;
            private readonly Stream m_stream;
            private readonly Task m_completion;

            private readonly GenericServer<Request> m_requestListener;
            private readonly ActionBlock<Response> m_sendResponseBlock;

            private bool m_stopRequestedByClient;

            private string Name { get; }

            private ILogger Logger => m_parent.Logger;

            /// <nodoc/>
            public RequestHandler(MultiplexingServer<TClient> parent, TClient client)
            {
                m_parent = parent;
                m_client = client;
                m_stream = parent.m_connectivityProvider.GetStreamForClient(client);

                Name = I($"{parent.Name}.RequestHandler({parent.m_connectivityProvider.Describe(client)})");

                m_stopRequestedByClient = false;

                m_requestListener = new GenericServer<Request>(
                    name: Name,
                    config: m_parent.m_requestHandlingConfig,
                    listener: AcceptRequestAsync);

                m_sendResponseBlock = new ActionBlock<Response>(
                    SendResponseAsync,
                    new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 }); // streaming must be sequential

                // chain block completions: either one completes the other
                var continuationDone = TaskSourceSlim.Create<Unit>();
                m_requestListener.Completion.ContinueWith(_ => m_sendResponseBlock.Complete());
                m_sendResponseBlock.Completion.ContinueWith(async _ =>
                {
                    m_requestListener.Complete();
                    if (!m_stopRequestedByClient)
                    {
                        var succeeded = await Response.DisconnectResponse.TrySerializeAsync(m_stream);
                        Logger.Verbose("({0}) Sending DisconnectResponse {1}.", Name, succeeded ? "Succeeded" : "Failed");
                    }

                    Logger.Verbose("({0}) Disconnecting client...", Name);
                    bool ok = TryDisconnectClient(m_client);
                    Logger.Verbose("({0}) Disconnecting client {1}.", Name, ok ? "Succeeded" : "Failed");
                    continuationDone.SetResult(Unit.Void);
                });

                // set the completion task to be the completion of both listener and sender blocks, as well as the cleanup continuation
                m_completion = TaskUtilities.SafeWhenAll(new[] { m_requestListener.Completion, m_sendResponseBlock.Completion, continuationDone.Task });
            }

            internal void StartServer() => m_requestListener.Start(ExecuteRequestAsync);

            internal Task Completion => m_completion;

            internal void RequestStop(string logMessage = null)
            {
                Logger.Verbose(logMessage ?? "({0}) Stop requested", Name);
                m_requestListener.Complete();
            }

            /// <nodoc/>
            public void Dispose()
            {
                Logger.Verbose("({0}) Disposing...", Name);
                m_requestListener.Dispose();
                m_stream.Dispose();
                Logger.Verbose("({0}) Disposed.", Name);
            }

            internal async Task<Request> AcceptRequestAsync(CancellationToken token)
            {
                Logger.Verbose("({0}) Waiting to receive request...", Name);
                var request = await Request.DeserializeAsync(m_stream, token);
                Logger.Verbose("({0}) Request received: {1}", Name, request);

                request.Operation.Timestamp.Daemon_AfterReceivedTime = DateTime.UtcNow;

                if (request.IsStopRequest)
                {
                    m_stopRequestedByClient = true;
                    RequestStop(I($"({Name}) Request to STOP received!"));
                }

                return request;
            }

            internal async Task ExecuteRequestAsync(Request request)
            {
                request.Operation.Timestamp.Daemon_BeforeExecuteTime = DateTime.UtcNow;

                Logger.Verbose("({0}) Executing request {1}", Name, request);
                var ipcResult = await Utils.HandleExceptionsAsync(
                    IpcResultStatus.ExecutionError,
                    () => m_parent.m_executor.ExecuteAsync(request.Operation));

                if (request.Operation.ShouldWaitForServerAck)
                {
                    var response = new Response(request.Id, ipcResult);
                    Logger.Verbose("({0}) Posting response {1}", Name, response);
                    m_sendResponseBlock.Post(response);
                }
                else
                {
                    Logger.Verbose("({0}) Response not requested for request {1}", Name, request);
                }
            }

            private async Task SendResponseAsync(Response response)
            {
                Logger.Verbose("({0}) Sending response {1} ...", Name, response);
                await response.SerializeAsync(m_stream);
                Logger.Verbose("({0}) Response sent: {1}", Name, response);
            }

            private bool TryDisconnectClient(TClient client)
            {
                try
                {
                    m_parent.m_connectivityProvider.DisconnectClient(client);
                    return true;
                }
                catch (Exception e)
                {
                    Logger.Warning("({0}) Ignoring exception thrown while trying to disconnect client: {1}", Name, e);
                    return false;
                }
            }
        }
    }
}
