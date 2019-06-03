// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Engine.Distribution
{
    internal interface IWorkerClient : IDisposable
    {
        Task<RpcCallResult<Unit>> AttachAsync(BuildStartData startData);

        Task<RpcCallResult<Unit>> ExecutePipsAsync(PipBuildRequest input, IList<long> semiStableHashes);

        Task<RpcCallResult<Unit>> ExitAsync(BuildEndData buildEndData, CancellationToken cancellationToken);

        Task CloseAsync();
    }
}