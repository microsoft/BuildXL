// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
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
    public class AzureBlobStorageMasterElectionMechanismTests : TestWithOutput
    {
        private readonly static MachineLocation M1 = new MachineLocation("M1");
        private readonly static MachineLocation M2 = new MachineLocation("M2");

        private const string SkipReason = "This test can only be run with Azure Storage Emulator";

        public AzureBlobStorageMasterElectionMechanismTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact(Skip = SkipReason)]
        public Task SingleMasterFirstBootTest()
        {
            return RunSingleMachineTest(async (context, clock, client) =>
            {
                var r1 = await client.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r1, role: Role.Master, master: M1);

                var r2 = await client.ReleaseRoleIfNecessaryAsync(context).ThrowIfFailureAsync();
                r2.Should().BeEquivalentTo(Role.Worker);

                var r3 = await client.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r3, role: Role.Master, master: M1);
            },
            clock: new MemoryClock());
        }

        [Fact(Skip = SkipReason)]
        public Task SingleWorkerFirstBootTest()
        {
            return RunSingleMachineTest(async (context, clock, worker) =>
            {
                var r1 = await worker.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r1, role: Role.Worker, master: null);

                var r2 = await worker.ReleaseRoleIfNecessaryAsync(context).ThrowIfFailureAsync();
                r2.Should().BeEquivalentTo(Role.Worker);

                var r3 = await worker.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r3, role: Role.Worker, master: null);
            },
            clock: new MemoryClock(),
            worker: true);
        }

        [Fact(Skip = SkipReason)]
        public Task WorkerAndMasterBasicInteractions()
        {
            return RunTwoMachinesTest(async (context, clock, m, w) =>
            {
                // Worker shouldn't see the master
                var r1 = await w.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r1, role: Role.Worker, master: null);

                var r2 = await w.ReleaseRoleIfNecessaryAsync(context).ThrowIfFailureAsync();
                r2.Should().BeEquivalentTo(Role.Worker);

                var r3 = await w.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r3, role: Role.Worker, master: null);

                // Master enters the play here
                var r4 = await m.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r4, role: Role.Master, master: M1);

                // Worker should see the master at this point
                var r5 = await w.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r5, role: Role.Worker, master: M1);

                // Master releases the role
                var r6 = await m.ReleaseRoleIfNecessaryAsync(context).ThrowIfFailureAsync();
                r6.Should().BeEquivalentTo(Role.Worker);

                // Worker can no longer see the master
                var r7 = await w.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r7, role: Role.Worker, master: null);

                // Master re-gains the role here
                var r8 = await m.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r8, role: Role.Master, master: M1);

                // Worker should see the master at this point
                var r9 = await w.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r9, role: Role.Worker, master: M1);

                // Master lets the lease expire
                (clock as MemoryClock)!.Increment(TimeSpan.FromHours(1));

                // Worker can no longer see the master
                var r10 = await w.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r10, role: Role.Worker, master: null);

                // Master re-gains the role here
                var r11 = await m.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r11, role: Role.Master, master: M1);

                // Worker can see the master again
                var r12 = await w.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r12, role: Role.Worker, master: M1);
            },
            clock: new MemoryClock());
        }

        [Fact(Skip = SkipReason)]
        public Task TwoMasterEligibleMachinesBasicInteractions()
        {
            return RunTwoMachinesTest(async (context, clock, m1, m2) =>
            {
                // M1 gets master
                var r1 = await m1.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r1, role: Role.Master, master: M1);

                // M2 sees it and declares itself as worker
                var r2 = await m2.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r2, role: Role.Worker, master: M1);

                // M1 releases the role
                var r3 = await m1.ReleaseRoleIfNecessaryAsync(context).ThrowIfFailureAsync();
                r3.Should().BeEquivalentTo(Role.Worker);

                // M2 picks up master
                var r4 = await m2.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r4, role: Role.Master, master: M2);

                // M1 sees that M2 is master
                var r5 = await m1.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r5, role: Role.Worker, master: M2);

                // Some time passes, but less than the lease expiry time
                (clock as MemoryClock)!.Increment(TimeSpan.FromMinutes(2));

                // M2 refreshes the lease
                var r6 = await m2.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r6, role: Role.Master, master: M2);

                // M1 still sees M2 as the master
                var r7 = await m1.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r7, role: Role.Worker, master: M2);

                // The original lease would have expired at this point
                (clock as MemoryClock)!.Increment(TimeSpan.FromMinutes(4));

                // M1 still sees M2 as the master (i.e., lease refresh worked)
                var r8 = await m1.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r8, role: Role.Worker, master: M2);

                // The refreshed lease expires
                (clock as MemoryClock)!.Increment(TimeSpan.FromMinutes(6));

                // M1 picks up master role
                var r9 = await m1.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r9, role: Role.Master, master: M1);

                // M2 sees M1 is master
                var r10 = await m2.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r10, role: Role.Worker, master: M1);
            },
            clock: new MemoryClock(),
            twoMasters: true);
        }

        private static void Check(MasterElectionState state, Role role, MachineLocation? master)
        {
            if (master is null)
            {
                state.Master.IsValid.Should().BeFalse($"Expected master to be invalid but found {state.Master}");
            }
            else
            {
                state.Master.Should().BeEquivalentTo(master.Value);
            }
            
            state.Role.Should().Be(role);
        }

        private async Task RunTwoMachinesTest(
            Func<OperationContext, IClock, AzureBlobStorageMasterElectionMechanism, AzureBlobStorageMasterElectionMechanism, Task> runTest,
            IClock? clock = null,
            bool twoMasters = false,
            bool allowRetries = false)
        {
            clock ??= SystemClock.Instance;

            var logger = TestGlobal.Logger;
            var context = new Context(logger);
            var operationContext = new OperationContext(context);

            var fileName = ThreadSafeRandom.RandomAlphanumeric(20);

            var m1 = new AzureBlobStorageMasterElectionMechanism(
                new AzureBlobStorageMasterElectionMechanismConfiguration()
                {
                    Credentials = AzureBlobStorageCredentials.StorageEmulator,
                    IsMasterEligible = true,
                    // Use a random filename to ensure tests don't interact with eachother
                    FileName = fileName,
                },
                M1,
                clock);

            var m2 = new AzureBlobStorageMasterElectionMechanism(
                new AzureBlobStorageMasterElectionMechanismConfiguration()
                {
                    Credentials = AzureBlobStorageCredentials.StorageEmulator,
                    IsMasterEligible = twoMasters,
                    // Use a random filename to ensure tests don't interact with eachother
                    FileName = fileName,
                },
                M2,
                clock);

            await m1.StartupAsync(operationContext).ThrowIfFailureAsync();
            await m2.StartupAsync(operationContext).ThrowIfFailureAsync();
            await m1.CleanupStateAsync(operationContext).ThrowIfFailureAsync();

            await runTest(operationContext, clock, m1, m2);

            await m1.CleanupStateAsync(operationContext).ThrowIfFailureAsync();
            await m2.ShutdownAsync(operationContext).ThrowIfFailureAsync();
            await m1.ShutdownAsync(operationContext).ThrowIfFailureAsync();
        }

        private static async Task RunSingleMachineTest(
            Func<OperationContext, IClock, AzureBlobStorageMasterElectionMechanism, Task> runTest,
            IClock? clock = null,
            bool worker = false)
        {
            clock ??= SystemClock.Instance;

            var logger = TestGlobal.Logger;
            var context = new Context(logger);
            var operationContext = new OperationContext(context);

            var fileName = ThreadSafeRandom.RandomAlphanumeric(20);

            var configuration = new AzureBlobStorageMasterElectionMechanismConfiguration()
            {
                Credentials = AzureBlobStorageCredentials.StorageEmulator,
                IsMasterEligible = !worker,
                // Use a random filename to ensure tests don't interact with eachother
                FileName = fileName,
            };
            var machine = new AzureBlobStorageMasterElectionMechanism(
                configuration,
                M1,
                clock);
            await machine.StartupAsync(operationContext).ThrowIfFailureAsync();
            await machine.CleanupStateAsync(operationContext).ThrowIfFailureAsync();
            await runTest(operationContext, clock, machine);
            await machine.CleanupStateAsync(operationContext).ThrowIfFailureAsync();
            await machine.ShutdownAsync(operationContext).ThrowIfFailureAsync();
        }
    }
}
