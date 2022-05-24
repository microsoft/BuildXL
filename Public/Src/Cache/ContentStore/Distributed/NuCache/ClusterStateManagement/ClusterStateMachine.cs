// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Immutable data structure that implements state machines for all machines inside of a cluster. This is the model
    /// behind <see cref="ClusterState"/>, and it is followed by all machines in the cluster.
    /// </summary>
    /// <remarks>
    /// This class must be serializable by System.Text.Json due to <see cref="BlobClusterStateStorage"/>
    /// </remarks>
    public record ClusterStateMachine
    {
        internal const MachineState InitialState = MachineState.Open;

        public DateTime LastStateMachineRecomputeTimeUtc { get; init; } = DateTime.MinValue;

        // Machine IDs have historically been assigned from 1 onwards as an implementation detail. Thus, 0 has been
        // deemed to be an invalid machine ID, and is used as such in some parts of the code. This code keeps the
        // convention to avoid making major changes.
        public int NextMachineId { get; init; } = MachineId.MinValue;

        public IReadOnlyList<MachineRecord> Records { get; init; } = Array.Empty<MachineRecord>();

        /// <summary>
        /// Registers a machine.
        /// </summary>
        public (ClusterStateMachine Next, MachineId Id) RegisterMachine(MachineLocation location, DateTime nowUtc)
        {
            var machineId = new MachineId(NextMachineId);
            return (ForceRegisterMachineWithState(machineId, location, nowUtc, state: InitialState), machineId);
        }

        /// <summary>
        /// Registers a machine with a specific machine ID. Used for transitioning between different storages.
        /// </summary>
        public ClusterStateMachine ForceRegisterMachine(MachineId machineId, MachineLocation location, DateTime nowUtc)
        {
            return ForceRegisterMachineWithState(machineId, location, nowUtc, state: InitialState);
        }

        /// <summary>
        /// Registers a machine with a specific state.
        /// </summary>
        /// <remarks>
        /// Used only for testing.
        /// </remarks>
        internal (ClusterStateMachine Next, MachineId Id) ForceRegisterMachineWithState(
            MachineLocation location,
            DateTime nowUtc,
            MachineState state)
        {
            var machineId = new MachineId(NextMachineId);
            return (ForceRegisterMachineWithState(machineId, location, nowUtc, state), machineId);
        }

        private ClusterStateMachine ForceRegisterMachineWithState(
            MachineId machineId,
            MachineLocation location,
            DateTime nowUtc,
            MachineState state)
        {
            Contract.Requires(state != MachineState.Unknown, $"Can't register machine ID `{machineId}` for location `{location}` with initial state `{state}`");

            if (machineId.Index < NextMachineId)
            {
                if (TryResolve(machineId, out var assignedLocation))
                {
                    Contract.Assert(assignedLocation.Equals(location), $"Machine id `{machineId}` has already been allocated to location `{assignedLocation}` and so can't be allocated to `{location}`");

                    // Heartbeat can only fail if the machine ID doesn't exist, and we know it does
                    return Heartbeat(machineId, nowUtc, state).ThrowIfFailure().Next;
                }
                else
                {
                    var records = Records.ToList();

                    records.Add(new MachineRecord()
                    {
                        Id = machineId,
                        Location = location,
                        State = state,
                        LastHeartbeatTimeUtc = nowUtc,
                    });

                    // We sort this list in order to ensure it is easy on the eyes when we need to manually inspect it
                    records.Sort((a, b) => a.Id.Index.CompareTo(b.Id.Index));

                    return this with { Records = records };
                }
            }
            else
            {
                var records = Records.ToList();

                records.Add(new MachineRecord()
                {
                    Id = machineId,
                    Location = location,
                    State = state,
                    LastHeartbeatTimeUtc = nowUtc,
                });

                return this with
                {
                    NextMachineId = Math.Max(machineId.Index + 1, NextMachineId + 1),
                    Records = records,
                };
            }
        }

        public Result<(ClusterStateMachine Next, MachineRecord Previous)> Heartbeat(MachineId machineId, DateTime nowUtc, MachineState state)
        {
            if (!machineId.IsValid())
            {
                // This will only happen when operating in DistributedContentConsumerOnly mode. These machines get
                // registered with an invalid machine ID, so this is the expected response of heartbeat.
                return Result.Success((Next: this, Previous: new MachineRecord()
                {
                    State = state,
                    LastHeartbeatTimeUtc = nowUtc,
                }));
            }

            var records = new List<MachineRecord>(capacity: Records.Count);
            MachineRecord? previous = null;
            foreach (var record in Records)
            {
                if (record.Id == machineId)
                {
                    records.Add(record.Heartbeat(nowUtc, state));
                    previous = record;
                }
                else
                {
                    records.Add(record);
                }
            }

            if (previous is null)
            {
                return Result.FromErrorMessage<(ClusterStateMachine Next, MachineRecord Previous)>($"Failed to find machine id `{machineId}` in records");
            }

            return Result.Success((Next: this with { Records = records }, Previous: previous));
        }

        public ClusterStateMachine Recompute(ClusterStateRecomputeConfiguration configuration, DateTime nowUtc)
        {
            if (configuration.RecomputeFrequency != TimeSpan.Zero && LastStateMachineRecomputeTimeUtc.IsRecent(nowUtc, configuration.RecomputeFrequency))
            {
                return this;
            }

            var records = new List<MachineRecord>(capacity: Records.Count);
            foreach (var record in Records)
            {
                records.Add(ChangeMachineStateIfNeeded(configuration, nowUtc, record));
            }

            return this with
            {
                LastStateMachineRecomputeTimeUtc = nowUtc,
                Records = records,
            };
        }

        private MachineRecord ChangeMachineStateIfNeeded(
            ClusterStateRecomputeConfiguration configuration,
            DateTime nowUtc,
            MachineRecord current)
        {
            switch (current.State)
            {
                case MachineState.Unknown:
                case MachineState.DeadUnavailable:
                case MachineState.DeadExpired:
                {
                    break;
                }
                case MachineState.Open:
                {
                    if (current.LastHeartbeatTimeUtc.IsStale(nowUtc, configuration.ActiveToDeadExpiredInterval))
                    {
                        return current with { State = MachineState.DeadExpired };
                    }

                    if (current.LastHeartbeatTimeUtc.IsStale(nowUtc, configuration.ActiveToClosedInterval))
                    {
                        return current with { State = MachineState.Closed, };
                    }

                    break;
                }
                case MachineState.Closed:
                {
                    if (current.LastHeartbeatTimeUtc.IsStale(nowUtc, configuration.ClosedToDeadExpiredInterval))
                    {
                        return current with { State = MachineState.DeadExpired };
                    }

                    break;
                }
                default:
                    throw new NotImplementedException($"Attempt to transition machine record `{current}` failed because the state `{current.State}` is unknown");
            }

            return current;
        }

        public bool TryResolve(MachineId machineId, out MachineLocation machineLocation)
        {
            var status = GetStatus(machineId);

            if (status.Succeeded)
            {
                machineLocation = status.Value.Location;
            }
            else
            {
                machineLocation = default;
            }

            return status.Succeeded;
        }

        public bool TryResolveMachineId(MachineLocation machineLocation, out MachineId machineId)
        {
            foreach (var record in Records)
            {
                if (record.Location.Equals(machineLocation))
                {
                    machineId = record.Id;
                    return true;
                }
            }

            machineId = default;
            return false;
        }

        public Result<MachineRecord> GetStatus(MachineId machineId)
        {
            // TODO: binary search
            foreach (var record in Records)
            {
                if (record.Id == machineId)
                {
                    return Result.Success(record);
                }
            }

            return Result.FromErrorMessage<MachineRecord>($"Failed to find machine id `{machineId}`");
        }
    }
}
