// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using BuildXL.Cache.ContentStore.Utils;
using ProtoBuf;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Single immutable record of a given machine's state. For the entire cluster, <see cref="ClusterStateMachine"/>
    /// </summary>
    /// <remarks>
    /// This class must be serializable due to <see cref="BlobClusterStateStorage"/>
    ///
    /// It must also be serializable by Protobuf.Net due to <see cref="IGrpcClusterStateStorage"/>. The ProtoContract
    /// below ensures this is the case.
    /// </remarks>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public record MachineRecord
    {
        public MachineId Id { get; init; } = MachineId.Invalid;

        public MachineLocation Location { get; init; } = new MachineLocation(string.Empty);

        public MachineState State { get; init; } = MachineState.Unknown;

        public DateTime LastHeartbeatTimeUtc { get; init; } = DateTime.MinValue;

        internal MachineRecord Heartbeat(DateTime nowUtc, MachineState nextState)
        {
            if (nextState == MachineState.Unknown)
            {
                return this with { LastHeartbeatTimeUtc = nowUtc, };
            }

            return this with { State = nextState, LastHeartbeatTimeUtc = nowUtc };
        }

        internal MachineRecord Heartbeat(DateTime nowUtc, MachineState nextState, MachineLocation machineLocation)
        {
            if (nextState == MachineState.Unknown)
            {
                return this with { LastHeartbeatTimeUtc = nowUtc, Location = machineLocation};
            }

            return this with { State = nextState, LastHeartbeatTimeUtc = nowUtc, Location = machineLocation };
        }

        public bool IsOpen()
        {
            return State == MachineState.Open;
        }

        public bool IsClosed()
        {
            return State == MachineState.Closed;
        }

        public bool IsInactive()
        {
            return State == MachineState.DeadExpired || State == MachineState.DeadUnavailable;
        }
    }
}
