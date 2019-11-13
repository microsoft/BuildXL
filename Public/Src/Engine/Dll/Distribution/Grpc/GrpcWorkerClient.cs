// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using static BuildXL.Engine.Distribution.DistributionHelpers;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <nodoc/>
    internal sealed class GrpcWorkerClient : IWorkerClient
    {
        private readonly LoggingContext m_loggingContext;
        private readonly ClientConnectionManager m_connectionManager;
        private readonly Worker.WorkerClient m_client;
        private readonly Func<CancellationToken, Task<IDisposable>> m_attachAcquireAsync;

        public GrpcWorkerClient(LoggingContext loggingContext, string buildId, string ipAddress, int port, EventHandler onConnectionTimeOutAsync, Func<CancellationToken, Task<IDisposable>> attachAcquireAsync)
        {
            m_loggingContext = loggingContext;
            m_connectionManager = new ClientConnectionManager(loggingContext, ipAddress, port, buildId);
            m_connectionManager.OnConnectionTimeOutAsync += onConnectionTimeOutAsync;
            m_client = new Worker.WorkerClient(m_connectionManager.Channel);
            m_attachAcquireAsync = attachAcquireAsync;
        }

        public Task CloseAsync()
        {
            return m_connectionManager.CloseAsync();
        }

        public void Dispose()
        { }

        public Task<RpcCallResult<Unit>> AttachAsync(OpenBond.BuildStartData message)
        {
            var grpcMessage = message.ToGrpc();

            return m_connectionManager.CallAsync(
                async (callOptions) =>
                {
                    using (await m_attachAcquireAsync(callOptions.CancellationToken))
                    {
                        return await m_client.AttachAsync(grpcMessage, options: callOptions);
                    }
                },
                "Attach",
                waitForConnection: true);
        }

        public Task<RpcCallResult<Unit>> ExecutePipsAsync(OpenBond.PipBuildRequest message, IList<long> semiStableHashes)
        {
            var grpcMessage = message.ToGrpc();

            return m_connectionManager.CallAsync(
               async (callOptions) => await m_client.ExecutePipsAsync(grpcMessage, options: callOptions),
               GetExecuteDescription(semiStableHashes));
        }

        public Task<RpcCallResult<Unit>> ExitAsync(OpenBond.BuildEndData message, CancellationToken cancellationToken)
        {
            var grpcBuildEndData = new BuildEndData();

            if (message.Failure != null)
            {
                grpcBuildEndData.Failure = message.Failure;
            }

            return m_connectionManager.CallAsync(
                async (callOptions) => await m_client.ExitAsync(grpcBuildEndData, options: callOptions),
                "Exit",
                cancellationToken);
        }
    }
}