// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    public class BlobClusterStateStorageTests : TestWithOutput
    {
        private readonly static MachineLocation M1 = new MachineLocation("M1");
        private readonly static MachineLocation M2 = new MachineLocation("M2");

        private const string SkipReason = "This test can only be run with Azure Storage Emulator";

        public BlobClusterStateStorageTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact(Skip = SkipReason)]
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

        [Fact(Skip = SkipReason)]
        public Task SimpleHeartbeatTest()
        {
            return RunTest(async (context, clock, storage) =>
            {
                var m1 = await storage.RegisterMachineAsync(context, MachineLocation.Create("A", 1)).ThrowIfFailureAsync();
                var m2 = await storage.RegisterMachineAsync(context, MachineLocation.Create("B", 1)).ThrowIfFailureAsync();

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
            });
        }

        private static async Task RunTest(
            Func<OperationContext, IClock, BlobClusterStateStorage, Task> runTest,
            IClock? clock = null,
            ClusterStateRecomputeConfiguration? recomputeConfiguration = null)
        {
            clock ??= SystemClock.Instance;

            var logger = TestGlobal.Logger;
            var context = new Context(logger);
            var operationContext = new OperationContext(context);

            var fileName = ThreadSafeRandom.RandomAlphanumeric(20);
            var configuration = new BlobClusterStateStorageConfiguration()
            {
                Credentials = AzureBlobStorageCredentials.StorageEmulator,
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
