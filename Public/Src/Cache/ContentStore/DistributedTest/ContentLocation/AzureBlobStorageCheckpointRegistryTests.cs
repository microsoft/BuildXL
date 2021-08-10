// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    public class AzureBlobStorageCheckpointRegistryTests : TestWithOutput
    {
        private readonly static MachineLocation M1 = new MachineLocation("M1");

        public AzureBlobStorageCheckpointRegistryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact(Skip = "This test can only be run with Azure Storage Emulator")]
        public Task RegisterAndGetCheckpoint()
        {
            return RunTest(async (context, registry, clock) =>
            {
                var now = clock.UtcNow;

                var checkpointId = "chkpt1";
                var sequencePoint = new EventSequencePoint(sequenceNumber: 1);

                await registry.RegisterCheckpointAsync(context, checkpointId, sequencePoint).ThrowIfFailureAsync();
                var checkpointState = await registry.GetCheckpointStateAsync(context).ThrowIfFailureAsync();

                checkpointState.CheckpointId.Should().BeEquivalentTo(checkpointId);
                checkpointState.StartSequencePoint.Should().BeEquivalentTo(sequencePoint);
                // Time doesn't pass when using a memory clock
                checkpointState.CheckpointTime.Should().Be(now);
                checkpointState.Producer.Should().BeEquivalentTo(M1);
            }, clock: new MemoryClock());
        }

        [Fact(Skip = "This test can only be run with Azure Storage Emulator")]
        public Task NewestCheckpointOverrides()
        {
            return RunTest(async (context, registry, clock) =>
            {
                var now = clock.UtcNow;

                var checkpointId = "chkpt1";
                var sequencePoint = new EventSequencePoint(sequenceNumber: 1);

                var checkpointId2 = "chkpt2";
                var sequencePoint2 = new EventSequencePoint(sequenceNumber: 2);

                await registry.RegisterCheckpointAsync(context, checkpointId, sequencePoint).ThrowIfFailureAsync();
                await registry.RegisterCheckpointAsync(context, checkpointId2, sequencePoint2).ThrowIfFailureAsync();
                var checkpointState = await registry.GetCheckpointStateAsync(context).ThrowIfFailureAsync();

                checkpointState.CheckpointId.Should().BeEquivalentTo(checkpointId2);
                checkpointState.StartSequencePoint.Should().BeEquivalentTo(sequencePoint2);
                checkpointState.CheckpointTime.Should().BeAfter(now);
                checkpointState.Producer.Should().BeEquivalentTo(M1);
            });
        }

        [Fact(Skip = "This test can only be run with Azure Storage Emulator")]
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

            var configuration = new AzureBlobStorageCheckpointRegistryConfiguration()
            {
                Credentials = new AzureBlobStorageCredentials("UseDevelopmentStorage=true"),
                ContainerName = "checkpoints",
                FolderName = "checkpointRegistry",
            };
            var registry = new AzureBlobStorageCheckpointRegistry(configuration, M1, clock);

            await registry.StartupAsync(context).ThrowIfFailureAsync();
            await registry.GarbageCollectAsync(context, limit: 0).ThrowIfFailureAsync();
            await runTest(context, registry, clock);
            await registry.GarbageCollectAsync(context, limit: 0).ThrowIfFailureAsync();
            await registry.ShutdownAsync(context).ThrowIfFailureAsync();
        }
    }
}
