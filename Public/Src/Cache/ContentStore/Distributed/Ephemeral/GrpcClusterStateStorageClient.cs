// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

public class GrpcClusterStateStorageClient : GrpcCodeFirstClient<IGrpcClusterStateStorage>, IClusterStateStorage
{
    public record Configuration(TimeSpan OperationTimeout, RetryPolicyConfiguration RetryPolicy);

    protected override Tracer Tracer { get; } = new(nameof(GrpcClusterStateStorageClient));

    public GrpcClusterStateStorageClient(Configuration configuration, IFixedClientAccessor<IGrpcClusterStateStorage> accessor, IClock clock)
        : base(accessor, CreateRetryPolicy(configuration.RetryPolicy), clock, configuration.OperationTimeout)
    {
    }

    private static IRetryPolicy CreateRetryPolicy(RetryPolicyConfiguration configurationRetryPolicy)
    {
        return configurationRetryPolicy.AsRetryPolicy(_ => true,
            // We use an absurdly high retry count because the actual operation timeout is controlled through
            // PerformOperationAsync in ExecuteAsync.
            1_000_000);
    }

    public Task<Result<IClusterStateStorage.HeartbeatOutput>> HeartbeatAsync(OperationContext context, IClusterStateStorage.HeartbeatInput request)
    {
        return ExecuteAsync(
            context,
            async (context, options, service) => Result.Success(await service.HeartbeatAsync(request, options)),
            extraEndMessage: _ => string.Empty);
    }

    public Task<Result<ClusterStateMachine>> ReadStateAsync(OperationContext context)
    {
        return ExecuteAsync(
            context,
            async (context, options, service) => Result.Success(await service.ReadStateAsync(options)),
            extraEndMessage: _ => string.Empty);
    }

    public Task<Result<IClusterStateStorage.RegisterMachineOutput>> RegisterMachinesAsync(OperationContext context, IClusterStateStorage.RegisterMachineInput request)
    {
        return ExecuteAsync(
            context,
            async (context, options, service) => Result.Success(await service.RegisterMachinesAsync(request, options)),
            extraEndMessage: _ => string.Empty);
    }
}
