// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    [Collection("Redis-based tests")]
    public class BlobClusterStateStorageTests : TestWithOutput
    {
        private readonly static MachineLocation M1 = new MachineLocation("M1");
        private readonly static MachineLocation M2 = new MachineLocation("M2");
        private readonly LocalRedisFixture _fixture;

        public BlobClusterStateStorageTests(LocalRedisFixture fixture, ITestOutputHelper output)
            : base(output)
        {
            _fixture = fixture;
        }

        [Fact]
        public Task SimpleGetUpdatesTests()
        {
            return RunTest(async (context, clock, storage) =>
            {
                var m1 = await storage.RegisterMachineAsync(context, MachineLocation.Create("A", 1)).ThrowIfFailureAsync();
                var m2 = await storage.RegisterMachineAsync(context, MachineLocation.Create("B", 1)).ThrowIfFailureAsync();

                var r = await storage.GetClusterUpdatesAsync(context, new GetClusterUpdatesRequest()
                {
                    MaxMachineId = 0,
                }).ThrowIfFailureAsync();
                r.MaxMachineId.Should().Be(2);
                r.UnknownMachines.Contains(new KeyValuePair<MachineId, MachineLocation>(m1.Id, m1.Location)).Should().BeTrue();
                r.UnknownMachines.Contains(new KeyValuePair<MachineId, MachineLocation>(m2.Id, m2.Location)).Should().BeTrue();

                r = await storage.GetClusterUpdatesAsync(context, new GetClusterUpdatesRequest()
                {
                    MaxMachineId = 1,
                }).ThrowIfFailureAsync();
                r.MaxMachineId.Should().Be(2);
                r.UnknownMachines.Contains(new KeyValuePair<MachineId, MachineLocation>(m2.Id, m2.Location)).Should().BeTrue();
            });
        }

        [Fact]
        public Task SimpleHeartbeatTest()
        {
            return RunTest(async (context, clock, storage) =>
            {
                var m1 = await storage.RegisterMachineAsync(context, MachineLocation.Create("A", 1)).ThrowIfFailureAsync();
                var m2 = await storage.RegisterMachineAsync(context, MachineLocation.Create("B", 1)).ThrowIfFailureAsync();

                // Transition m1 to Closed
                var r = await storage.HeartbeatAsync(context, new HeartbeatMachineRequest()
                {
                    MachineId = m1.Id,
                    Location = m1.Location,
                    Name = m1.Location.Path,
                    HeartbeatTime = clock.UtcNow,
                    DeclaredMachineState = MachineState.Closed,
                }).ThrowIfFailureAsync();

                r.PriorState.Should().Be(ClusterStateMachine.InitialState);
                r.ClosedMachines.Value.Contains(m1.Id).Should().BeTrue();
                r.InactiveMachines.Value.IsEmpty.Should().BeTrue();

                // Transition m1 to Open
                r = await storage.HeartbeatAsync(context, new HeartbeatMachineRequest()
                {
                    MachineId = m1.Id,
                    Location = m1.Location,
                    Name = m1.Location.Path,
                    HeartbeatTime = clock.UtcNow,
                    DeclaredMachineState = MachineState.Open,
                }).ThrowIfFailureAsync();

                r.PriorState.Should().Be(MachineState.Closed);
                r.ClosedMachines.Value.IsEmpty.Should().BeTrue();
                r.InactiveMachines.Value.IsEmpty.Should().BeTrue();

                // Transition m1 to DeadUnavailable
                r = await storage.HeartbeatAsync(context, new HeartbeatMachineRequest()
                {
                    MachineId = m1.Id,
                    Location = m1.Location,
                    Name = m1.Location.Path,
                    HeartbeatTime = clock.UtcNow,
                    DeclaredMachineState = MachineState.DeadUnavailable,
                }).ThrowIfFailureAsync();

                r.PriorState.Should().Be(MachineState.Open);
                r.ClosedMachines.Value.IsEmpty.Should().BeTrue();
                r.InactiveMachines.Value.Contains(m1.Id).Should().BeTrue();

                // Transition m1 to Closed
                r = await storage.HeartbeatAsync(context, new HeartbeatMachineRequest()
                {
                    MachineId = m1.Id,
                    Location = m1.Location,
                    Name = m1.Location.Path,
                    HeartbeatTime = clock.UtcNow,
                    DeclaredMachineState = MachineState.Closed,
                }).ThrowIfFailureAsync();

                r.PriorState.Should().Be(MachineState.DeadUnavailable);
                r.ClosedMachines.Value.Contains(m1.Id).Should().BeTrue();
                r.InactiveMachines.Value.IsEmpty.Should().BeTrue();

                // Transition m1 to Open
                r = await storage.HeartbeatAsync(context, new HeartbeatMachineRequest()
                {
                    MachineId = m1.Id,
                    Location = m1.Location,
                    Name = m1.Location.Path,
                    HeartbeatTime = clock.UtcNow,
                    DeclaredMachineState = MachineState.Open,
                }).ThrowIfFailureAsync();

                r.PriorState.Should().Be(MachineState.Closed);
                r.ClosedMachines.Value.IsEmpty.Should().BeTrue();
                r.InactiveMachines.Value.IsEmpty.Should().BeTrue();
            });
        }

        private async Task RunTest(
            Func<OperationContext, IClock, BlobClusterStateStorage, Task> runTest,
            IClock? clock = null,
            ClusterStateRecomputeConfiguration? recomputeConfiguration = null)
        {
            clock ??= SystemClock.Instance;

            var logger = TestGlobal.Logger;
            var context = new Context(logger);
            var operationContext = new OperationContext(context);

            using var azureStorage = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, TestGlobal.Logger);

            var fileName = ThreadSafeRandom.RandomAlphanumeric(20);
            var configuration = new BlobClusterStateStorageConfiguration()
            {
                Credentials = new AzureBlobStorageCredentials(connectionString: azureStorage.ConnectionString),
                // Use a random filename to ensure tests don't interact with eachother
                FileName = fileName,
            };
            if (recomputeConfiguration is not null)
            {
                configuration.RecomputeConfiguration = recomputeConfiguration;
            }
            var storage = new BlobClusterStateStorage(
                configuration,
                clock);

            await storage.StartupAsync(operationContext).ThrowIfFailureAsync();
            await runTest(operationContext, clock, storage);
            await storage.ShutdownAsync(operationContext).ThrowIfFailureAsync();
        }
    }
}
