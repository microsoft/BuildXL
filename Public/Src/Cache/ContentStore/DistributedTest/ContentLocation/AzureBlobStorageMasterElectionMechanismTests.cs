// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
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
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Grpc.Core;
using Xunit;
using Xunit.Abstractions;
using Channel = System.Threading.Channels.Channel;

#nullable enable annotations

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class AzureBlobStorageMasterElectionMechanismTests : TestWithOutput
    {
        private readonly static MachineLocation M1 = new MachineLocation("M1");
        private readonly static MachineLocation M2 = new MachineLocation("M2");

        private readonly MemoryClock _clock1;
        private MemoryClock _clock2;

        private TestRoleObserver _observer1;
        private TestRoleObserver _observer2;

        private readonly LocalRedisFixture _fixture;

        private readonly TimeSpan _backgroundElectionInterval = TimeSpan.FromMinutes(1);
        private readonly TimeSpan _leaseExpirationTime = TimeSpan.FromMinutes(5);

        public AzureBlobStorageMasterElectionMechanismTests(LocalRedisFixture fixture, ITestOutputHelper output)
            : base(output)
        {
            _fixture = fixture;

            _clock1 = new MemoryClock();
            _clock2 = _clock1;
        }

        private void SetupClocks(bool useSeparate = true, bool enableTimerQueue = true)
        {
            if (useSeparate)
            {
                _clock2 = new MemoryClock();
            }

            _clock1.TimerQueueEnabled = enableTimerQueue;
            _clock2.TimerQueueEnabled = enableTimerQueue;
        }

        [Fact]
        public Task TestStartupElection()
        {
            SetupClocks();

            return RunSingleMachineTest(async (context, client) =>
            {
                Check(client, role: Role.Master, master: M1);

                var r2 = await client.ReleaseRoleIfNecessaryAsync(context).ThrowIfFailureAsync();
                r2.Should().BeEquivalentTo(Role.Worker);

                var r3 = await client.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r3, role: Role.Master, master: M1);
            },
            mode: ElectionMode.BackgroundAndStartupGetRole);
        }

        [Fact]
        public Task TestBackgroundElection()
        {
            SetupClocks();

            return RunTwoMachinesTest(async (context, client1, client2) =>
            {
                Check(client1, role: Role.Worker, master: default);

                _clock1.Increment(_backgroundElectionInterval);
                await _observer1.WaitTillCurrentRoleAsync();
                Check(client1, role: Role.Master, master: M1);

                _clock2.Increment(_backgroundElectionInterval);
                await _observer2.WaitTillCurrentRoleAsync();
                Check(client1, role: Role.Master, master: M1);
                Check(client2, role: Role.Worker, master: M1);

                _clock2.Increment(_leaseExpirationTime);
                await _observer2.WaitTillCurrentRoleAsync();
                Check(client2, role: Role.Master, master: M2);

                _clock1.Increment(_backgroundElectionInterval);
                await _observer1.WaitTillCurrentRoleAsync();
                Check(client1, role: Role.Worker, master: M2);

                var role = await client2.ReleaseRoleIfNecessaryAsync(context).ShouldBeSuccess();
                role.Value.Should().Be(Role.Worker);
            },
            twoMasters: true,
            mode: ElectionMode.BackgroundGetRole);
        }

        [Fact]
        public Task SingleMasterFirstBootTest()
        {
            return RunSingleMachineTest(async (context, client) =>
            {
                var r1 = await client.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r1, role: Role.Master, master: M1);

                var r2 = await client.ReleaseRoleIfNecessaryAsync(context).ThrowIfFailureAsync();
                r2.Should().BeEquivalentTo(Role.Worker);

                var r3 = await client.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r3, role: Role.Master, master: M1);
            });
        }

        [Fact]
        public Task SingleWorkerFirstBootTest()
        {
            return RunSingleMachineTest(async (context, worker) =>
            {
                var r1 = await worker.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r1, role: Role.Worker, master: null);

                var r2 = await worker.ReleaseRoleIfNecessaryAsync(context).ThrowIfFailureAsync();
                r2.Should().BeEquivalentTo(Role.Worker);

                var r3 = await worker.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r3, role: Role.Worker, master: null);
            },
            worker: true);
        }

        [Fact]
        public Task WorkerAndMasterBasicInteractions()
        {
            return RunTwoMachinesTest(async (context, m, w) =>
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
                _clock1.Increment(TimeSpan.FromHours(1));

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

        [Fact]
        public Task TwoMasterEligibleMachinesBasicInteractions()
        {
            return RunTwoMachinesTest(async (context, m1, m2) =>
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
                _clock1.Increment(TimeSpan.FromMinutes(2));

                // M2 refreshes the lease
                var r6 = await m2.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r6, role: Role.Master, master: M2);

                // M1 still sees M2 as the master
                var r7 = await m1.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r7, role: Role.Worker, master: M2);

                // The original lease would have expired at this point
                _clock1.Increment(TimeSpan.FromMinutes(4));

                // M1 still sees M2 as the master (i.e., lease refresh worked)
                var r8 = await m1.GetRoleAsync(context).ThrowIfFailureAsync();
                Check(r8, role: Role.Worker, master: M2);

                // The refreshed lease expires
                _clock1.Increment(TimeSpan.FromMinutes(6));

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

        private static void Check(IMasterElectionMechanism state, Role role, MachineLocation? master)
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

        private enum ElectionMode
        {
            ExplicitOnly,

            BackgroundGetRole,

            BackgroundAndStartupGetRole
        }

        private IMasterElectionMechanism WrapIfNecessary(
            IClock clock,
            ElectionMode mode,
            IMasterElectionMechanism inner,
            ref TestRoleObserver observer)
        {
            if (mode == ElectionMode.ExplicitOnly)
            {
                return inner;
            }

            observer = new TestRoleObserver(clock);

            return new ObservableMasterElectionMechanism(
                new ObservableMasterElectionMechanismConfiguration()
                {
                    GetRoleInterval = _backgroundElectionInterval,
                    GetRoleOnStartup = mode == ElectionMode.BackgroundAndStartupGetRole
                },
                inner,
                clock,
                observer);
        }

        private class TestRoleObserver : StartupShutdownComponentBase, IRoleObserver
        {
            protected override Tracer Tracer { get; } = new Tracer(nameof(TestRoleObserver));

            private IClock Clock { get; }
            private Channel<(Role Role, DateTime UpdateTime)> RoleQueue { get; } = Channel.CreateUnbounded<(Role, DateTime)>();

            public Role? CurrentRole { get; private set; }
            public TestRoleObserver(IClock clock)
            {
                Clock = clock;
            }

            public Task OnRoleUpdatedAsync(OperationContext context, Role role)
            {
                return RoleQueue.Writer.WriteAsync((role, Clock.UtcNow)).AsTask();
            }

            public async Task WaitTillCurrentRoleAsync()
            {
                var now = Clock.UtcNow;
                var reader = RoleQueue.Reader;
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(5000);
                while (await reader.WaitToReadAsync(cts.Token))
                {
                    bool isCurrent = false;
                    if (reader.TryRead(out var roleEntry))
                    {
                        CurrentRole = roleEntry.Role;
                        if (roleEntry.UpdateTime == now)
                        {
                            isCurrent = true;
                        }
                    }

                    if (isCurrent)
                    {
                        return;
                    }
                }
            }
        }

        private async Task RunTwoMachinesTest(
            Func<OperationContext, IMasterElectionMechanism, IMasterElectionMechanism, Task> runTest,
            IClock? clock = null,
            bool twoMasters = false,
            bool allowRetries = false,
            ElectionMode mode = ElectionMode.ExplicitOnly)
        {
            var logger = TestGlobal.Logger;
            var context = new Context(logger);
            var operationContext = new OperationContext(context);

            using var storage = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, TestGlobal.Logger);

            var fileName = ThreadSafeRandom.RandomAlphanumeric(20);

            var m1 = WrapIfNecessary(_clock1, mode, new AzureBlobStorageMasterElectionMechanism(
                new AzureBlobStorageMasterElectionMechanismConfiguration()
                {
                    Credentials = new AzureBlobStorageCredentials(connectionString: storage.ConnectionString),
                    IsMasterEligible = true,
                    // Use a random filename to ensure tests don't interact with eachother
                    FileName = fileName,
                    LeaseExpiryTime = _leaseExpirationTime,
                },
                M1,
                _clock1),
                ref _observer1);

            var m2 = WrapIfNecessary(_clock2, mode, new AzureBlobStorageMasterElectionMechanism(
                new AzureBlobStorageMasterElectionMechanismConfiguration()
                {
                    Credentials = new AzureBlobStorageCredentials(connectionString: storage.ConnectionString),
                    IsMasterEligible = twoMasters,
                    // Use a random filename to ensure tests don't interact with eachother
                    FileName = fileName,
                    LeaseExpiryTime = _leaseExpirationTime,
                },
                M2,
                _clock2),
                ref _observer2);

            await m1.StartupAsync(operationContext).ThrowIfFailureAsync();
            await m2.StartupAsync(operationContext).ThrowIfFailureAsync();

            await runTest(operationContext, m1, m2);

            await m2.ShutdownAsync(operationContext).ThrowIfFailureAsync();
            await m1.ShutdownAsync(operationContext).ThrowIfFailureAsync();
        }

        private async Task RunSingleMachineTest(
            Func<OperationContext, IMasterElectionMechanism, Task> runTest,
            bool worker = false,
            ElectionMode mode = ElectionMode.ExplicitOnly)
        {
            var logger = TestGlobal.Logger;
            var context = new Context(logger);
            var operationContext = new OperationContext(context);

            using var storage = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, logger);

            var fileName = ThreadSafeRandom.RandomAlphanumeric(20);

            var configuration = new AzureBlobStorageMasterElectionMechanismConfiguration()
            {
                Credentials = new AzureBlobStorageCredentials(connectionString: storage.ConnectionString),
                IsMasterEligible = !worker,
                // Use a random filename to ensure tests don't interact with eachother
                FileName = fileName,
                LeaseExpiryTime = _leaseExpirationTime,
            };
            var machine = WrapIfNecessary(_clock1, mode, new AzureBlobStorageMasterElectionMechanism(
                configuration,
                M1,
                _clock1),
                ref _observer1);
            await machine.StartupAsync(operationContext).ThrowIfFailureAsync();
            await runTest(operationContext, machine);
            await machine.ShutdownAsync(operationContext).ThrowIfFailureAsync();
        }
    }
}
