// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
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

/// <summary>
/// Server-side implementation of <see cref="IContentTracker"/>.
/// </summary>
/// <remarks>
/// This class expects a high volume of requests and is designed to avoid logging because of it. We only log
/// exceptions.
/// </remarks>
public class GrpcContentTrackerService : StartupShutdownComponentBase, IGrpcContentTracker
{
    /// <inheritdoc />
    protected override Tracer Tracer { get; } = new(nameof(GrpcContentTrackerService));

    private readonly IContentResolver _resolver;
    private readonly IContentUpdater _updater;

    private ILogger _logger = NullLogger.Instance;

    public GrpcContentTrackerService(IContentResolver resolver, IContentUpdater updater)
    {
        _resolver = resolver;
        _updater = updater;
        LinkLifetime(_resolver);
        LinkLifetime(_updater);
    }

    /// <inheritdoc />
    protected override Task<BoolResult> StartupComponentAsync(OperationContext context)
    {
        _logger = context.TracingContext.Logger;
        return base.StartupComponentAsync(context);
    }

    /// <inheritdoc />
    public async Task UpdateLocationsAsync(UpdateLocationsRequest request, CallContext callContext)
    {
        using var operationContext = CreateOperationContext(callContext);

        try
        {
            await _updater.UpdateLocationsAsync(operationContext, request).ThrowIfFailureAsync();
        }
        catch (Exception exception)
        {
            Tracer.Error(operationContext, exception, $"Failed to process request {request}");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<GetLocationsResponse> GetLocationsAsync(GetLocationsRequest request, CallContext callContext)
    {
        using var operationContext = CreateOperationContext(callContext);

        try
        {
            return await _resolver.GetLocationsAsync(operationContext, request).ThrowIfFailureAsync();
        }
        catch (Exception exception)
        {
            Tracer.Error(operationContext, exception, $"Failed to process request {request}");
            throw;
        }
    }

    private CancellableOperationContext CreateOperationContext(CallContext callContext)
    {
        var contextId = MetadataServiceSerializer.TryGetContextId(callContext.RequestHeaders);
        var tracingContext = contextId != null
            ? new Context(contextId, _logger)
            : new Context(_logger);

        var operationContext = new OperationContext(tracingContext, callContext.CancellationToken);
        return TrackShutdown(operationContext);
    }
}
