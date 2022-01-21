// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Utilities.Tasks;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public class ClusterStateManager : StartupShutdownSlimBase
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(ClusterStateManager));

        private readonly LocalLocationStoreConfiguration _configuration;

        private readonly IClock _clock;

        public ClusterState ClusterState { get; private set; } = ClusterState.CreateForTest();

        private readonly IClusterStateStorage _storage;

        public ClusterStateManager(
            LocalLocationStoreConfiguration configuration,
            IClusterStateStorage storage,
            IClock? clock = null)
        {
            _configuration = configuration;
            _storage = storage;
            _clock = clock ?? SystemClock.Instance;
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _storage.StartupAsync(context).ThrowIfFailureAsync();

            var machineLocations = (new[] { _configuration.PrimaryMachineLocation }).Concat(_configuration.AdditionalMachineLocations);
            var machineMappings = (await TaskUtilities.SafeWhenAll(machineLocations.Select(machineLocation => RegisterMachineAsync(context, machineLocation).ThrowIfFailureAsync()))).ToList();
            Contract.Assert(machineMappings.Count > 0, "Cluster State needs at least 1 machine mapping to function");

            var machineMappingsString = string.Join(", ", machineMappings.Select(m => m.ToString()));
            Tracer.Info(context, $"Initializing Cluster State with machine mappings: {machineMappingsString}");

            ClusterState = new ClusterState(machineMappings[0].Id, machineMappings);

            return BoolResult.Success;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await _storage.ShutdownAsync(context).ThrowIfFailureAsync();

            return BoolResult.Success;
        }

        internal Task<Result<MachineMapping>> RegisterMachineAsync(OperationContext context, MachineLocation machineLocation)
        {
            Contract.Requires(machineLocation.IsValid, $"Specified machine location `{machineLocation}` can't be registered because it is invalid");

            if (_configuration.DistributedContentConsumerOnly)
            {
                return Task.FromResult(Result.Success(new MachineMapping(machineLocation, MachineId.Invalid)));
            }

            return _storage.RegisterMachineAsync(context, machineLocation);
        }

        public Task<Result<MachineState>> UpdateClusterStateAsync(
            OperationContext context,
            MachineState machineState = MachineState.Unknown,
            ClusterState? clusterState = null,
            Role? currentRole = null)
        {
            clusterState ??= ClusterState;

            // Due to initialization issues the instance level ClusterState still can be null.
            if (clusterState is null)
            {
                return Task.FromResult(Result.FromErrorMessage<MachineState>("Failed to update cluster state because the existing cluster state is null."));
            }

            var startMaxMachineId = clusterState.MaxMachineId;

            int postDbMaxMachineId = startMaxMachineId;
            int postGlobalMaxMachineId = startMaxMachineId;

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var updateResult = await UpdateClusterStateCoreAsync(context, clusterState, machineState);
                    postGlobalMaxMachineId = clusterState.MaxMachineId;

                    if (currentRole == Role.Master && _configuration.UseBinManager)
                    {
                        Tracer.Info(context, $"Initializing bin manager");
                        clusterState.InitializeBinManagerIfNeeded(locationsPerBin: _configuration.ProactiveCopyLocationsThreshold, _clock, expiryTime: _configuration.PreferredLocationsExpiryTime);
                    }

                    return updateResult;
                },
                extraEndMessage: result => $"[MaxMachineId=({startMaxMachineId} -> (Db={postDbMaxMachineId}, Global={postGlobalMaxMachineId}))]");
        }

        private async Task<Result<MachineState>> UpdateClusterStateCoreAsync(
            OperationContext context,
            ClusterState clusterState,
            MachineState machineState)
        {
            var heartbeatResponse = await CallHeartbeatAsync(context, clusterState, machineState);

            var updates = await _storage.GetClusterUpdatesAsync(context, new GetClusterUpdatesRequest()
            {
                MaxMachineId = clusterState.MaxMachineId
            }).ThrowIfFailureAsync();

            BitMachineIdSet inactiveMachineIdSet = heartbeatResponse.InactiveMachines;
            BitMachineIdSet closedMachineIdSet = heartbeatResponse.ClosedMachines;

            Contract.Assert(inactiveMachineIdSet != null, "inactiveMachineIdSet != null");
            Contract.Assert(closedMachineIdSet != null, "closedMachineIdSet != null");

            if (updates.MaxMachineId != clusterState.MaxMachineId)
            {
                Tracer.Debug(context, $"Retrieved unknown machines from ({clusterState.MaxMachineId}, {updates.MaxMachineId}]");
                if (updates.UnknownMachines != null)
                {
                    foreach (var item in updates.UnknownMachines)
                    {
                        context.LogMachineMapping(Tracer, item.Key, item.Value);
                    }
                }
            }

            if (updates.UnknownMachines != null)
            {
                clusterState.AddUnknownMachines(updates.MaxMachineId, updates.UnknownMachines);
            }

            clusterState.SetMachineStates(inactiveMachineIdSet, closedMachineIdSet).ThrowIfFailure();

            Tracer.Debug(context, $"Inactive machines: Count={inactiveMachineIdSet.Count}, [{string.Join(", ", inactiveMachineIdSet)}]");
            Tracer.TrackMetric(context, "InactiveMachineCount", inactiveMachineIdSet.Count);

            if (!_configuration.DistributedContentConsumerOnly)
            {
                foreach (var machineMapping in clusterState.LocalMachineMappings)
                {
                    if (!clusterState.TryResolveMachineId(machineMapping.Location, out var machineId))
                    {
                        return Result.FromErrorMessage<MachineState>($"Invalid cluster state on machine {machineMapping}. (Missing location {machineMapping.Location})");
                    }
                    else if (machineId != machineMapping.Id)
                    {
                        Tracer.Warning(context, $"Machine id mismatch for location {machineMapping.Location}. Registered id: {machineMapping.Id}. Cluster state id: {machineId}. Updating registered id with cluster state id.");
                        machineMapping.Id = machineId;
                    }

                    if (updates.MaxMachineId < machineMapping.Id.Index)
                    {
                        return Result.FromErrorMessage<MachineState>($"Invalid cluster state on machine {machineMapping} (max machine id={updates.MaxMachineId})");
                    }
                }
            }

            return heartbeatResponse.PriorState;
        }

        public async Task<HeartbeatMachineResponse> CallHeartbeatAsync(
            OperationContext context,
            ClusterState clusterState,
            MachineState machineState)
        {
            // There is very low concurrency here, machines have 1 or 2 local machine mappings
            var responses = await TaskUtilities.SafeWhenAll(clusterState.LocalMachineMappings.Select(async m =>
            {
                var response = await _storage.HeartbeatAsync(context, new HeartbeatMachineRequest()
                {
                    MachineId = m.Id,
                    Location = m.Location,
                    Name = Environment.MachineName,
                    DeclaredMachineState = machineState
                }).ThrowIfFailureAsync();

                var priorState = response.PriorState;

                if (priorState != machineState)
                {
                    Tracer.Debug(context, $"Machine {m} state changed from {priorState} to {machineState}");
                }

                if (priorState == MachineState.DeadUnavailable || priorState == MachineState.DeadExpired)
                {
                    clusterState.LastInactiveTime = _clock.UtcNow;
                }

                return response;
            }));

            return responses.FirstOrDefault() ?? new HeartbeatMachineResponse()
            {
                PriorState = MachineState.Unknown,
                InactiveMachines = BitMachineIdSet.EmptyInstance,
                ClosedMachines = BitMachineIdSet.EmptyInstance
            };
        }
    }
}
