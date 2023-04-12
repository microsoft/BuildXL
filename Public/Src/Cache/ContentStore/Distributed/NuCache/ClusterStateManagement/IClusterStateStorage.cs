// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;

public interface IClusterStateStorage: IStartupShutdownSlim
{
    public record RegisterMachineInput(IReadOnlyList<MachineLocation> MachineLocations);

    public record RegisterMachineOutput(ClusterStateMachine State, MachineMapping[] MachineMappings);

    public Task<Result<RegisterMachineOutput>> RegisterMachinesAsync(OperationContext context, RegisterMachineInput request);

    public record HeartbeatInput(IReadOnlyList<MachineId> MachineIds, MachineState MachineState);

    public record HeartbeatOutput(ClusterStateMachine State, MachineRecord[] PriorRecords);

    public Task<Result<HeartbeatOutput>> HeartbeatAsync(OperationContext context, HeartbeatInput request);

    public Task<Result<ClusterStateMachine>> ReadState(OperationContext context);
}
