// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement
{
    internal class InMemoryClusterStateStorage : StartupShutdownSlimBase, IClusterStateStorage
    {
        protected override Tracer Tracer { get; } = new(nameof(InMemoryClusterStateStorage));

        private readonly SemaphoreSlim _lock = new(1);
        private readonly IClock _clock = SystemClock.Instance;

        private ClusterStateMachine _clusterStateMachine = new();

        // TODO: make configurable
        private readonly ClusterStateRecomputeConfiguration _recomputeConfiguration = new();

        public Task<Result<IClusterStateStorage.HeartbeatOutput>> HeartbeatAsync(OperationContext context, IClusterStateStorage.HeartbeatInput request)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                using var guard = await _lock.AcquireAsync(context.Token);
                var (currentState, result) = _clusterStateMachine.HeartbeatMany(request, _clock.UtcNow);
                _clusterStateMachine = currentState;
                return Result.Success(new IClusterStateStorage.HeartbeatOutput(_clusterStateMachine, result));
            });
        }

        public Task<Result<ClusterStateMachine>> ReadStateAsync(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                using var guard = await _lock.AcquireAsync(context.Token);
                return Result.Success(_clusterStateMachine);
            });
        }

        public Task<Result<IClusterStateStorage.RegisterMachineOutput>> RegisterMachinesAsync(OperationContext context, IClusterStateStorage.RegisterMachineInput request)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                using var guard = await _lock.AcquireAsync(context.Token);
                var (currentState, assignedMachineIds) = _clusterStateMachine.RegisterMany(_recomputeConfiguration, request, _clock.UtcNow);
                _clusterStateMachine = currentState;

                var machineMappings = request.MachineLocations
                    .Zip(assignedMachineIds, (machineLocation, machineId) => new MachineMapping(machineId, machineLocation))
                    .ToArray();

                return Result.Success(new IClusterStateStorage.RegisterMachineOutput(_clusterStateMachine, machineMappings));
            });
        }
    }
}
