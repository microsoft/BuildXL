// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
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
    public class AzureBlobStorageCheckpointRegistryTests : TestWithOutput
    {
        private readonly static MachineLocation M1 = new MachineLocation("M1");
        private readonly LocalRedisFixture _fixture;

        public AzureBlobStorageCheckpointRegistryTests(LocalRedisFixture fixture, ITestOutputHelper output)
            : base(output)
        {
            _fixture = fixture;
        }

        [Fact]
        public Task RegisterAndGetCheckpoint()
        {
            return RunTest(async (context, registry, clock) =>
            {
                var now = clock.UtcNow;

                var cs1 = CreateCheckpointState(clock);
                await registry.RegisterCheckpointAsync(context, cs1).ThrowIfFailureAsync();
                var checkpointState = await registry.GetCheckpointStateAsync(context).ThrowIfFailureAsync();

                checkpointState.CheckpointId.Should().Be(cs1.CheckpointId);
                checkpointState.CheckpointTime.Should().Be(cs1.CheckpointTime);
                checkpointState.StartSequencePoint.Should().BeEquivalentTo(cs1.StartSequencePoint);
                checkpointState.Producer.Should().BeEquivalentTo(cs1.Producer);
            }, clock: new MemoryClock());
        }

        [Fact]
        public Task NewestCheckpointOverrides()
        {
            return RunTest(async (context, registry, clock) =>
            {
                var cs1 = CreateCheckpointState(clock);
                var cs2 = CreateCheckpointState(clock);
                await registry.RegisterCheckpointAsync(context, cs1).ThrowIfFailureAsync();
                await registry.RegisterCheckpointAsync(context, cs2).ThrowIfFailureAsync();
                var checkpointState = await registry.GetCheckpointStateAsync(context).ThrowIfFailureAsync();

                checkpointState.CheckpointId.Should().Be(cs2.CheckpointId);
            });
        }

        int _index = 0;

        public CheckpointState CreateCheckpointState(IClock clock)
        {
            var index = _index++;
            var checkpointId = "chkpt" + _index;
            var sequencePoint = new EventSequencePoint(sequenceNumber: index);

            return new CheckpointState(sequencePoint, checkpointId, clock.UtcNow, M1);
        }

        [Fact]
        public Task NoCheckpointReturnsInvalid()
        {
            return RunTest(async (context, registry, clock) =>
            {
                var checkpointState = await registry.GetCheckpointStateAsync(context).ThrowIfFailureAsync();
                checkpointState.CheckpointAvailable.Should().BeFalse();
            });
        }

        private async Task RunTest(Func<OperationContext, AzureBlobStorageCheckpointRegistry, IClock, Task> runTest, IClock? clock = null)
        {
            clock ??= SystemClock.Instance;

            var tracingContext = new Context(TestGlobal.Logger);
            var context = new OperationContext(tracingContext);

            using var storage = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, TestGlobal.Logger);

            var configuration = new AzureBlobStorageCheckpointRegistryConfiguration()
            {
                Credentials = new AzureBlobStorageCredentials(storage.ConnectionString),
                ContainerName = "checkpoints",
                FolderName = "checkpointRegistry",
            };
            var registry = new AzureBlobStorageCheckpointRegistry(configuration, M1, clock);

            await registry.StartupAsync(context).ThrowIfFailureAsync();
            await registry.GarbageCollectAsync(context, retentionLimit: 0).ThrowIfFailureAsync();
            await runTest(context, registry, clock);
            await registry.GarbageCollectAsync(context, retentionLimit: 0).ThrowIfFailureAsync();
            await registry.ShutdownAsync(context).ThrowIfFailureAsync();
        }
    }
}
