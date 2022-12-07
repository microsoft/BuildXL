// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public record BlobClusterStateStorageConfiguration()
        : Host.Configuration.BlobFolderStorageConfiguration(ContainerName: "checkpoints", FolderName: "clusterState")
    {
        public string FileName { get; set; } = "clusterState.json";

        public ClusterStateRecomputeConfiguration RecomputeConfiguration { get; set; } = new ClusterStateRecomputeConfiguration();
    }

    public class BlobClusterStateStorage : StartupShutdownComponentBase
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(BlobClusterStateStorage));

        private readonly BlobClusterStateStorageConfiguration _configuration;
        private readonly IClock _clock;

        private readonly BlobFolderStorage _storage;

        public BlobClusterStateStorage(
            BlobClusterStateStorageConfiguration configuration,
            IClock? clock = null)
        {
            Contract.RequiresNotNull(configuration.Credentials);
            _configuration = configuration;
            _clock = clock ?? SystemClock.Instance;
            _storage = new BlobFolderStorage(Tracer, configuration);

            LinkLifetime(_storage);
        }

        public record RegisterMachineInput(IReadOnlyList<MachineLocation> MachineLocations);

        public record RegisterMachineOutput(ClusterStateMachine State, MachineMapping[] MachineMappings);

        public Task<Result<RegisterMachineOutput>> RegisterMachinesAsync(OperationContext context, RegisterMachineInput request)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var (currentState, assignedMachineIds) = await _storage.ReadModifyWriteAsync<ClusterStateMachine, MachineId[]>(context, _configuration.FileName, currentState =>
                {
                    var now = _clock.UtcNow;

                    MachineId[] assignedMachineIds = new MachineId[request.MachineLocations.Count];
                    foreach (var (item, index) in request.MachineLocations.AsIndexed())
                    {
                        if (currentState.TryResolveMachineId(item, out var machineId))
                        {
                            assignedMachineIds[index] = machineId;
                        }
                        else
                        {
                            (currentState, assignedMachineIds[index]) = currentState.RegisterMachine(item, now);
                        }
                    }

                    currentState = currentState.Recompute(_configuration.RecomputeConfiguration, now);
                    return (currentState, assignedMachineIds);
                }).ThrowIfFailureAsync();

                var machineMappings = request.MachineLocations
                    .Zip(assignedMachineIds, (machineLocation, machineId) => new MachineMapping(machineId, machineLocation))
                    .ToArray();

                return Result.Success(new RegisterMachineOutput(currentState, machineMappings));
            },
            traceOperationStarted: false);
        }

        public record HeartbeatInput(IReadOnlyList<MachineId> MachineIds, MachineState MachineState);

        public record HeartbeatOutput(ClusterStateMachine State, MachineRecord[] PriorRecords);

        public Task<Result<HeartbeatOutput>> HeartbeatAsync(OperationContext context, HeartbeatInput request)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var (currentState, priorMachineRecords) = await _storage.ReadModifyWriteAsync<ClusterStateMachine, MachineRecord[]>(context, _configuration.FileName, currentState =>
                {
                    var now = _clock.UtcNow;

                    var priorMachineRecords = new MachineRecord[request.MachineIds.Count];
                    foreach (var entry in request.MachineIds.AsIndexed())
                    {
                        (currentState, priorMachineRecords[entry.Index]) = currentState.Heartbeat(entry.Item, now, request.MachineState).ThrowIfFailure();
                    }

                    currentState = currentState.Recompute(_configuration.RecomputeConfiguration, now);

                    return (currentState, priorMachineRecords);
                }).ThrowIfFailureAsync();

                return Result.Success(new HeartbeatOutput(currentState, priorMachineRecords));
            },
            traceOperationStarted: false);
        }

        public Task<Result<ClusterStateMachine>> ReadState(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer, () =>
            {
                return _storage.ReadAsync<ClusterStateMachine>(context, _configuration.FileName);
            },
            traceOperationStarted: false);
        }
    }
}
