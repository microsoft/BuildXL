// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class ClusterStateMachineTests
    {
        private readonly MemoryClock _clock = new MemoryClock();

        [Fact]
        public void RegisterNewMachineUsesCorrectDefaults()
        {
            var clusterState = new ClusterStateMachine();
            var nowUtc = _clock.UtcNow;
            MachineId machineId;

            var machineLocation = new MachineLocation("node1");

            (clusterState, machineId) = clusterState.RegisterMachine(machineLocation, nowUtc);
            machineId.Index.Should().Be(MachineId.MinValue);

            var record = clusterState.GetStatus(machineId).ThrowIfFailure();
            record.Should().BeEquivalentTo(new MachineRecord()
            {
                Id = new MachineId(MachineId.MinValue),
                Location = machineLocation,
                State = ClusterStateMachine.InitialState,
                LastHeartbeatTimeUtc = nowUtc,
            });
        }

        [Fact]
        public void RegisterMachineEmitsIdsInSequence()
        {
            var clusterState = new ClusterStateMachine();
            var nowUtc = _clock.UtcNow;
            MachineId machineId;

            (clusterState, machineId) = clusterState.RegisterMachine(new MachineLocation("node1"), nowUtc);
            machineId.Index.Should().Be(1);

            (_, machineId) = clusterState.RegisterMachine(new MachineLocation("node2"), nowUtc);
            machineId.Index.Should().Be(2);
        }

        [Fact]
        public void RegisterMachineTransitionTests()
        {
            var clusterState = new ClusterStateMachine();
            var nowUtc = _clock.UtcNow;

            // During transition, all machines will be added forcefully without regard for the consistency of the data
            // structure
            clusterState = clusterState.ForceRegisterMachine(new MachineId(3), MachineLocation.Create("A", 0), nowUtc);
            clusterState.NextMachineId.Should().Be(4);

            clusterState = clusterState.ForceRegisterMachine(new MachineId(8), MachineLocation.Create("B", 0), nowUtc);
            clusterState.NextMachineId.Should().Be(9);

            clusterState = clusterState.ForceRegisterMachine(new MachineId(16), MachineLocation.Create("C", 0), nowUtc);
            clusterState.NextMachineId.Should().Be(17);

            clusterState = clusterState.ForceRegisterMachine(new MachineId(20), MachineLocation.Create("D", 0), nowUtc);
            clusterState.NextMachineId.Should().Be(21);

            clusterState = clusterState.ForceRegisterMachine(new MachineId(23), MachineLocation.Create("D", 0), nowUtc);
            clusterState.NextMachineId.Should().Be(24);

            // After transition, adding proceeds as usual, by appending to the end basically
            MachineId n1Id;
            (clusterState, n1Id) = clusterState.RegisterMachine(MachineLocation.Create("Machine Gets Added After Transition", 0), nowUtc);
            n1Id.Index.Should().Be(24);
            clusterState.NextMachineId.Should().Be(25);
        }

        [Fact]
        public void HeartbeatUpdatesLastHeartbeatTimeAndState()
        {
            var clusterState = new ClusterStateMachine();
            MachineId machineId;

            (clusterState, machineId) = clusterState.RegisterMachine(new MachineLocation("node1"), _clock.UtcNow);

            _clock.Increment(TimeSpan.FromMinutes(1));
            clusterState = clusterState.Heartbeat(machineId, _clock.UtcNow, MachineState.Open).ThrowIfFailure().Next;

            var r = clusterState.GetStatus(machineId).ShouldBeSuccess().Value;
            r.LastHeartbeatTimeUtc.Should().Be(_clock.UtcNow);
            r.State.Should().Be(MachineState.Open);
        }

        [Fact]
        public void HeartbeatKeepsOtherRecordsAsIs()
        {
            var clusterState = new ClusterStateMachine();
            var nowUtc = _clock.UtcNow;

            MachineId n1Id;
            (clusterState, n1Id) = clusterState.RegisterMachine(new MachineLocation("node1"), nowUtc);

            MachineId n2Id;
            (clusterState, n2Id) = clusterState.RegisterMachine(new MachineLocation("node2"), nowUtc);

            _clock.Increment(TimeSpan.FromMinutes(1));
            clusterState = clusterState.Heartbeat(n1Id, _clock.UtcNow, MachineState.Closed).ThrowIfFailure().Next;

            var r = clusterState.GetStatus(n2Id).ShouldBeSuccess().Value;
            r.LastHeartbeatTimeUtc.Should().Be(nowUtc);
            r.State.Should().Be(MachineState.Open);
        }

        [Fact]
        public void HeartbeatDoesntChangeRecomputeTime()
        {
            var clusterState = new ClusterStateMachine();
            MachineId n1Id;

            (clusterState, n1Id) = clusterState.RegisterMachine(new MachineLocation("node1"), _clock.UtcNow);

            _clock.Increment(TimeSpan.FromMinutes(1));
            clusterState = clusterState.Heartbeat(n1Id, _clock.UtcNow, MachineState.Open).ThrowIfFailure().Next;

            clusterState.LastStateMachineRecomputeTimeUtc.Should().Be(DateTime.MinValue);
        }

        [Fact]
        public void RecomputeDoesntRunIfNotNeeded()
        {
            var now = _clock.UtcNow;
            var current = new ClusterStateMachine()
            {
                LastStateMachineRecomputeTimeUtc = now,
            };

            var cfg = new ClusterStateRecomputeConfiguration()
            {
                RecomputeFrequency = TimeSpan.FromMilliseconds(1),
            };

            var next = current.Recompute(cfg, now);

            next.Should().BeEquivalentTo(current);
        }

        [Fact]
        public void RecomputeChangesStatesAsExpected()
        {
            var clusterState = new ClusterStateMachine();
            var cfg = new ClusterStateRecomputeConfiguration()
            {
                // Force recompute to run
                RecomputeFrequency = TimeSpan.Zero,
            };

            var nowUtc = _clock.UtcNow;

            // We want to test all possible state machine transitions. To do so, we generate a very specific instance
            // of cluster state meant to transition the way we expect instead of actually simulating each possible
            // branch.

            var n1 = MachineLocation.Create("node2", 0);
            MachineId n1Id;
            (clusterState, n1Id) = clusterState.ForceRegisterMachineWithState(n1, nowUtc, MachineState.DeadUnavailable);

            var n2 = MachineLocation.Create("node3", 0);
            MachineId n2Id;
            (clusterState, n2Id) = clusterState.ForceRegisterMachineWithState(n2, nowUtc, MachineState.DeadExpired);

            var n3 = MachineLocation.Create("node4", 0);
            MachineId n3Id;
            (clusterState, n3Id) = clusterState.ForceRegisterMachineWithState(n3, nowUtc - cfg.ActiveToDeadExpiredInterval, MachineState.Open);

            var n4 = MachineLocation.Create("node5", 0);
            MachineId n4Id;
            (clusterState, n4Id) = clusterState.ForceRegisterMachineWithState(n4, nowUtc - cfg.ActiveToClosedInterval, MachineState.Open);

            var n5 = MachineLocation.Create("node6", 0);
            MachineId n5Id;
            (clusterState, n5Id) = clusterState.ForceRegisterMachineWithState(n5, nowUtc, MachineState.Open);

            var n6 = MachineLocation.Create("node7", 0);
            MachineId n6Id;
            (clusterState, n6Id) = clusterState.ForceRegisterMachineWithState(n6, nowUtc - cfg.ClosedToDeadExpiredInterval, MachineState.Closed);

            var n7 = MachineLocation.Create("node8", 0);
            MachineId n7Id;
            (clusterState, n7Id) = clusterState.ForceRegisterMachineWithState(n7, nowUtc, MachineState.Closed);

            clusterState = clusterState.Recompute(cfg, nowUtc);

            clusterState.GetStatus(n1Id).ThrowIfFailure().State.Should().Be(MachineState.DeadUnavailable);
            clusterState.GetStatus(n2Id).ThrowIfFailure().State.Should().Be(MachineState.DeadExpired);
            clusterState.GetStatus(n3Id).ThrowIfFailure().State.Should().Be(MachineState.DeadExpired);
            clusterState.GetStatus(n4Id).ThrowIfFailure().State.Should().Be(MachineState.Closed);
            clusterState.GetStatus(n5Id).ThrowIfFailure().State.Should().Be(MachineState.Open);
            clusterState.GetStatus(n6Id).ThrowIfFailure().State.Should().Be(MachineState.DeadExpired);
            clusterState.GetStatus(n7Id).ThrowIfFailure().State.Should().Be(MachineState.Closed);
        }
    }
}
