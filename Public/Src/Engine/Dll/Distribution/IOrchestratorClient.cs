// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Tasks;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;

namespace BuildXL.Engine.Distribution
{
    internal interface IOrchestratorClient
    {
        Task<RpcCallResult<Unit>> SayHelloAsync(ServiceLocation serviceLocation, CancellationToken cancellationToken = default);
        void Initialize(string ipAddress, int port, EventHandler<ConnectionFailureEventArgs> onConnectionFailureAsync);
        Task<RpcCallResult<Unit>> AttachCompletedAsync(AttachCompletionInfo attachCompletionInfo);
        Task<RpcCallResult<Unit>> ReportPipResultsAsync(PipResultsInfo message, string description, CancellationToken cancellationToken = default);
        Task<RpcCallResult<Unit>> ReportExecutionLogAsync(ExecutionLogInfo message, CancellationToken cancellationToken = default);
        Task CloseAsync();
        void FinalizeStreaming();
    }
}