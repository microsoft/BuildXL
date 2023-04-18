// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using ProtoBuf.Grpc;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

public class GrpcClusterStateStorageService : StartupShutdownComponentBase, IGrpcClusterStateStorage
{
    protected override Tracer Tracer { get; } = new(nameof(GrpcClusterStateStorageService));

    private readonly IClusterStateStorage _clusterStateStorage;

    private ILogger _logger = NullLogger.Instance;

    public GrpcClusterStateStorageService(IClusterStateStorage clusterStateStorage)
    {
        _clusterStateStorage = clusterStateStorage;
        LinkLifetime(_clusterStateStorage);
    }

    protected override Task<BoolResult> StartupComponentAsync(OperationContext context)
    {
        _logger = context.TracingContext.Logger;
        return base.StartupComponentAsync(context);
    }

    public Task<IClusterStateStorage.RegisterMachineOutput> RegisterMachinesAsync(IClusterStateStorage.RegisterMachineInput request, CallContext callContext = default)
    {
        var operationContext = CreateOperationContext(callContext);
        return _clusterStateStorage.RegisterMachinesAsync(operationContext, request).ThrowIfFailureAsync();
    }

    public Task<IClusterStateStorage.HeartbeatOutput> HeartbeatAsync(IClusterStateStorage.HeartbeatInput request, CallContext callContext = default)
    {
        var operationContext = CreateOperationContext(callContext);
        return _clusterStateStorage.HeartbeatAsync(operationContext, request).ThrowIfFailureAsync();
    }

    public Task<ClusterStateMachine> ReadStateAsync(CallContext callContext = default)
    {
        var operationContext = CreateOperationContext(callContext);
        return _clusterStateStorage.ReadStateAsync(operationContext).ThrowIfFailureAsync();
    }

    private OperationContext CreateOperationContext(CallContext callContext)
    {
        var contextId = MetadataServiceSerializer.TryGetContextId(callContext.RequestHeaders);
        var tracingContext = contextId != null
            ? new Context(contextId, _logger)
            : new Context(_logger);

        var operationContext = new OperationContext(tracingContext, callContext.CancellationToken);
        return operationContext;
    }
}
