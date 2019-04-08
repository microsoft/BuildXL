// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Ipc.Interfaces;
using JetBrains.Annotations;

namespace BuildXL.Ipc.Common.Multiplexing
{
    /// <summary>
    /// Generic "Server" implementation that implements the following data flow:
    ///
    ///                1   +---------------+       +-------------+    n
    /// listener() +---/---> ListenerBlock +-------> ActionBlock | ---/---> handler()
    ///                    +---------------+       +-------------+
    ///
    /// A listener() function (provided via the constructor) is never executed concurrently.
    ///
    /// The handler() function (provided via the <see cref="Start"/> method)
    /// may be invoked concurrently, depending on the <see cref="IServerConfig.MaxConcurrentClients"/>
    /// property of the <see cref="Config"/> object.
    /// </summary>
    public sealed class GenericServer<TClient>
    {
        private readonly ListenerSourceBlock<TClient> m_clientListenerBlock;
        private readonly ConcurrentQueue<Exception> m_diagnostics;

        /// <summary>Handler that gets set in the <see cref="Start"/> method.</summary>
        private Func<TClient, Task> m_handler;

        /// <summary>Arbitrary name only for descriptive purposes.</summary>
        public string Name { get; }

        /// <nodoc/>
        public IServerConfig Config { get; }

        /// <nodoc/>
        public ILogger Logger { get; }

        /// <nodoc/>
        internal ConcurrentQueue<Exception> Diagnostics => m_diagnostics;

        /// <nodoc/>
        public GenericServer([CanBeNull]string name, IServerConfig config, ListenerSourceBlock<TClient>.CancellableListener listener, bool clientFailuresAreFatal = false)
        {
            Contract.Requires(config != null);

            Config = config;
            Logger = config.Logger ?? VoidLogger.Instance;
            Name = name ?? "GenericServer";

            m_diagnostics = new ConcurrentQueue<Exception>();

            // in an event loop, wait for clients to connect via the 'AcceptClient' method
            m_clientListenerBlock = new ListenerSourceBlock<TClient>(listener, name + ".EventLoop", Logger);

            // handle each connected client via the 'ClientHandler' method
            var clientHandlerAction = clientFailuresAreFatal
                ? (Func<TClient, Task>)ClientHandlerWithoutExceptionHandling
                : (Func<TClient, Task>)ClientHandlerWithExceptionHandling;
            var clientHandlerBlock = new ActionBlock<TClient>(
                clientHandlerAction,
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = config.MaxConcurrentClients,
                });

            // link the event loop to feed the clientHandler ActionBlock
            m_clientListenerBlock.LinkTo(clientHandlerBlock, propagateCompletion: true);
        }

        /// <summary>
        /// Starts the underlying listener block (<see cref="ListenerSourceBlock{TOutput}.Start"/>), which
        /// will listen for clients using the <see cref="BuildXL.Ipc.Common.Connectivity.IConnectivityProvider{TClient}.AcceptClientAsync"/> method
        /// of the <see cref="BuildXL.Ipc.Common.Connectivity.IConnectivityProvider{TClient}"/> provided in the constructor of this class.
        ///
        /// In response to every client the listener receives, the <paramref name="handler"/> action is called.
        /// </summary>
        /// <remarks>
        /// The <paramref name="handler"/> function may be called concurrently, as determined by the
        /// <see cref="IServerConfig.MaxConcurrentClients"/> property of the provided <see cref="Config"/>.
        /// </remarks>
        public void Start(Func<TClient, Task> handler)
        {
            Contract.Requires(handler != null);

            m_handler = handler;
            m_clientListenerBlock.Start();
        }

        /// <summary>
        /// Forwards the request to the underlying <see cref="ListenerSourceBlock{TOutput}"/>.
        /// </summary>
        public void Complete()
        {
            m_clientListenerBlock.Complete();
        }

        /// <summary>
        /// Whether stop has been requested (via <see cref="StopRequested"/>).
        /// </summary>
        public bool StopRequested => m_clientListenerBlock.StopRequested;

        /// <remarks>
        /// When created, a server is in a "not completed" state.  After <see cref="Complete"/>
        /// has been called, the server completes when all clients have been handled.
        /// </remarks>
        public Task Completion => m_clientListenerBlock.TargetBlock.Completion;

        /// <summary>
        /// This task completes as soon as <see cref="Complete"/> is called.  This server, however,
        /// doesn't complete (<see cref="Completion"/>) until all pending clients have been handled.
        /// </summary>
        public Task ClientListenerBlockCompletion => m_clientListenerBlock.Completion;

        /// <summary>
        /// Returns whether this server has been started.
        /// </summary>
        public bool IsStarted => m_clientListenerBlock.IsStarted;

        /// <summary>
        /// Disposing a server after it starts and before it completes is not allowed.
        /// </summary>
        public void Dispose()
        {
            if (IsStarted && !Completion.IsCompleted)
            {
                throw new IpcException(IpcException.IpcExceptionKind.DisposeBeforeCompletion);
            }
        }

        private Task ClientHandlerWithoutExceptionHandling(TClient client)
        {
            return m_handler(client);
        }

        private async Task ClientHandlerWithExceptionHandling(TClient client)
        {
            try
            {
                await ClientHandlerWithoutExceptionHandling(client);
            }
            catch (Exception e)
            {
                // catching all exceptions when failing to handle a client should not bring the whole server down
                e = (e as AggregateException)?.InnerException ?? e;
                Logger.Warning("({0}) Handling client '{1}' failed: {2}", Name, client, e.Message);
                m_diagnostics.Enqueue(e);
            }
        }
    }
}
