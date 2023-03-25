// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using BuildXL.Cache.ContentStore.Distributed.Blob;
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
    public record BlobClusterStateStorageConfiguration
    {
        public record StorageSettings(AzureStorageCredentials Credentials, string ContainerName = "checkpoints", string FolderName = "clusterState")
            : AzureBlobStorageFolder.Configuration(Credentials, ContainerName, FolderName);

        public required StorageSettings Storage { get; init; }

        public BlobFolderStorageConfiguration BlobFolderStorageConfiguration { get; set; } = new BlobFolderStorageConfiguration();

        public string FileName { get; set; } = "clusterState.json";

        public ClusterStateRecomputeConfiguration RecomputeConfiguration { get; set; } = new ClusterStateRecomputeConfiguration();
    }

    public class BlobClusterStateStorage : StartupShutdownComponentBase
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(BlobClusterStateStorage));

        private readonly BlobClusterStateStorageConfiguration _configuration;
        private readonly IClock _clock;

        private readonly BlobStorageClientAdapter _storageClientAdapter;
        private readonly BlobClient _client;

        public BlobClusterStateStorage(
            BlobClusterStateStorageConfiguration configuration,
            IClock? clock = null)
        {
            _configuration = configuration;
            _clock = clock ?? SystemClock.Instance;

            _storageClientAdapter = new BlobStorageClientAdapter(Tracer, _configuration.BlobFolderStorageConfiguration);

            var azureBlobStorageFolder = _configuration.Storage.Create();
            _client = azureBlobStorageFolder.GetBlobClient(new BlobPath(_configuration.FileName, relative: true));
        }

        protected override async Task<BoolResult> StartupComponentAsync(OperationContext context)
        {
            await _storageClientAdapter.EnsureContainerExists(context, _client.GetParentBlobContainerClient()).ThrowIfFailureAsync();
            return BoolResult.Success;
        }

        public record RegisterMachineInput(IReadOnlyList<MachineLocation> MachineLocations);

        public record RegisterMachineOutput(ClusterStateMachine State, MachineMapping[] MachineMappings);

        public Task<Result<RegisterMachineOutput>> RegisterMachinesAsync(OperationContext context, RegisterMachineInput request)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var (currentState, assignedMachineIds) = await _storageClientAdapter.ReadModifyWriteAsync<ClusterStateMachine, MachineId[]>(
                        context,
                        _client,
                        currentState =>
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
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var (currentState, priorMachineRecords) = await _storageClientAdapter.ReadModifyWriteAsync<ClusterStateMachine, MachineRecord[]>(
                        context,
                        _client,
                        currentState =>
                        {
                            var now = _clock.UtcNow;

                            var priorMachineRecords = new MachineRecord[request.MachineIds.Count];
                            foreach (var entry in request.MachineIds.AsIndexed())
                            {
                                (currentState, priorMachineRecords[entry.Index]) =
                                    currentState.Heartbeat(entry.Item, now, request.MachineState).ThrowIfFailure();
                            }

                            return (currentState, priorMachineRecords);
                        }).ThrowIfFailureAsync();

                    return Result.Success(new HeartbeatOutput(TransitionInactiveMachines(currentState), priorMachineRecords));
                },
                traceOperationStarted: false);
        }

        public Task<Result<ClusterStateMachine>> ReadState(OperationContext context)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var currentState = await _storageClientAdapter.ReadAsync<ClusterStateMachine>(context, _client)
                        .ThrowIfFailureAsync();

                    return Result.Success(TransitionInactiveMachines(currentState));
                },
                traceOperationStarted: false);
        }

        private ClusterStateMachine TransitionInactiveMachines(ClusterStateMachine currentState)
        {
            return currentState.TransitionInactiveMachines(_configuration.RecomputeConfiguration, _clock.UtcNow);
        }
    }
}
