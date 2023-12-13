// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Utils;
using ProtoBuf;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache;

public static class ClusterStateMachineExtensions
{
    public static (ClusterStateMachine NextState, MachineRecord[] Result) HeartbeatMany(
        this ClusterStateMachine state,
        IClusterStateStorage.HeartbeatInput request,
        DateTime nowUtc)
    {
        var priorMachineRecords = new MachineRecord[request.MachineIds.Count];
        foreach (var entry in request.MachineIds.AsIndexed())
        {
            (state, priorMachineRecords[entry.Index]) =
                state.Heartbeat(entry.Item, nowUtc, request.MachineState).ThrowIfFailure();
        }

        return (state, priorMachineRecords);
    }

    public static (ClusterStateMachine NextState, MachineId[] Result) RegisterMany(
        this ClusterStateMachine state,
        ClusterStateRecomputeConfiguration configuration,
        IClusterStateStorage.RegisterMachineInput request,
        DateTime nowUtc)
    {
        IReadOnlyList<MachineId> takeover = Array.Empty<MachineId>();

        // If we allow takeover, we need to decide which IDs to take over. We pick dead machines that have been dead
        // the longest. We avoid doing takeover for persistent machines because persistent locations will be around
        // for a while, so there's no telling what might appear from nowhere.
        if (!request.Persistent && state.AllowTakeover(nowUtc, configuration))
        {
            var transitioned = state.TransitionInactiveMachines(configuration, nowUtc);
            takeover = transitioned.Records
                .Where(record => record.State == MachineState.DeadUnavailable && !record.Persistent)
                .OrderBy(record => record.LastHeartbeatTimeUtc)
                .Select(record => record.Id)
                .Take(request.MachineLocations.Count)
                .ToList();
        }

        // When assigning IDs, we must prefer to always re-use. We will take-over if we can't reuse, and obtain a new
        // one if both of those fail. Re-use is important because it prevents content churn. Moreover, we assume that
        // there's a single ID per location, so breaking that assumption will cause issues.
        var taken = 0;
        var assignments = new MachineId[request.MachineLocations.Count];
        foreach (var (location, index) in request.MachineLocations.AsIndexed())
        {
            if (state.TryResolveMachineId(location, out var machineId))
            {
                assignments[index] = machineId;
            }
            else
            {
                assignments[index] = taken < takeover.Count ? takeover[taken++] : new MachineId(state.NextMachineId);
                state = state.ForceTakeoverMachine(assignments[index], location, nowUtc, persistent: request.Persistent);
            }
        }

        return (state, assignments);
    }
}

