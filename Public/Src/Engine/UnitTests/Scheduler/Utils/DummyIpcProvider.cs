// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Tasks;

namespace Test.BuildXL.Scheduler.Utils
{
    /// <summary>
    /// A provider that always returns unique monikers and <see cref="DummyIpcServer"/> and <see cref="DummyEchoingIpcClient"/>.
    /// </summary>
    internal sealed class DummyIpcProvider : IIpcProvider
    {
        private int m_monikerCounter = 0;
        private readonly IpcResultStatus m_statusToAlwaysReturn;

        internal DummyIpcProvider(IpcResultStatus statusToAlwaysReturn = IpcResultStatus.Success)
        {
            m_statusToAlwaysReturn = statusToAlwaysReturn;
        }

        /// <inheritdoc />
        public IIpcMoniker CreateNewMoniker() => new StringMoniker(Interlocked.Increment(ref m_monikerCounter).ToString(CultureInfo.InvariantCulture));

        /// <inheritdoc />
        public IIpcMoniker LoadOrCreateMoniker(string id) => new StringMoniker(id);

        /// <inheritdoc />
        public string RenderConnectionString(IIpcMoniker moniker) => moniker.Id;

        /// <inheritdoc />
        public IClient GetClient(string connectionString, IClientConfig config) => new DummyEchoingIpcClient(m_statusToAlwaysReturn);

        /// <inheritdoc />
        public IServer GetServer(string connectionString, IServerConfig config) => DummyIpcServer.Instance;
    }

    /// <summary>
    /// A server that does absolutely nothing.  It's always completed.
    /// </summary>
    internal sealed class DummyIpcServer : IServer
    {
        internal static readonly DummyIpcServer Instance = new DummyIpcServer();

        public Task Completion => global::BuildXL.Utilities.Tasks.Unit.VoidTask;

        public IServerConfig Config => null;

        public void Dispose() { }

        public void RequestStop() { }

        public void Start(IIpcOperationExecutor executor) { Contract.Requires(executor != null); }
    }

    /// <summary>
    /// A client that doesn't talk to any server and instead always returns 
    /// <see cref="IIpcResult.Success(string)"/> echoing the payload received in <see cref="IIpcOperation"/> 
    /// </summary>
    internal sealed class DummyEchoingIpcClient : IClient
    {
        private readonly IpcResultStatus m_status;

        internal DummyEchoingIpcClient(IpcResultStatus statusToAlwaysReturn)
        {
            m_status = statusToAlwaysReturn;
        }

        public IClientConfig Config => null;

        void IStoppable.RequestStop() { }

        Task IStoppable.Completion => Unit.VoidTask;

        public void Dispose() { }

        /// <summary>
        /// Always returns <see cref="IIpcResult.Success(string)"/> echoing the payload received in <paramref name="operation"/>.
        /// </summary>
        public Task<IIpcResult> Send(IIpcOperation operation)
        {
            Contract.Requires(operation != null);
            return Task.FromResult((IIpcResult) new IpcResult(m_status, operation.Payload));
        }
    }
}
