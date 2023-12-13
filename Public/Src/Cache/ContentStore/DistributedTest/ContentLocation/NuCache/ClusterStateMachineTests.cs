// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
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

        // WARNING: DO NOT DISABLE THIS TEST. READ BELOW.
        [Fact]
        public void ClusterStateSerializationRoundrip()
        {
            // This test is testing that serialization for: ClusterStateMachine, MachineRecord, and MachineId is
            // entirely backwards-compatible. If it isn't, a change can break a stamp by breaking either ClusterState,
            // RocksDbContentLocationDatabase, or both of them, and completely obliterate the cluster.

            var clusterState = new ClusterStateMachine();
            (clusterState, _) = clusterState.RegisterMachineForTests(MachineLocation.Create("node", 1234), DateTime.MinValue, false);

            var str = JsonUtilities.JsonSerialize(clusterState, indent: false);
            str.Should().BeEquivalentTo(@"{""NextMachineId"":2,""Records"":[{""Id"":1,""Location"":""grpc://node:1234/"",""State"":""Open"",""LastHeartbeatTimeUtc"":""0001-01-01T00:00:00"",""Persistent"":false}]}");

            var deserialized = JsonUtilities.JsonDeserialize<ClusterStateMachine>(str);
            deserialized.NextMachineId.Should().Be(clusterState.NextMachineId);
            deserialized.Records.Count.Should().Be(1);
        }

        [Fact]
        public void ClusterStateDeserializationWithUnknownArguments()
        {
            // This test verifies that non-existent properties do not fail the deserialization of ClusterState. This is
            // important because we want to be able to add new properties to ClusterState without breaking the cluster,
            // and this condition can happen while deploying.
            var str = @"{""NextMachineId"":2,""Records"":[{""Id"":1,""Location"":""grpc://node:1234/"",""State"":""Open"",""LastHeartbeatTimeUtc"":""0001-01-01T00:00:00"",""ThisDoesNotExist"":false}]}";
            var deserialized = JsonUtilities.JsonDeserialize<ClusterStateMachine>(str);
            deserialized.Records.Count.Should().Be(1);
            deserialized.NextMachineId.Should().Be(deserialized.Records[0].Id.Index + 1);
        }

        [Fact]
        public void RegisterNewMachineUsesCorrectDefaults()
        {
            var clusterState = new ClusterStateMachine();
            var nowUtc = _clock.UtcNow;
            MachineId machineId;

            var machineLocation = MachineLocation.Create(@"node1", 123);

            (clusterState, machineId) = clusterState.RegisterMachineForTests(machineLocation, nowUtc, false);
            machineId.Index.Should().Be(MachineId.MinValue);

            var record = clusterState.GetRecord(machineId).ThrowIfFailure();
            record.Should().BeEquivalentTo(
                new MachineRecord()
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

            (clusterState, machineId) = clusterState.RegisterMachineForTests(MachineLocation.Create("node1", 1234), nowUtc, false);
            machineId.Index.Should().Be(1);

            (_, machineId) = clusterState.RegisterMachineForTests(MachineLocation.Create("node2", 1234), nowUtc, false);
            machineId.Index.Should().Be(2);
        }

        [Fact]
        public void RegisterMachineTransitionTests()
        {
            var clusterState = new ClusterStateMachine();
            var nowUtc = _clock.UtcNow;

            // During transition, all machines will be added forcefully without regard for the consistency of the data
            // structure
            clusterState = clusterState.ForceTakeoverMachine(new MachineId(3), MachineLocation.Create("A", 0), nowUtc, persistent: false);
            clusterState.NextMachineId.Should().Be(4);

            clusterState = clusterState.ForceTakeoverMachine(new MachineId(8), MachineLocation.Create("B", 0), nowUtc, persistent: false);
            clusterState.NextMachineId.Should().Be(9);

            clusterState = clusterState.ForceTakeoverMachine(new MachineId(16), MachineLocation.Create("C", 0), nowUtc, persistent: false);
            clusterState.NextMachineId.Should().Be(17);

            clusterState = clusterState.ForceTakeoverMachine(new MachineId(20), MachineLocation.Create("D", 0), nowUtc, persistent: false);
            clusterState.NextMachineId.Should().Be(21);

            clusterState = clusterState.ForceTakeoverMachine(new MachineId(23), MachineLocation.Create("D", 0), nowUtc, persistent: false);
            clusterState.NextMachineId.Should().Be(24);

            // After transition, adding proceeds as usual, by appending to the end basically
            MachineId n1Id;
            (clusterState, n1Id) = clusterState.RegisterMachineForTests(MachineLocation.Create("MachineAddedAfterTransition", 0), nowUtc, false);
            n1Id.Index.Should().Be(24);
            clusterState.NextMachineId.Should().Be(25);
        }

        [Fact]
        public void HeartbeatUpdatesLastHeartbeatTimeAndState()
        {
            var clusterState = new ClusterStateMachine();
            MachineId machineId;

            (clusterState, machineId) = clusterState.RegisterMachineForTests(MachineLocation.Create("node1", 1234), _clock.UtcNow, false);

            _clock.Increment(TimeSpan.FromMinutes(1));
            clusterState = clusterState.Heartbeat(machineId, _clock.UtcNow, MachineState.Open).ThrowIfFailure().Next;

            var r = clusterState.GetRecord(machineId).ShouldBeSuccess().Value;
            r.LastHeartbeatTimeUtc.Should().Be(_clock.UtcNow);
            r.State.Should().Be(MachineState.Open);
        }

        [Fact]
        public void HeartbeatKeepsOtherRecordsAsIs()
        {
            var clusterState = new ClusterStateMachine();
            var nowUtc = _clock.UtcNow;

            MachineId n1Id;
            (clusterState, n1Id) = clusterState.RegisterMachineForTests(MachineLocation.Create("node1", 1234), nowUtc, false);

            MachineId n2Id;
            (clusterState, n2Id) = clusterState.RegisterMachineForTests(MachineLocation.Create("node2", 1234), nowUtc, false);

            _clock.Increment(TimeSpan.FromMinutes(1));
            clusterState = clusterState.Heartbeat(n1Id, _clock.UtcNow, MachineState.Closed).ThrowIfFailure().Next;

            var r = clusterState.GetRecord(n2Id).ShouldBeSuccess().Value;
            r.LastHeartbeatTimeUtc.Should().Be(nowUtc);
            r.State.Should().Be(MachineState.Open);
        }

        [Fact]
        public void RecomputeChangesStatesAsExpected()
        {
            var clusterState = new ClusterStateMachine();
            var cfg = new ClusterStateRecomputeConfiguration();

            var nowUtc = _clock.UtcNow;

            // We want to test all possible state machine transitions. To do so, we generate a very specific instance
            // of cluster state meant to transition the way we expect instead of actually simulating each possible
            // branch.

            var n1 = MachineLocation.Create("node2", 0);
            MachineId n1Id;
            (clusterState, n1Id) = clusterState.ForceRegisterMachineWithStateForTests(n1, nowUtc, MachineState.DeadUnavailable, persistent: false);

            var n2 = MachineLocation.Create("node3", 0);
            MachineId n2Id;
            (clusterState, n2Id) = clusterState.ForceRegisterMachineWithStateForTests(n2, nowUtc, MachineState.DeadExpired, persistent: false);

            var n3 = MachineLocation.Create("node4", 0);
            MachineId n3Id;
            (clusterState, n3Id) = clusterState.ForceRegisterMachineWithStateForTests(n3, nowUtc - cfg.ActiveToExpired, MachineState.Open, persistent: false);

            var n4 = MachineLocation.Create("node5", 0);
            MachineId n4Id;
            (clusterState, n4Id) = clusterState.ForceRegisterMachineWithStateForTests(n4, nowUtc - cfg.ActiveToClosed, MachineState.Open, persistent: false);

            var n5 = MachineLocation.Create("node6", 0);
            MachineId n5Id;
            (clusterState, n5Id) = clusterState.ForceRegisterMachineWithStateForTests(n5, nowUtc, MachineState.Open, persistent: false);

            var n6 = MachineLocation.Create("node7", 0);
            MachineId n6Id;
            (clusterState, n6Id) = clusterState.ForceRegisterMachineWithStateForTests(n6, nowUtc - cfg.ClosedToExpired, MachineState.Closed, persistent: false);

            var n7 = MachineLocation.Create("node8", 0);
            MachineId n7Id;
            (clusterState, n7Id) = clusterState.ForceRegisterMachineWithStateForTests(n7, nowUtc, MachineState.Closed, persistent: false);

            clusterState = clusterState.TransitionInactiveMachines(cfg, nowUtc);

            clusterState.GetRecord(n1Id).ThrowIfFailure().State.Should().Be(MachineState.DeadUnavailable);
            clusterState.GetRecord(n2Id).ThrowIfFailure().State.Should().Be(MachineState.DeadExpired);
            clusterState.GetRecord(n3Id).ThrowIfFailure().State.Should().Be(MachineState.DeadExpired);
            clusterState.GetRecord(n4Id).ThrowIfFailure().State.Should().Be(MachineState.Closed);
            clusterState.GetRecord(n5Id).ThrowIfFailure().State.Should().Be(MachineState.Open);
            clusterState.GetRecord(n6Id).ThrowIfFailure().State.Should().Be(MachineState.DeadExpired);
            clusterState.GetRecord(n7Id).ThrowIfFailure().State.Should().Be(MachineState.Closed);
        }


        [Fact]
        public void InactiveMachineIdsAreReclaimed()
        {
            var clusterState = new ClusterStateMachine();
            var cfg = new ClusterStateRecomputeConfiguration();

            var n1 = MachineLocation.Create("node1", 0);

            (clusterState, var ids) = clusterState.RegisterMany(cfg, new IClusterStateStorage.RegisterMachineInput(new[] { n1 }), _clock.UtcNow);
            var n1Id = ids[0];

            _clock.Increment(cfg.ActiveToUnavailable + TimeSpan.FromHours(1));

            var n2 = MachineLocation.Create("node2", 0);
            (clusterState, ids) = clusterState.RegisterMany(cfg, new IClusterStateStorage.RegisterMachineInput(new[] { n2 }), _clock.UtcNow);
            var n2Id = ids[0];

            n2Id.Index.Should().Be(2, "The machine ID should be 2 because the registration shouldn't have been allowed to take over IDs");

            var n3 = MachineLocation.Create("node3", 0);
            (clusterState, ids) = clusterState.RegisterMany(cfg, new IClusterStateStorage.RegisterMachineInput(new[] { n3 }), _clock.UtcNow);
            var n3Id = ids[0];

            clusterState.GetRecord(n3Id).ThrowIfFailure().Location.Should().Be(n3);
            n3Id.Index.Should().Be(1, "The machine ID for node3 should be 1 because it should have taken over node1's due to inactivity");
        }

        [Fact]
        public void RegisterPersistentLocationWorks()
        {
            var clusterState = new ClusterStateMachine();
            var machineLocation = MachineLocation.Create("persistentNode", 5678);
            var nowUtc = _clock.UtcNow;

            (clusterState, var id) = clusterState.RegisterMachineForTests(machineLocation, nowUtc, true);
            var record = clusterState.GetRecord(id).ThrowIfFailure();

            record.Location.Should().Be(machineLocation);
            record.Persistent.Should().BeTrue();
        }

        [Fact]
        public void PersistentLocationsCannotBeTakenOverOrReRegistered()
        {
            var clusterState = new ClusterStateMachine();
            var persistentMachineLocation = MachineLocation.Create("persistentNode", 5678);
            var nowUtc = _clock.UtcNow;

            // Register persistent machine
            (clusterState, var id) = clusterState.RegisterMachineForTests(persistentMachineLocation, nowUtc, true);
            clusterState = clusterState.Heartbeat(id, nowUtc, MachineState.Closed).ThrowIfFailure().Next;

            // Attempt to take over the persistent machine with a new machine
            var newMachineLocation = MachineLocation.Create("newNode", 5678);
            (clusterState, _) = clusterState.RegisterMachineForTests(newMachineLocation, nowUtc, false);

            var record = clusterState.GetRecord(id).ThrowIfFailure();
            record.Location.Should().NotBe(newMachineLocation);
            record.Persistent.Should().BeTrue();
        }

        [Fact]
        public void PersistentLocationStateAlwaysOpen()
        {
            var clusterState = new ClusterStateMachine();
            var persistentMachineLocation = MachineLocation.Create("persistentNode", 5678);
            var nowUtc = _clock.UtcNow;

            // Register persistent machine
            (clusterState, var machineId) = clusterState.RegisterMachineForTests(persistentMachineLocation, nowUtc, true);

            var record = clusterState.GetRecord(machineId).ShouldBeSuccess().Value;
            record!.State.Should().Be(MachineState.Open);

            // Attempt to change the state of the persistent machine
            clusterState = clusterState.Heartbeat(machineId, nowUtc, MachineState.Closed).ThrowIfFailure().Next;

            record = clusterState.GetRecord(machineId).ShouldBeSuccess().Value;
            record!.State.Should().Be(MachineState.Open);
        }

        [Fact]
        public void PersistentLocationCannotTakeOverInactiveMachineId()
        {
            var clusterState = new ClusterStateMachine();
            var cfg = new ClusterStateRecomputeConfiguration();

            // Register a non-persistent machine and make it inactive
            var inactiveMachineLocation = MachineLocation.Create("inactiveNode", 1234);
            (clusterState, var inactiveMachineId) = clusterState.RegisterMachineForTests(inactiveMachineLocation, _clock.UtcNow, false);

            // Increment time to make the machine inactive
            _clock.Increment(cfg.ActiveToUnavailable + TimeSpan.FromHours(1));

            // Register a persistent machine
            var persistentMachineLocation = MachineLocation.Create("persistentNode", 5678);
            (clusterState, var persistentMachineId) = clusterState.RegisterMachineForTests(persistentMachineLocation, _clock.UtcNow, true);

            // Verify that the persistent machine did not take over the ID of the inactive machine
            persistentMachineId.Should().NotBe(inactiveMachineId);
            var persistentRecord = clusterState.GetRecord(persistentMachineId).ThrowIfFailure();
            persistentRecord.Persistent.Should().BeTrue();

            var inactiveRecord = clusterState.GetRecord(inactiveMachineId).ThrowIfFailure();
            inactiveRecord.Location.Should().Be(inactiveMachineLocation);
        }
    }
}
