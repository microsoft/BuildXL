// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc;

public interface IGrpcServerHost<TConfiguration>
{
    public Task<BoolResult> StartAsync(OperationContext context, TConfiguration configuration, IEnumerable<IGrpcServiceEndpoint> endpoints);

    public Task<BoolResult> StopAsync(OperationContext context, TConfiguration configuration);
}
