// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <nodoc/>
    internal sealed class GrpcMasterClient : IMasterClient
    {
        private readonly Master.MasterClient m_client;
        private readonly ClientConnectionManager m_connectionManager;
        private readonly LoggingContext m_loggingContext;

        public GrpcMasterClient(LoggingContext loggingContext, string buildId, string ipAddress, int port)
        {
            m_loggingContext = loggingContext;
            m_connectionManager = new ClientConnectionManager(m_loggingContext, ipAddress, port, buildId);
            m_client = new Master.MasterClient(m_connectionManager.Channel);
        }

        public void Close()
        {
            m_connectionManager.Close();
        }

        public Task<RpcCallResult<Unit>> AttachCompletedAsync(OpenBond.AttachCompletionInfo message)
        {
            var grpcMessage = message.ToGrpc();
            return m_connectionManager.CallAsync(
                (callOptions) => m_client.AttachCompletedAsync(grpcMessage, options: callOptions),
                "AttachCompleted",
                waitForConnection: true);
        }

        public Task<RpcCallResult<Unit>> NotifyAsync(OpenBond.WorkerNotificationArgs message, IList<long> semiStableHashes)
        {
            var grpcMessage = message.ToGrpc();
            return m_connectionManager.CallAsync(
               (callOptions) => m_client.NotifyAsync(grpcMessage, options: callOptions),
               DistributionHelpers.GetNotifyDescription(message, semiStableHashes));
        }
    }
}