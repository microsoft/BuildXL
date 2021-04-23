// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Utilities.Tasks;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;

namespace BuildXL.Engine.Distribution
{
    internal interface IOrchestratorClient
    {
        void Initialize(string ipAddress, int port, EventHandler<ConnectionTimeoutEventArgs> onConnectionTimeOutAsync);        
        Task<RpcCallResult<Unit>> AttachCompletedAsync(AttachCompletionInfo attachCompletionInfo);
        Task<RpcCallResult<Unit>> NotifyAsync(WorkerNotificationArgs notificationArgs, IList<long> semiStableHashes, CancellationToken cancellationToken = default);
        Task CloseAsync();
    }
}