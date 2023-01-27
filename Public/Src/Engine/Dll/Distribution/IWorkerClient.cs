// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Engine.Distribution
{
    internal interface IWorkerClient : IDisposable
    {
        Task<RpcCallResult<Unit>> AttachAsync(BuildStartData startData, CancellationToken cancellationToken);

        Task<RpcCallResult<Unit>> ExecutePipsAsync(PipBuildRequest input, string description, CancellationToken cancellationToken = default);

        Task<RpcCallResult<Unit>> ExitAsync(BuildEndData buildEndData, CancellationToken cancellationToken);

        void SetWorkerLocation(ServiceLocation serviceLocation);

        bool TryFinalizeStreaming();

        Task CloseAsync();
    }
}