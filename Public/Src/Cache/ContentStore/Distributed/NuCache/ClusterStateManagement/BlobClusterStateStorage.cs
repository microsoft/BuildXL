// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public class BlobClusterStateStorageConfiguration : IBlobFolderStorageConfiguration
    {
        public AzureBlobStorageCredentials? Credentials { get; set; }

        public string ContainerName { get; set; } = "checkpoints";

        public string FolderName { get; set; } = "clusterState";

        public string FileName { get; set; } = "clusterState.json";

        public TimeSpan StorageInteractionTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public bool Standalone { get; set; } = false;

        public ClusterStateRecomputeConfiguration RecomputeConfiguration { get; set; } = new ClusterStateRecomputeConfiguration();
    }

    public class BlobClusterStateStorage : StartupShutdownComponentBase, IClusterStateStorage, ISecondaryClusterStateStorage
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

        public Task<Result<MachineMapping>> RegisterMachineAsync(OperationContext context, MachineLocation machineLocation)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var (_, assignedMachineId) = await _storage.ReadModifyWriteAsync<ClusterStateMachine, MachineId>(context, _configuration.FileName, current =>
                {
                    var now = _clock.UtcNow;
                    var next = current;
                    if (!current.TryResolveMachineId(machineLocation, out var machineId))
                    {
                        (next, machineId) = current.RegisterMachine(machineLocation, now);
                    }

                    next = next.Recompute(_configuration.RecomputeConfiguration, now);
                    return (next, machineId);
                }).ThrowIfFailureAsync();

                return Result.Success(new MachineMapping(machineLocation, assignedMachineId));
            },
            traceOperationStarted: false,
            extraEndMessage: r => $"Mapping=[{r.ToStringOr($"(Id: Failure, Location: {machineLocation})")}]");
        }

        public Task<BoolResult> ForceRegisterMachineAsync(OperationContext context, MachineMapping mapping)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                await _storage.ReadModifyWriteAsync<ClusterStateMachine>(context, _configuration.FileName, current =>
                {
                    var now = _clock.UtcNow;
                    var next = current.ForceRegisterMachine(mapping.Id, mapping.Location, now);
                    next = next.Recompute(_configuration.RecomputeConfiguration, now);

                    return next;
                }).ThrowIfFailureAsync();

                return BoolResult.Success;
            },
            traceOperationStarted: false,
            extraEndMessage: _ =>
            {
                return $"Mapping=[{mapping}]";
            });
        }

        public Task<Result<HeartbeatMachineResponse>> HeartbeatAsync(OperationContext context, HeartbeatMachineRequest request)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var now = _clock.UtcNow;
                request.HeartbeatTime ??= now;

                var (currentState, priorStatus) = await _storage.ReadModifyWriteAsync<ClusterStateMachine, MachineRecord>(context, _configuration.FileName, current =>
                {
                    var (next, priorStatus) = current.Heartbeat(request.MachineId, request.HeartbeatTime!.Value, request.DeclaredMachineState).ThrowIfFailure();
                    next = next.Recompute(_configuration.RecomputeConfiguration, now);

                    return (next, priorStatus);
                }).ThrowIfFailureAsync();

                return Result.Success(new HeartbeatMachineResponse()
                {
                    Added = false,
                    PriorState = priorStatus.State,
                    InactiveMachines = currentState.InactiveMachinesToBitMachineIdSet(),
                    ClosedMachines = currentState.ClosedMachinesToBitMachineIdSet(),
                });
            },
            traceOperationStarted: false,
            extraEndMessage: r => $"Request=[{request}] Response=[{r.ToStringOr("Error")}]");
        }

        public Task<Result<GetClusterUpdatesResponse>> GetClusterUpdatesAsync(OperationContext context, GetClusterUpdatesRequest request)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var clusterState = await _storage.ReadAsync<ClusterStateMachine>(context, _configuration.FileName).ThrowIfFailureAsync();
                if (clusterState is null || request.MaxMachineId >= clusterState.NextMachineId)
                {
                    return Result.Success(new GetClusterUpdatesResponse()
                    {
                        UnknownMachines = new Dictionary<MachineId, MachineLocation>(),
                        MaxMachineId = Math.Max(request.MaxMachineId, (clusterState?.NextMachineId ?? 1) - 1),
                    });
                }

                return Result.Success(new GetClusterUpdatesResponse()
                {
                    UnknownMachines = clusterState.Records.Where(r => r.Id.Index > request.MaxMachineId)
                                                          .ToDictionary(r => r.Id, r => r.Location),
                    MaxMachineId = clusterState.NextMachineId - 1,
                });
            },
            traceOperationStarted: false,
            extraEndMessage: r => $"Request=[{request}] Response=[{r.ToStringOr("Error")}]");
        }
    }
}
