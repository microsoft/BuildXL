// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// This interface serves as a code-first service declaration for protobuf-net gRPC. Clients may talk to a service
/// implementing this interface, which behind it servers a <see cref="IClusterStateStorage"/>.
/// </summary>
[Service("Cache.ClusterState")]
public interface IGrpcClusterStateStorage
{
    Task<IClusterStateStorage.RegisterMachineOutput> RegisterMachinesAsync(IClusterStateStorage.RegisterMachineInput request, CallContext callContext = default);

    Task<IClusterStateStorage.HeartbeatOutput> HeartbeatAsync(IClusterStateStorage.HeartbeatInput request, CallContext callContext = default);

    Task<ClusterStateMachine> ReadStateAsync(CallContext callContext = default);
}
