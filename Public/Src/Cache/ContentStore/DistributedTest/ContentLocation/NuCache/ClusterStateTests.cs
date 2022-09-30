// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class ClusterStateTests
    {
        private readonly MemoryClock _clock = new MemoryClock();

        [Fact]
        public void MachineLookupIsCaseInsensitive()
        {
            var tracingContext = new Context(TestGlobal.Logger);
            var context = new OperationContext(tracingContext, default);

            var machineLocation = new MachineLocation(@"grpc://fun.com:123");
            var casedMachineLocation = new MachineLocation(@"grpc://FuN.com:123");
            var clusterStateMachine = new ClusterStateMachine();

            MachineId machineId;
            (clusterStateMachine, machineId) = clusterStateMachine.RegisterMachine(machineLocation, _clock.UtcNow);

            var clusterState = new ClusterState(machineId, new[] { new MachineMapping(machineId, machineLocation) });
            clusterState.Update(context, clusterStateMachine, _clock.UtcNow).ThrowIfFailure();

            clusterState.TryResolve(machineId, out var resolvedMachineLocation).Should().BeTrue();
            resolvedMachineLocation.Equals(machineLocation).Should().BeTrue();
            resolvedMachineLocation.Equals(casedMachineLocation).Should().BeTrue();

            clusterState.TryResolve(new MachineId(machineId.Index), out var resolvedMachineLocation2).Should().BeTrue();
            resolvedMachineLocation2.Equals(machineLocation).Should().BeTrue();
            resolvedMachineLocation2.Equals(casedMachineLocation).Should().BeTrue();
            resolvedMachineLocation2.Equals(resolvedMachineLocation).Should().BeTrue();

            clusterState.TryResolveMachineId(resolvedMachineLocation, out var resolvedMachineId).Should().BeTrue();
            resolvedMachineId.Equals(machineId).Should().BeTrue();

            clusterState.TryResolveMachineId(machineLocation, out var resolvedMachineId2).Should().BeTrue();
            resolvedMachineId2.Equals(machineId).Should().BeTrue();

            clusterState.TryResolveMachineId(casedMachineLocation, out var resolvedMachineId3).Should().BeTrue();
            resolvedMachineId3.Equals(machineId).Should().BeTrue();
            resolvedMachineId3.Equals(resolvedMachineId).Should().BeTrue();
        }
    }
}
