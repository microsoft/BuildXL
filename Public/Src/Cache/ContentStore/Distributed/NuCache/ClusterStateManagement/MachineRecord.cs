// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Single immutable record of a given machine's state. For the entire cluster, <see cref="ClusterStateMachine"/>
    /// </summary>
    /// <remarks>
    /// This class must be serializable due to <see cref="BlobClusterStateStorage"/>
    /// </remarks>
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
