// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public class ClusterStateManager : StartupShutdownComponentBase
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(ClusterStateManager));

        public override bool AllowMultipleStartupAndShutdowns => true;

        private readonly LocalLocationStoreConfiguration _configuration;

        private readonly IClock _clock;

        private readonly BlobClusterStateStorage _storage;

        public ClusterState ClusterState { get; private set; } = ClusterState.CreateForTest();

        public ClusterStateManager(
            LocalLocationStoreConfiguration configuration,
            BlobClusterStateStorage storage,
            IClock? clock = null)
        {
            _configuration = configuration;
            _storage = storage;
            _clock = clock ?? SystemClock.Instance;

            LinkLifetime(_storage);

            if (_configuration.Checkpoint?.UpdateClusterStateInterval > TimeSpan.Zero)
            {
                RunInBackground(nameof(BackgroundUpdateAsync), BackgroundUpdateAsync, fireAndForget: true);
            }
        }

        protected override async Task<BoolResult> StartupComponentAsync(OperationContext context)
        {
            var machineLocations = (new[] { _configuration.PrimaryMachineLocation }).Concat(_configuration.AdditionalMachineLocations).ToArray();

            MachineMapping[] machineMappings;
            ClusterStateMachine currentState;
            if (_configuration.DistributedContentConsumerOnly)
            {
                currentState = await _storage.ReadState(context).ThrowIfFailureAsync();
                machineMappings = machineLocations.Select(machineLocation => new MachineMapping(MachineId.Invalid, machineLocation)).ToArray();
            }
            else
            {
                (currentState, machineMappings) = await RegisterMachinesAsync(context, machineLocations).ThrowIfFailureAsync();
            }

            foreach (var mapping in machineMappings)
            {
                Tracer.Info(context, $"Machine mapping created. Mapping=[{mapping}]");
            }

            var initialState = new ClusterState(machineMappings[0].Id, machineMappings);
            initialState.Update(context, currentState, nowUtc: _clock.UtcNow).ThrowIfFailure();
            ClusterState = initialState;

            return BoolResult.Success;
        }

        private async Task<BoolResult> BackgroundUpdateAsync(OperationContext startupContext)
        {
            // The loop will stop running when shutdown starts
            using var cancellableContext = startupContext.WithCancellationToken(ShutdownStartedCancellationToken);
            var context = cancellableContext.Context;

            var updateFrequency = _configuration.Checkpoint!.UpdateClusterStateInterval;
            while (true)
            {
                context.Token.ThrowIfCancellationRequested();

                // Wait until it's the right time to update
                var nextUpdateTime = ClusterState.LastUpdateTimeUtc + updateFrequency;
                var now = _clock.UtcNow;
                if (nextUpdateTime > now)
                {
                    await Task.Delay(nextUpdateTime - now, context.Token);

                    // Something else may have updated before we did
                    continue;
                }

                // This loop will never update the state of the machine, only refresh the local view of the remote
                // cluster state and update the last heartbeat time in the remote.
                await HeartbeatAsync(context, MachineState.Unknown).IgnoreFailure();
            }
        }

        private Task<Result<(ClusterStateMachine State, MachineMapping[] MachineMappings)>> RegisterMachinesAsync(OperationContext context, IReadOnlyList<MachineLocation> machineLocations)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var registerMachinesResponse = await _storage.RegisterMachinesAsync(context, new BlobClusterStateStorage.RegisterMachineInput(machineLocations)).ThrowIfFailureAsync();

                return Result.Success((registerMachinesResponse.State, registerMachinesResponse.MachineMappings));
            });
        }

        /// <remarks>
        /// Used for testing only. DO NOT USE OUTSIDE TESTS.
        /// </remarks>
        internal async Task<Result<MachineMapping>> RegisterMachineForTestsAsync(OperationContext context, MachineLocation machineLocation)
        {
            return (await RegisterMachinesAsync(context, new[] { machineLocation })).Then(result =>
            {
                ClusterState.Update(context, result.State, nowUtc: _clock.UtcNow).ThrowIfFailure();
                return Result.Success(result.MachineMappings[0]);
            });
        }

        public Task<Result<MachineState>> HeartbeatAsync(
            OperationContext context,
            MachineState machineState)
        {
            // This is a weird method because it is meant to:
            //  1. Update the remote representation of cluster state with the current machine state
            //  2. Update the local representation of the cluster state
            //  3. Change or return the current machine state for this machine
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    if (machineState != MachineState.Unknown)
                    {
                        ClusterState.CurrentState = machineState;
                    }

                    var localMachineIds = ClusterState.LocalMachineMappings.Select(machineMapping => machineMapping.Id).ToArray();

                    if (_configuration.DistributedContentConsumerOnly || localMachineIds.Length == 0)
                    {
                        var currentState = await _storage.ReadState(context).ThrowIfFailureAsync();
                        ClusterState.Update(context, currentState, nowUtc: _clock.UtcNow).ThrowIfFailure();

                        // When in consumer-only mode, we should never update the remote representation of the cluster
                        // state. We will instead just return whatever came in.
                        return Result.Success(machineState);
                    }
                    else
                    {
                        var heartbeatResponse = await _storage.HeartbeatAsync(context, new BlobClusterStateStorage.HeartbeatInput(localMachineIds, machineState)).ThrowIfFailureAsync();
                        Contract.Assert(heartbeatResponse.PriorRecords.Length == localMachineIds.Length, "Mismatch between number of requested heartbeats and actual heartbeats. This should never happen.");

                        ClusterState.Update(context, heartbeatResponse.State, nowUtc: _clock.UtcNow).ThrowIfFailure();

                        var priorRecord = heartbeatResponse.PriorRecords[0];
                        if (!priorRecord.IsOpen())
                        {
                            ClusterState.LastInactiveTime = priorRecord.LastHeartbeatTimeUtc;
                        }

                        if (priorRecord.State != machineState)
                        {
                            Tracer.Debug(context, $"Machine state changed from {priorRecord.State} to {machineState}");
                        }

                        return Result.Success(priorRecord.State);
                    }
                },
                extraStartMessage: $"MachineState=[{machineState}]",
                extraEndMessage: _ => $"MachineState=[{machineState}]");
        }
    }
}
