// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Ephemeral;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Distributed.Redis;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.Ephemeral;

[TestClassIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
[Collection("Redis-based tests")]
[Trait("DisableFailFast", "true")]
public class DatacenterWideEphemeralCacheTests : EphemeralCacheTestsBase
{
    protected override Mode TestMode => Mode.DatacenterWide;

    public DatacenterWideEphemeralCacheTests(LocalRedisFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    [Fact]
    public Task LeaderDoesntMakeWorkerAwareOfChanges()
    {
        return RunTestAsync(
            async (context, silentContext, host) =>
            {
                var ring = host.Ring(0);
                var leader = host.Instance(ring.Leader);

                var content = new ContentHashWithSize(ContentHash.Random(), 100);

                await leader.LocalChangeProcessor.ProcessLocalChangeAsync(context, ChangeStampOperation.Add, content);
                leader.ContentTracker.GetSequenceNumber(content.Hash, leader.Id).Should().Be(
                    new SequenceNumber(1),
                    "Sequence number must increment when doing a mutation");

                int unseen = 0;
                foreach (var instance in host.Instances)
                {
                    var seen = instance.ContentTracker.GetSequenceNumber(content.Hash, leader.Id) == new SequenceNumber(1);
                    if (!seen)
                    {
                        unseen++;
                    }
                }

                unseen.Should().BeGreaterOrEqualTo(4, "The update should have been gossipped to 6 machines in total.");
            },
            // WARNING: because of the distributed hash table, we need to use enough workers that at least one of
            // them can be guaranteed not to know.
            instancesPerRing: 10);
    }

    [Fact]
    public Task WorkerMakesLeaderAwareOfChanges()
    {
        return RunTestAsync(
            async (context, silentContext, host) =>
            {
                var ring = host.Ring(0);
                var leader = host.Instance(ring.Leader);
                var worker = host.Instance(ring.Builders[1]);

                var content = new ContentHashWithSize(ContentHash.Random(), 100);

                await worker.LocalChangeProcessor.ProcessLocalChangeAsync(context, ChangeStampOperation.Add, content);
                worker.ContentTracker.GetSequenceNumber(content.Hash, worker.Id).Should().Be(new SequenceNumber(1));
                leader.ContentTracker.GetSequenceNumber(content.Hash, worker.Id).Should().Be(new SequenceNumber(1));

                var entry = await leader.ContentTracker.GetSingleLocationAsync(context, content.Hash).ThrowIfFailureAsync();
                entry.Size.Should().Be(content.Size);
                entry.Contains(worker.Id).Should().BeTrue("The worker added the content and should have notified the leader");

                await worker.LocalChangeProcessor.ProcessLocalChangeAsync(context, ChangeStampOperation.Delete, content);
                worker.ContentTracker.GetSequenceNumber(content.Hash, worker.Id).Should().Be(new SequenceNumber(2));
                leader.ContentTracker.GetSequenceNumber(content.Hash, worker.Id).Should().Be(new SequenceNumber(2));

                entry = await leader.ContentTracker.GetSingleLocationAsync(context, content.Hash).ThrowIfFailureAsync();
                entry.Size.Should().Be(content.Size);
                entry.Tombstone(worker.Id).Should().BeTrue("The worker deleted the content and should have notified the leader");
            });
    }

    [Fact]
    public Task DistributedHashTableIsMadeAwareOfChanges()
    {
        return RunTestAsync(
            async (context, silentContext, host) =>
            {
                var r1 = host.Ring(0);
                var r1l = host.Instance(r1.Leader);
                var r1w = host.Instance(r1.Builders[1]);

                var r2 = host.Ring(1);
                var r2l = host.Instance(r2.Leader);
                var r2w = host.Instance(r2.Builders[1]);

                var content = new ContentHashWithSize(ContentHash.Random(), 100);

                await r1w.LocalChangeProcessor.ProcessLocalChangeAsync(context, ChangeStampOperation.Add, content);

                var entry = await r2w.ContentResolver.GetSingleLocationAsync(context, content.Hash).ThrowIfFailureAsync();
                entry.Size.Should().Be(content.Size);
                entry.Contains(r1w.Id).Should()
                    .BeTrue($"{nameof(r1w)} added a piece of content, so {nameof(r2w)} should be able to look it up via the DHT");
            },
            numRings: 2);
    }

    [Fact]
    public Task UpdatesArePropagatedFromLocalContentStore()
    {
        return RunTestAsync(
            async (context, silentContext, host) =>
            {
                var r1 = host.Ring(0);
                var r1l = host.Instance(r1.Leader);
                var r1w = host.Instance(r1.Builders[1]);

                var r2 = host.Ring(1);
                var r2l = host.Instance(r2.Leader);
                var r2w = host.Instance(r2.Builders[1]);

                var putResult = await r1w.Session!.PutRandomAsync(context, HashType.Vso0, provideHash: true, size: 100, context.Token)
                    .ThrowIfFailureAsync();

                var r1le = await r1l.ContentResolver.GetSingleLocationAsync(context, putResult.ContentHash).ThrowIfFailureAsync();
                r1le.Contains(r1w.Id).Should()
                    .BeTrue($"{nameof(r1w)} added a piece of content, so {nameof(r1l)} should be able to look it up via the DHT");
                r1le.Size.Should().Be(putResult.ContentSize);

                var r2le = await r2l.ContentResolver.GetSingleLocationAsync(context, putResult.ContentHash).ThrowIfFailureAsync();
                r2le.Contains(r1w.Id).Should()
                    .BeTrue($"{nameof(r1w)} added a piece of content, so {nameof(r2l)} should be able to look it up via the DHT");
                r2le.Size.Should().Be(putResult.ContentSize);

                var r2we = await r2w.ContentResolver.GetSingleLocationAsync(context, putResult.ContentHash).ThrowIfFailureAsync();
                r2we.Contains(r1w.Id).Should()
                    .BeTrue($"{nameof(r1w)} added a piece of content, so {nameof(r2w)} should be able to look it up via the DHT");
                r2we.Size.Should().Be(putResult.ContentSize);

                var evictResult = await r1w.Cache.DeleteAsync(context, putResult.ContentHash, null).ThrowIfFailureAsync();

                r2we = await r2w.ContentResolver.GetSingleLocationAsync(context, putResult.ContentHash).ThrowIfFailureAsync();
                r2we.Tombstone(r1w.Id).Should()
                    .BeTrue($"{nameof(r1w)} removed a piece of content, so {nameof(r2w)} should be able to see the tombstone");
                r2we.Size.Should().Be(putResult.ContentSize);
            },
            numRings: 2);
    }

    [Fact]
    public Task TestFileCopyAcrossMachinesAsync()
    {
        return RunTestAsync(
            async (context, silentContext, host) =>
            {
                //Debugger.Launch();
                var r1 = host.Ring(0);
                var r1l = host.Instance(r1.Leader);
                var r1w = host.Instance(r1.Builders[1]);

                var r2 = host.Ring(1);
                var r2l = host.Instance(r2.Leader);
                var r2w = host.Instance(r2.Builders[1]);

                var putResult = await r1w.Session!.PutRandomAsync(context, HashType.Vso0, provideHash: true, size: 100, context.Token)
                    .ThrowIfFailureAsync();

                // In order for this to succeed, it needs to copy a file from r1w
                var placeResult = await r2w.Session!.PlaceFileAsync(
                    context,
                    putResult.ContentHash,
                    host.TestDirectory.CreateRandomFileName(),
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    context.Token);
                placeResult.ShouldBeSuccess();
                placeResult.MaterializationSource.Should().Be(PlaceFileResult.Source.DatacenterCache);

                await host.RemoveRingAsync(silentContext, r1.Id).ThrowIfFailureAsync();

                // In order for this to succeed, it needs to copy a file from r2w
                placeResult = await r2l.Session!.PlaceFileAsync(
                    context,
                    putResult.ContentHash,
                    host.TestDirectory.CreateRandomFileName(),
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    context.Token);
                placeResult.ShouldBeSuccess();
                placeResult.MaterializationSource.Should().Be(PlaceFileResult.Source.DatacenterCache);
            },
            numRings: 2);
    }

    [Fact]
    public Task TestFileExistsAfterRingShutsDownAsync()
    {
        return RunTestAsync(
            async (context, silentContext, host) =>
            {
                var r1 = host.Ring(0);
                var r1l = host.Instance(r1.Leader);
                var r1w = host.Instance(r1.Builders[1]);

                var r2 = host.Ring(1);
                var r2l = host.Instance(r2.Leader);
                var r2w = host.Instance(r2.Builders[1]);

                var putResult = await r1w.Session!.PutRandomAsync(context, HashType.Vso0, provideHash: true, size: 100, context.Token)
                    .ThrowIfFailureAsync();

                await host.RemoveRingAsync(silentContext, r1.Id).ThrowIfFailureAsync();

                // In order for this to succeed, it needs to download a file from the persistent cache 
                var placeResult = await r2w.Session!.PlaceFileAsync(
                    context,
                    putResult.ContentHash,
                    host.TestDirectory.CreateRandomFileName(),
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    context.Token);
                placeResult.ShouldBeSuccess();
                placeResult.MaterializationSource.Should().Be(PlaceFileResult.Source.BackingStore);
            },
            numRings: 2);
    }

    [Fact]
    public Task AddedMachinesCanFindOldFilesTestAsync()
    {
        return RunTestAsync(
            async (context, silentContext, host) =>
            {
                var r1 = host.Ring(0);
                var r1l = host.Instance(r1.Leader);
                var r1w = host.Instance(r1.Builders[1]);

                var r2 = host.Ring(1);
                var r2l = host.Instance(r2.Leader);
                var r2w = host.Instance(r2.Builders[1]);

                var putResult = await r1w.Session!.PutRandomAsync(context, HashType.Vso0, provideHash: true, size: 100, context.Token)
                    .ThrowIfFailureAsync();

                var r3 = await host.AddRingAsync(silentContext, "1234", 2).ThrowIfFailureAsync();
                var r3l = host.Instance(r3.Leader);
                var r3w = host.Instance(r3.Builders[1]);

                await host.HearbeatAsync(silentContext).ThrowIfFailure();

                var r3we = await r3w.ContentResolver.GetSingleLocationAsync(context, putResult.ContentHash).ThrowIfFailureAsync();
                r3we.Contains(r1w.Id).Should()
                    .BeTrue($"{nameof(r1w)} added a piece of content, so {nameof(r3w)} should be able to look it up via the DHT");
                r3we.Size.Should().Be(putResult.ContentSize);

                // In order for this to succeed, it needs to download a file from the machines that had it before it
                // joined
                var placeResult = await r3w.Session!.PlaceFileAsync(
                    context,
                    putResult.ContentHash,
                    host.TestDirectory.CreateRandomFileName(),
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    context.Token);
                placeResult.ShouldBeSuccess();
                placeResult.MaterializationSource.Should().Be(PlaceFileResult.Source.DatacenterCache);
            },
            numRings: 2);
    }
}
