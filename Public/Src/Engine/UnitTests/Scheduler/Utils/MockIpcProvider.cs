// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Tasks;

namespace Test.BuildXL.Scheduler.Utils
{
    /// <summary>
    /// Mock implementation that has a settable property for each property/method in the <see cref="IIpcProvider"/> interface.
    /// </summary>
    internal sealed class MockIpcProvider : IIpcProvider
    {
        internal Func<IIpcMoniker> CreateMonikerFn { get; set; } = new Func<IIpcMoniker>(() => new StringMoniker(Guid.NewGuid().ToString()));
        internal Func<string, IIpcMoniker> LoadMonikerFn { get; set; } = new Func<string, IIpcMoniker>(id => new StringMoniker(id));
        internal Func<IIpcMoniker, string> RenderMonikerFn { get; set; } = new Func<IIpcMoniker, string>(m => m.Id);
        internal Func<string, IClientConfig, IClient> GetClientFn { get; set; } = new Func<string, IClientConfig, IClient>((_, c) => new MockClient { Config = c });
        internal Func<string, IServerConfig, IServer> GetServerFn { get; set; } = new Func<string, IServerConfig, IServer>((_, c) => new MockServer { Config = c });

        IIpcMoniker IIpcProvider.CreateNewMoniker() => CreateMonikerFn();
        IIpcMoniker IIpcProvider.LoadOrCreateMoniker(string monikerId) => LoadMonikerFn(monikerId);
        string IIpcProvider.RenderConnectionString(IIpcMoniker moniker) => RenderMonikerFn(moniker);
        IClient IIpcProvider.GetClient(string connectionString, IClientConfig config) => GetClientFn(connectionString, config);
        IServer IIpcProvider.GetServer(string connectionString, IServerConfig config) => GetServerFn(connectionString, config);
    }

    /// <summary>
    /// Mock implementation that has a settable property for each property/method in the <see cref="IStoppable"/> interface.
    /// </summary>
    internal class MockStoppable : IStoppable
    {
        internal Task Completion { get; set; } = Unit.VoidTask;
        internal Action DisposeFn { get; set; } = new Action(() => { });
        internal Action RequestStopFn { get; set; } = new Action(() => { });

        Task IStoppable.Completion => Completion;
        void IDisposable.Dispose() => DisposeFn();
        void IStoppable.RequestStop() => RequestStopFn();
    }

    /// <summary>
    /// Mock implementation that has a settable property for each property/method in the <see cref="IClient"/> interface.
    /// </summary>
    internal sealed class MockClient : MockStoppable, IClient
    {
        internal IClientConfig Config { get; set; } = new ClientConfig();
        internal Func<IIpcOperation, Task<IIpcResult>> SendFn { get; set; } = new Func<IIpcOperation, Task<IIpcResult>>(_ => Task.FromResult(IpcResult.Success()));
        
        IClientConfig IClient.Config => Config;
        Task<IIpcResult> IClient.Send(IIpcOperation operation)
        {
            Contract.Requires(operation != null);
            return SendFn(operation);
        }
    }

    /// <summary>
    /// Mock implementation that has a settable property for each property/method in the <see cref="IServer"/> interface.
    /// </summary>
    internal sealed class MockServer : MockStoppable, IServer
    {
        internal IServerConfig Config { get; set; } = new ServerConfig();
        internal Action<IIpcOperationExecutor> StartFn { get; set; } = new Action<IIpcOperationExecutor>(_ => { });

        IServerConfig IServer.Config => Config;
        void IServer.Start(IIpcOperationExecutor executor)
        {
            Contract.Requires(executor != null);
            StartFn(executor);
        }
    }
}
