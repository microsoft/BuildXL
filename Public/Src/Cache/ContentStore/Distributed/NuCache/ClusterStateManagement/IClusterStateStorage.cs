// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ProtoBuf;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;

public interface IClusterStateStorage : IStartupShutdownSlim
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public record RegisterMachineInput(IReadOnlyList<MachineLocation> MachineLocations, bool Persistent = false)
    {
        /// <summary>
        /// This parameterless constructor exists only to allow ProtoBuf.NET initialization
        /// </summary>
        public RegisterMachineInput() : this(new List<MachineLocation>())
        {
        }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public record RegisterMachineOutput(ClusterStateMachine State, MachineMapping[] MachineMappings)
    {
        /// <summary>
        /// This parameterless constructor exists only to allow ProtoBuf.NET initialization
        /// </summary>
        public RegisterMachineOutput() : this(default, new MachineMapping[] { })
        {
        }
    }

    public Task<Result<RegisterMachineOutput>> RegisterMachinesAsync(OperationContext context, RegisterMachineInput request);

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public record HeartbeatInput(IReadOnlyList<MachineId> MachineIds, MachineState MachineState)
    {
        /// <summary>
        /// This parameterless constructor exists only to allow ProtoBuf.NET initialization
        /// </summary>
        public HeartbeatInput() : this(new List<MachineId>(), default)
        {
        }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public record HeartbeatOutput(ClusterStateMachine State, MachineRecord[] PriorRecords)
    {
        /// <summary>
        /// This parameterless constructor exists only to allow ProtoBuf.NET initialization
        /// </summary>
        public HeartbeatOutput() : this(default, new MachineRecord[] { })
        {
        }
    }

    public Task<Result<HeartbeatOutput>> HeartbeatAsync(OperationContext context, HeartbeatInput request);

    public Task<Result<ClusterStateMachine>> ReadStateAsync(OperationContext context);
}
