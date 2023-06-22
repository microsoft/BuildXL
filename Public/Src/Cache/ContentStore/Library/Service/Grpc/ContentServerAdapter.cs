// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using ContentStore.Grpc;
using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service.Grpc;

/// <summary>
/// gRPC generates a stub class (<see cref="ContentServer.ContentServerBase"/>) that all gRPC services must
/// implement. When binding the service to the gRPC server (via <see cref="IGrpcServiceEndpoint"/>), the server
/// will call the methods on the stub class (i.e., this implementation) when a client calls the corresponding
/// gRPC method.
/// </summary>
/// <remarks>
/// This adapter only implements the copy verbs, and is intended for usages where we're only interested in allowing P2P
/// copies.
/// </remarks>
public class CopyServerAdapter : ContentServer.ContentServerBase, IGrpcServiceEndpoint
{
    private readonly GrpcCopyServer _copyServer;

    /// <inheritdoc />
    public CopyServerAdapter(GrpcCopyServer contentServer)
    {
        _copyServer = contentServer;
    }

    /// <inheritdoc />
    public void BindServices(Server.ServiceDefinitionCollection services)
    {
        services.Add(ContentServer.BindService(this));
    }

    /// <inheritdoc />
    public void MapServices(IGrpcServiceEndpointCollection endpoints)
    {
        endpoints.MapService<CopyServerAdapter>();
    }

    /// <inheritdoc />
    public void AddServices(IGrpcServiceCollection services)
    {
        services.AddService(this);
    }

    /// <inheritdoc />
    public override Task CopyFile(CopyFileRequest request, IServerStreamWriter<CopyFileResponse> responseStream, ServerCallContext context) => _copyServer.HandleCopyRequestAsync(request, responseStream, context);
}

/// <inheritdoc />
/// <remarks>
/// This adapter only implements the content verbs, and will throw an unimplemented exception when a client
/// calls an unavailable method (specifically, any metadata verb!).
/// </remarks>
public class ContentServerAdapter : CopyServerAdapter
{
    private readonly GrpcContentServer _contentServer;

    public ContentServerAdapter(GrpcContentServer contentServer)
        : base(contentServer)
    {
        _contentServer = contentServer;
    }

    /// <inheritdoc />
    public override Task<ExistenceResponse> CheckFileExists(ExistenceRequest request, ServerCallContext context) => throw new NotSupportedException("The operation 'CheckFileExists' is not supported.");

    /// <inheritdoc />
    public override Task<RequestCopyFileResponse> RequestCopyFile(RequestCopyFileRequest request, ServerCallContext context) => _contentServer.RequestCopyFileAsync(request, context.CancellationToken);

    /// <inheritdoc />
    public override Task PushFile(IAsyncStreamReader<PushFileRequest> requestStream, IServerStreamWriter<PushFileResponse> responseStream, ServerCallContext context) => _contentServer.HandlePushFileAsync(requestStream, responseStream, context);

    /// <inheritdoc />
    public override Task<HelloResponse> Hello(HelloRequest request, ServerCallContext context) => _contentServer.HelloAsync(request, context.CancellationToken);

    /// <inheritdoc />
    public override Task<GetStatsResponse> GetStats(GetStatsRequest request, ServerCallContext context) => _contentServer.GetStatsAsync(request, context.CancellationToken);

    /// <inheritdoc />
    public override Task<CreateSessionResponse> CreateSession(CreateSessionRequest request, ServerCallContext context) => _contentServer.CreateSessionAsync(request, context.CancellationToken);

    /// <inheritdoc />
    public override Task<DeleteContentResponse> Delete(DeleteContentRequest request, ServerCallContext context) => _contentServer.DeleteAsync(request, context.CancellationToken);

    /// <inheritdoc />
    public override Task<PinResponse> Pin(PinRequest request, ServerCallContext context) => _contentServer.PinAsync(request, context.CancellationToken);

    /// <inheritdoc />
    public override Task<PlaceFileResponse> PlaceFile(PlaceFileRequest request, ServerCallContext context) => _contentServer.PlaceFileAsync(request, context.CancellationToken);

    /// <inheritdoc />
    public override Task<PinBulkResponse> PinBulk(PinBulkRequest request, ServerCallContext context) => _contentServer.PinBulkAsync(request, context.CancellationToken);

    /// <inheritdoc />
    public override Task<PutFileResponse> PutFile(PutFileRequest request, ServerCallContext context) => _contentServer.PutFileAsync(request, context.CancellationToken);

    /// <inheritdoc />
    public override Task<ShutdownResponse> ShutdownSession(ShutdownRequest request, ServerCallContext context) => _contentServer.ShutdownSessionAsync(request, context.CancellationToken);

    /// <inheritdoc />
    public override Task<HeartbeatResponse> Heartbeat(HeartbeatRequest request, ServerCallContext context) => _contentServer.HeartbeatAsync(request, context.CancellationToken);

    /// <inheritdoc />
    public override Task<RemoveFromTrackerResponse> RemoveFromTracker(RemoveFromTrackerRequest request, ServerCallContext context) => _contentServer.RemoveFromTrackerAsync(request, context.CancellationToken);
}
