// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// This interface serves as a code-first service declaration for protobuf-net gRPC. Clients may talk to a service
/// implementing this interface, which behind it serves a <see cref="IContentTracker"/>.
///
/// Server-side component: <see cref="GrpcContentTrackerService"/>
/// Client-side component: <see cref="GrpcContentTrackerClient"/>
/// 
/// CODESYNC: <see cref="IContentTracker"/>
/// </summary>
/// <remarks>
/// These methods do not return errors. The reason for this is that gRPC.NET will throw an exception on the client-side
/// if there's a server-side exception.
/// </remarks>
[Service("Cache.ContentTracker")]
public interface IGrpcContentTracker
{
    /// <inheritdoc cref="IContentTracker.UpdateLocationsAsync" />
    public Task UpdateLocationsAsync(UpdateLocationsRequest request, CallContext callContext);

    /// <inheritdoc cref="IContentTracker.GetLocationsAsync" />
    public Task<GetLocationsResponse> GetLocationsAsync(GetLocationsRequest request, CallContext context);
}