/// <summary>
/// Immutable data structure that implements state machines for all machines inside of a cluster. This is the model
/// behind <see cref="ClusterState"/>, and it is followed by all machines in the cluster.
/// </summary>
/// <remarks>
/// This class must be serializable by System.Text.Json due to <see cref="BlobClusterStateStorage"/>.
///
/// It must also be serializable by Protobuf.Net due to <see cref="IGrpcClusterStateStorage"/>. The ProtoContract
/// below ensures this is the case.
/// </remarks>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public record ClusterStateMachine
{
    internal const MachineState InitialState = MachineState.Open;

    // Machine IDs have historically been assigned from 1 onwards as an implementation detail. Thus, 0 has been
    // deemed to be an invalid machine ID, and is used as such in some parts of the code. This code keeps the
    // convention to avoid making major changes.
    public int NextMachineId { get; init; } = MachineId.MinValue;

    public IReadOnlyList<MachineRecord> Records { get; init; } = new List<MachineRecord>();

    /// <summary>
    /// Registers a machine with a specific machine ID.
    /// </summary>
    /// <remarks>
    /// This is used for either transitioning from one storage to another, or for taking over a machine ID from another
    /// machine. In both cases, the machine whose ID is being taken over MUST be entirely offline, or things can go
    /// very wrong.
    /// </remarks>
    internal ClusterStateMachine ForceTakeoverMachine(MachineId machineId, MachineLocation location, DateTime nowUtc, bool persistent)
    {
        return ForceRegisterMachineWithState(machineId, location, nowUtc, state: InitialState, takeover: true, persistent);
    }

    /// <summary>
    /// Registers a machine.
    /// </summary>
    internal (ClusterStateMachine Next, MachineId Id) RegisterMachineForTests(MachineLocation location, DateTime nowUtc, bool persistent)
    {
        if (TryResolveMachineId(location, out var machineId))
        {
            return (this, machineId);
        }

        machineId = new MachineId(NextMachineId);
        return (ForceRegisterMachineWithState(machineId, location, nowUtc, state: InitialState, takeover: false, persistent), machineId);
    }

    /// <summary>
    /// Registers a machine with a specific state.
    /// </summary>
    /// <remarks>
    /// Used only for testing.
    /// </remarks>
    internal (ClusterStateMachine Next, MachineId Id) ForceRegisterMachineWithStateForTests(
        MachineLocation location,
        DateTime nowUtc,
        MachineState state,
        bool persistent)
    {
        var machineId = new MachineId(NextMachineId);
        return (ForceRegisterMachineWithState(machineId, location, nowUtc, state, takeover: false, persistent), machineId);
    }

    private ClusterStateMachine ForceRegisterMachineWithState(
        MachineId machineId,
        MachineLocation location,
        DateTime nowUtc,
        MachineState state,
        bool takeover,
        bool persistent)
    {
        Contract.Requires(
            state != MachineState.Unknown,
            $"Can't register machine ID `{machineId}` for location `{location}` with initial state `{state}`");

        if (persistent)
        {
            state = MachineState.Open;
        }

        var addition = new MachineRecord
        {
            Id = machineId,
            Location = location,
            State = state,
            LastHeartbeatTimeUtc = nowUtc,
            Persistent = persistent,
        };

        if (machineId.Index < NextMachineId)
        {
            bool inserted = false;
            var records = new List<MachineRecord>();
            foreach (var record in Records)
            {
                if (record.Id == machineId)
                {
                    // The record already exists. Update it.
                    Contract.Assert(
                        record.Location.Equals(location) || (takeover && !record.Persistent),
                        $"Machine id `{machineId}` has already been allocated to location `{record}` and so can't be allocated to `{location}`");
                    records.Add(addition);
                    inserted = true;
                    continue;
                }

                if (record.Id.Index > machineId.Index && !inserted)
                {
                    // The record doesn't exist, and we've passed the point where it would have been. Insert it now and
                    // make sure we don't go through this logic again
                    records.Add(addition);
                    inserted = true;
                }

                records.Add(record);
            }

            Contract.Assert(
                inserted,
                $"Machine id `{machineId}` with location {location} should have been inserted but wasn't");

            return this with { Records = records };
        }
        else
        {
            var records = Records.ToList();
            records.Add(addition);
            return this with { NextMachineId = Math.Max(machineId.Index + 1, NextMachineId + 1), Records = records, };
        }
    }

    public Result<(ClusterStateMachine Next, MachineRecord Previous)> Heartbeat(MachineId machineId, DateTime nowUtc, MachineState state)
    {
        if (!machineId.Valid)
        {
            // This will only happen when operating in DistributedContentConsumerOnly mode. These machines get
            // registered with an invalid machine ID, so this is the expected response of heartbeat.
            return Result.Success((Next: this, Previous: new MachineRecord() { State = state, LastHeartbeatTimeUtc = nowUtc, }));
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
            return Result.FromErrorMessage<(ClusterStateMachine Next, MachineRecord Previous)>(
                $"Failed to find machine id `{machineId}` in records");
        }

        return Result.Success((Next: this with { Records = records }, Previous: previous));
    }

    public ClusterStateMachine TransitionInactiveMachines(ClusterStateRecomputeConfiguration configuration, DateTime nowUtc)
    {
        var records = new List<MachineRecord>(capacity: Records.Count);
        foreach (var record in Records)
        {
            var current = record;

            // A record may be mutated more than once if enough time has passed since the last heartbeat. For example,
            // if A heartbeat as Open 1h ago, and the configuration is to transition from Open to Closed after 10m and
            // Closed to Dead after 5m, then the record will be mutated twice. First from Open to Closed, and then from
            // Closed to Dead.
            while (TryChangeMachineStateIfNeeded(configuration, nowUtc, current, out var next))
            {
                current = next;
            }

            records.Add(current);
        }

        return this with { Records = records, };
    }

    private bool TryChangeMachineStateIfNeeded(
        ClusterStateRecomputeConfiguration configuration,
        DateTime nowUtc,
        MachineRecord current,
        out MachineRecord next)
    {
        // The following changes in state push forward the last heartbeat time because we want to ensure that changes
        // only happen if enough time has passed.
        switch (current.State)
        {
            case MachineState.Unknown:
            case MachineState.DeadUnavailable:
            {
                break;
            }
            case MachineState.DeadExpired:
            {
                if (current.LastHeartbeatTimeUtc.IsStale(nowUtc, configuration.ExpiredToUnavailable))
                {
                    next = current with { State = MachineState.DeadUnavailable, LastHeartbeatTimeUtc = current.LastHeartbeatTimeUtc + configuration.ExpiredToUnavailable };
                    return true;
                }

                break;
            }
            case MachineState.Open:
            {
                if (current.LastHeartbeatTimeUtc.IsStale(nowUtc, configuration.ActiveToClosed))
                {
                    next = current with { State = MachineState.Closed, LastHeartbeatTimeUtc = current.LastHeartbeatTimeUtc + configuration.ActiveToClosed };
                    return true;
                }

                break;
            }
            case MachineState.Closed:
            {
                if (current.LastHeartbeatTimeUtc.IsStale(nowUtc, configuration.ClosedToExpired))
                {
                    next = current with { State = MachineState.DeadExpired, LastHeartbeatTimeUtc = current.LastHeartbeatTimeUtc + configuration.ClosedToExpired };
                    return true;
                }

                break;
            }
            default:
                throw new NotImplementedException(
                    $"Attempt to transition machine record `{current}` failed because the state `{current.State}` is unknown");
        }

        next = current;
        return false;
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

    public Result<MachineRecord> GetRecord(MachineId machineId)
    {
        // TODO: binary search
        foreach (var record in Records)
        {
            if (record.Id == machineId)
            {
                Contract.Assert(!record.Persistent || record.State == MachineState.Open);
                return Result.Success(record);
            }
        }

        return Result.FromErrorMessage<MachineRecord>($"Failed to find machine id `{machineId}`");
    }

    /// <summary>
    /// Decide whether to allow a machine to take over machine IDs from other machines.
    /// </summary>
    /// <remarks>
    /// This basically boils down to deciding whether the clock from the current machine is skewed or not. If the clock
    /// is skewed by enough time (say a couple of hours), then the machine will think all others are inactive and could
    /// cause an incident by taking over machine IDs from machines that are actually running and available.
    /// </remarks>
    public bool AllowTakeover(DateTime nowUtc, ClusterStateRecomputeConfiguration configuration)
    {
        var latestHeartbeat = Records
            .Where(record => record.IsOpen() || record.IsClosed())
            .Select(record => record.LastHeartbeatTimeUtc)
            .MaxByOrDefault(r => r);
        if (latestHeartbeat != default && nowUtc > latestHeartbeat + configuration.ActiveToExpired)
        {
            return false;
        }

        return true;
    }
}
