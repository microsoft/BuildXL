// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.ExternalApi.Commands;
using BuildXL.Ipc.Interfaces;

namespace Test.Tool.ServicePipDaemonTestUtilities
{
    /// <summary>
    /// A test double for <see cref="IClient"/>, used to stand up a dummy BuildXL API <see cref="Client"/> in daemon
    /// tests. When constructed with a send function it answers every operation with that function; when constructed
    /// with an inner client it delegates to that client, except for RegisterFilesForBuildManifest operations, which
    /// are always answered by the send function (defaulting to success).
    /// </summary>
    public sealed class MockClient : IClient
    {
        /// <nodoc/>
        public IClient InternalClient { get; set; }

        /// <nodoc/>
        public Task Completion => Task.CompletedTask;

        /// <nodoc/>
        public IClientConfig Config { get; set; } = new ClientConfig();

        /// <nodoc/>
        public Func<IIpcOperation, IIpcResult> SendHandler { get; set; }

        /// <nodoc/>
        public void Dispose() { }

        /// <nodoc/>
        public void RequestStop() { }

        /// <nodoc/>
        public MockClient(IClient client)
        {
            InternalClient = client;
            SendHandler = ipcOperation => IpcResult.Success("true");
        }

        /// <nodoc/>
        public MockClient(Func<IIpcOperation, IIpcResult> sendHandler)
        {
            InternalClient = null;
            SendHandler = sendHandler;
        }

        /// <inheritdoc/>
        Task<IIpcResult> IClient.Send(IIpcOperation operation)
        {
            Contract.Requires(operation != null);
            if (InternalClient != null)
            {
                if (Command.Deserialize(operation.Payload) is RegisterFilesForBuildManifestCommand)
                {
                    // Override for RegisterFilesForBuildManifestCommand (always answered by SendHandler).
                    return Task.FromResult(SendHandler(operation));
                }
                else
                {
                    return InternalClient.Send(operation);
                }
            }

            return Task.FromResult(SendHandler(operation));
        }

        /// <summary>
        /// Creates a dummy BuildXL API <see cref="Client"/> backed by a <see cref="MockClient"/> that routes
        /// operations to an in-memory IPC server reached via <paramref name="moniker"/>.
        /// </summary>
        public static Client CreateDummyApiClient(IIpcProvider ipcProvider, IpcMoniker moniker)
            => new Client(new MockClient(ipcProvider.GetClient(ipcProvider.RenderConnectionString(moniker), new ClientConfig())));

        /// <summary>
        /// Creates a dummy BuildXL API <see cref="Client"/> backed by a <see cref="MockClient"/> on a fresh connection.
        /// </summary>
        public static Client CreateDummyApiClient(IIpcProvider ipcProvider)
            => new Client(new MockClient(ipcProvider.GetClient(ipcProvider.CreateNewConnectionString(), new ClientConfig())));
    }
}
