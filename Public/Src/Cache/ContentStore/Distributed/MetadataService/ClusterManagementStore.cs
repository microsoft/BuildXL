// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Manages cluster state and global content tracking. More specifically, the component keeps track of which machines are active based on their heartbeat status.
    /// </summary>
    public partial class ClusterManagementStore : StartupShutdownComponentBase, IClusterManagementStore
    {
        private const string ClusterStateKey = "ResilientContentMetadataService.ClusterState";
        private const string ClusterStateStorageName = "tracking/clusterState.v1.json";

        protected override Tracer Tracer { get; } = new Tracer(nameof(ClusterManagementStore));

        private readonly SemaphoreSlim _machineInfoLock = TaskUtilities.CreateMutex();

        private ImmutableDictionary<MachineLocation, ClusterMachineEntry> _machineInfoMap = ImmutableDictionary<MachineLocation, ClusterMachineEntry>.Empty;

        private int _sequenceNumber = ushort.MaxValue;

        private readonly ClusterManagementConfiguration _configuration;
        private readonly IStreamStorage _storage;
        private readonly IClock _clock;

        private BitMachineIdSet _closedMachines = BitMachineIdSet.EmptyInstance;
        private BitMachineIdSet _inactiveMachines = BitMachineIdSet.EmptyInstance;

        public ClusterManagementStore(
            ClusterManagementConfiguration configuration,
            IStreamStorage streamStorage,
            IClock clock = null)
        {
            _configuration = configuration;
            _storage = streamStorage;
            _clock = clock ?? SystemClock.Instance;

            LinkLifetime(streamStorage);
        }

        public async Task<Result<HeartbeatMachineResponse>> HeartbeatAsync(OperationContext context, HeartbeatMachineRequest request)
        {
            using var releaser = await _machineInfoLock.AcquireAsync();
            bool added = false;
            bool persist = false;
            MachineState priorState = MachineState.Unknown;

            if (request.MachineId.Index != ClusterState.InvalidMachineId)
            {
                // Only persist heartbeats for cluster participants
                persist = true;
                if (!_machineInfoMap.TryGetValue(request.Location, out var entry))
                {
                    added = true;
                    entry = new ClusterMachineEntry()
                    {
                        Info = new ClusterMachineInfo()
                        {
                            Location = request.Location,
                            MachineId = request.MachineId,
                            Name = request.Name,
                        },
                        SequenceNumber = Interlocked.Increment(ref _sequenceNumber)
                    };

                    _machineInfoMap = _machineInfoMap.SetItem(request.Location, entry);
                }

                priorState = entry.State;
                UpdateMachineState(context, entry, request.DeclaredMachineState);
                request.HeartbeatTime ??= _clock.UtcNow;
                entry.LastHeartbeatTimeUtc = request.HeartbeatTime.Value;
            }

            return new HeartbeatMachineResponse()
            {
                PriorState = priorState,
                ClosedMachines = _closedMachines,
                InactiveMachines = _inactiveMachines,
                Added = added,
                PersistRequest = persist
            };
        }

        public Task<Result<GetClusterUpdatesResponse>> GetClusterUpdatesAsync(OperationContext context, GetClusterUpdatesRequest request)
        {
            var sequenceNumber = _sequenceNumber;

            Dictionary<MachineId, MachineLocation> unknownMachines = null;

            // This store keeps a sequence number for determining whether unknown machines are present rather than using
            // the max machine id since primary registration currently happens in Redis so this store does not get machine ids
            // in order.
            if (request.MaxMachineId != sequenceNumber)
            {
                unknownMachines = _machineInfoMap.Values.Where(e => e.SequenceNumber > request.MaxMachineId).ToDictionary(e => e.Info.MachineId, e => e.Info.Location);
            }

            var response = new GetClusterUpdatesResponse()
            {
                MaxMachineId = sequenceNumber,
                UnknownMachines = unknownMachines,
            };

            return Task.FromResult(Result.Success(response));
        }

        private bool IsExpired(ClusterMachineEntry entry)
        {
            return entry.LastHeartbeatTimeUtc.IsRecent(_clock.UtcNow, _configuration.MachineExpiryInterval);
        }

        public void UpdateMachineState(OperationContext context, ClusterMachineEntry entry, MachineState newState, bool restoring = false)
        {
            if (newState == MachineState.Unknown || (newState == entry.State && !restoring))
            {
                return;
            }

            var machineId = entry.Info.MachineId;
            var oldState = entry.State;

            context.PerformOperation(
                Tracer,
                () =>
                {
                    entry.State = newState;

                    switch (newState)
                    {
                        case MachineState.Open:
                            _inactiveMachines = _inactiveMachines.SetExistenceBit(machineId, false);
                            _closedMachines = _closedMachines.SetExistenceBit(machineId, false);
                            break;
                        case MachineState.DeadExpired:
                        case MachineState.DeadUnavailable:
                            _inactiveMachines = _inactiveMachines.SetExistenceBit(machineId, true);
                            _closedMachines = _closedMachines.SetExistenceBit(machineId, false);
                            break;
                        case MachineState.Closed:
                            _inactiveMachines = _inactiveMachines.SetExistenceBit(machineId, false);
                            _closedMachines = _closedMachines.SetExistenceBit(machineId, true);
                            break;
                    }

                    return BoolResult.Success;
                },
                traceOperationStarted: false,
                traceOperationFinished: !restoring,
                messageFactory: r => $"{oldState} -> {newState}").ThrowIfFailure();
        }

        public Task<BoolResult> RestoreClusterCheckpointAsync(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var clusterStateResult = await _storage.ReadAsync(context, ClusterStateStorageName, async stream =>
                {
                    using var streamReader = new StreamReader(stream);
                    var clusterStateJson = await streamReader.ReadToEndAsync();
                    return Result.Success(JsonUtilities.JsonDeserialize<ClusterTrackingState>(clusterStateJson));
                });

                if (clusterStateResult.TryGetValue(out var clusterState))
                {
                    using (await _machineInfoLock.AcquireAsync())
                    {
                        int sequenceNumber = clusterState.SequenceNumber;
                        var machineInfoBuilder = ImmutableDictionary<MachineLocation, ClusterMachineEntry>.Empty.ToBuilder();

                        _inactiveMachines = BitMachineIdSet.EmptyInstance;
                        _closedMachines = BitMachineIdSet.EmptyInstance;

                        foreach (var machine in clusterState.Machines)
                        {
                            machineInfoBuilder[machine.Info.Location] = machine;
                            UpdateMachineState(context, machine, IsExpired(machine) ? MachineState.DeadExpired : machine.State, restoring: true);
                            sequenceNumber = Math.Max(_sequenceNumber, machine.SequenceNumber);
                        }

                        _machineInfoMap = machineInfoBuilder.ToImmutable();
                        _sequenceNumber = sequenceNumber;
                    }
                }

                return BoolResult.Success;
            },
            extraEndMessage: r => $"MachineCount={_machineInfoMap.Count} MaxMachineId={_sequenceNumber} InactiveMachines={_inactiveMachines.Count} ClosedMachines={_closedMachines.Count}");
        }

        public Task<BoolResult> CreateClusterCheckpointAsync(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                await ComputeClusterStateAsync(context);

                return BoolResult.Success;
            });
        }

        public Task<ClusterTrackingState> ComputeClusterStateAsync(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                ClusterTrackingState clusterState;
                using (await _machineInfoLock.AcquireAsync())
                {
                    foreach (var machine in _machineInfoMap.Values)
                    {
                        if (IsExpired(machine))
                        {
                            UpdateMachineState(context, machine, MachineState.DeadExpired);
                        }
                    }

                    clusterState = new ClusterTrackingState()
                    {
                        SequenceNumber = _sequenceNumber,
                        Machines = _machineInfoMap.Values.ToArray()
                    };
                }

                var clusterStateJson = JsonUtilities.JsonSerialize(clusterState, indent: true);
                await _storage.StoreAsync(context, ClusterStateStorageName, clusterStateJson.AsStream()).ThrowIfFailureAsync();

                return Result.Success(clusterState);
            }).ThrowIfFailureAsync();
        }
    }

    public class ClusterTrackingState
    {
        public int SequenceNumber { get; set; }

        public ClusterMachineEntry[] Machines { get; set; }
    }

    public record ClusterMachineEntry
    {
        public DateTime LastHeartbeatTimeUtc { get; set; }

        public ClusterMachineInfo Info { get; set; }

        public MachineState State { get; set; }

        public int SequenceNumber { get; set; }

        public override string ToString()
        {
            return $"State=[{State}] SequenceNumber=[{SequenceNumber}] LastHeartbeatTime=[{LastHeartbeatTimeUtc}] {Info}";
        }
    }
}
