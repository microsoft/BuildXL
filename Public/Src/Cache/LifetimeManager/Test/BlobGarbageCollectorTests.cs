// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.BlobLifetimeManager.Library;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.IO;

namespace BuildXL.Cache.BlobLifetimeManager.Test
{
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class BlobGarbageCollectorTests : LifetimeDatabaseTestBase
    {
        public BlobGarbageCollectorTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(redis, output) { }

        [Fact]
        public Task GcToZero()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));
            return RunTest(context,
                async (topology, session, namespaceId, _) =>
                {
                    var putResult = await session.PutRandomAsync(context, HashType.Vso0, provideHash: false, size: 1, CancellationToken.None).ThrowIfFailure();
                    var openResult = await session.OpenStreamAsync(context, putResult.ContentHash, CancellationToken.None).ThrowIfFailure();
                    openResult.Stream!.Dispose();

                    var putResult2 = await session.PutRandomAsync(context, HashType.Vso0, provideHash: false, size: 1, CancellationToken.None).ThrowIfFailure();
                    openResult = await session.OpenStreamAsync(context, putResult2.ContentHash, CancellationToken.None).ThrowIfFailure();
                    openResult.Stream!.Dispose();

                    var chl = new ContentHashListWithDeterminism(
                        new MemoizationStore.Interfaces.Sessions.ContentHashList(new[] { putResult.ContentHash, putResult2.ContentHash }), CacheDeterminism.None);
                    var fp = StrongFingerprint.Random();

                    var addChlResult = await session.AddOrGetContentHashListAsync(context, fp, chl, CancellationToken.None).ThrowIfFailure();
                    await session.GetContentHashListAsync(context, fp, CancellationToken.None).ThrowIfFailure();

                    var temp = Path.Combine(Path.GetTempPath(), "LifetimeDatabase", Guid.NewGuid().ToString());
                    var dbConfig = new RocksDbLifetimeDatabase.Configuration
                    {
                        DatabasePath = temp,
                        LruEnumerationPercentileStep = 0.05,
                        LruEnumerationBatchSize = 1000,
                        BlobNamespaceIds = new[] { namespaceId },
                    };
                    using var db = await LifetimeDatabaseCreator.CreateAsync(
                        context,
                        dbConfig,
                        SystemClock.Instance,
                        contentDegreeOfParallelism: 1,
                        fingerprintDegreeOfParallelism: 1,
                        n => topology).ThrowIfFailureAsync();

                    var manager = new BlobQuotaKeeper(db.GetAccessor(namespaceId), topology, lastAccessTimeDeletionThreshold: TimeSpan.Zero, SystemClock.Instance);

                    // It is much, much simpler to pass in null here, since this test really shouldn't use the checkpoint manager.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
                    await manager.EnsureUnderQuota(
                        context,
                        maxSize: 0,
                        dryRun: false,
                        contentDegreeOfParallelism: 1,
                        fingerprintDegreeOfParallelism: 1,
                        checkpointManager: null,
                        checkpointCreationInterval: TimeSpan.FromDays(1)).ThrowIfFailure();
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

                    (await session.OpenStreamAsync(context, putResult.ContentHash, CancellationToken.None)).Code.Should().Be(OpenStreamResult.ResultCode.ContentNotFound);
                    (await session.OpenStreamAsync(context, putResult2.ContentHash, CancellationToken.None)).Code.Should().Be(OpenStreamResult.ResultCode.ContentNotFound);

                    (await session.GetContentHashListAsync(context, fp, CancellationToken.None)).ThrowIfFailure().ContentHashListWithDeterminism.ContentHashList.Should().BeNull();
                });
        }

        [Fact]
        public Task GcToNonZero()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));
            return RunTest(context,
                async (topology, session, namespaceId, secretsProvider) =>
                {
                    // Because the LRU ordering isn't expected to be perfect because we're only calculating rough quantile estimates,
                    // we need to have a deterministic seed to our randomness to avoid test flakyness.
                    ThreadSafeRandom.SetSeed(1);

                    var totalFingerprints = 10;
                    var contentPerFingerprint = 2;
                    var contentSize = 8;

                    // Two unique pieces of content per fingerprint, plus one content that everyone shares.
                    var totalContent = totalFingerprints * contentPerFingerprint + 1;

                    var hashes = new List<ContentHash>();
                    for (var i = 0; i < totalContent; i++)
                    {
                        var result = await session.PutRandomAsync(
                            context,
                            HashType.Vso0,
                            provideHash: false,
                            size: contentSize,
                            CancellationToken.None)
                            .ThrowIfFailure();

                        hashes.Add(result.ContentHash);
                    }

                    var fingerprints = new List<StrongFingerprint>();
                    for (var i = 0; i < totalFingerprints; i++)
                    {
                        var selector = await session.PutRandomAsync(
                            context,
                            HashType.Vso0,
                            provideHash: false,
                            size: contentSize,
                            CancellationToken.None)
                            .ThrowIfFailure();

                        var sf = new StrongFingerprint(Fingerprint.Random(), new Selector(selector.ContentHash));

                        var chl = new ContentHashListWithDeterminism(
                            new MemoizationStore.Interfaces.Sessions.ContentHashList(
                                hashes.Skip(i * contentPerFingerprint).Take(contentPerFingerprint).Append(hashes[^1]).ToArray()),
                            CacheDeterminism.None);

                        await session.AddOrGetContentHashListAsync(context, sf, chl, CancellationToken.None).ThrowIfFailure();
                        fingerprints.Add(sf);
                    }

                    var temp = Path.Combine(Path.GetTempPath(), "LifetimeDatabase", Guid.NewGuid().ToString());
                    var dbConfig = new RocksDbLifetimeDatabase.Configuration
                    {
                        DatabasePath = temp,
                        LruEnumerationPercentileStep = 0.05,
                        LruEnumerationBatchSize = 1000,
                        BlobNamespaceIds = new[] { namespaceId },
                    };
                    using var db = await LifetimeDatabaseCreator.CreateAsync(
                        context,
                        dbConfig,
                        SystemClock.Instance,
                        contentDegreeOfParallelism: 1,
                        fingerprintDegreeOfParallelism: 1,
                        n => topology).ThrowIfFailureAsync();

                    var accessor = db.GetAccessor(namespaceId);

                    // This here is a bit of a hack. Since we don't have any control of the azure emulator's clock, we'll have to trick
                    // the manager into thinking that the last access times are what we want them to be. Otherwise, the emulator gives us identical
                    // last access times for all fingerprints, since they were put so close to one another.
                    var clock = new MemoryClock
                    {
                        UtcNow = DateTime.UtcNow
                    };

                    for (var i = 0; i < totalFingerprints; i++)
                    {
                        clock.Increment(TimeSpan.FromHours(1));
                        var entry = accessor.GetContentHashList(fingerprints[i], out var blobPath);
                        accessor.UpdateContentHashListLastAccessTime(new Library.ContentHashList(BlobName: blobPath!, LastAccessTime: clock.UtcNow, entry!.Hashes, entry.BlobSize));
                    }

                    // Since all CHLs have the same number of hashes, they should all have the same size
                    var metadataSize = accessor.GetContentHashList(fingerprints[0], out _)!.BlobSize;

                    var fingerprintsToKeep = 2;
                    var contentToKeep = fingerprintsToKeep * contentPerFingerprint + 1;
                    var totalContentToKeep = contentToKeep + fingerprintsToKeep; // This is to account for the selector of each FP.
                    var contentSizeToKeep = totalContentToKeep * contentSize;
                    var fingerprintSizeToKeep = fingerprintsToKeep * metadataSize;
                    var maxSize = contentSizeToKeep + fingerprintSizeToKeep;

                    var manager = new Library.BlobQuotaKeeper(accessor, topology, lastAccessTimeDeletionThreshold: TimeSpan.Zero, clock);

                    // It is much, much simpler to pass in null here, since this test really shouldn't use the checkpoint manager.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
                    await manager.EnsureUnderQuota(
                        context,
                        maxSize: maxSize,
                        dryRun: false,
                        contentDegreeOfParallelism: 1,
                        fingerprintDegreeOfParallelism: 1,
                        checkpointManager: null,
                        checkpointCreationInterval: TimeSpan.FromDays(1)).ThrowIfFailure();
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

                    for (var i = 0; i < (totalContent - contentToKeep); i++)
                    {
                        // These pieces of content should be gone.
                        (await session.OpenStreamAsync(context, hashes[i], CancellationToken.None)).Code.Should().Be(OpenStreamResult.ResultCode.ContentNotFound);
                    }

                    for (var i = totalContent - contentToKeep; i < totalContent; i++)
                    {
                        // These pieces of content should still exist.
                        var streamResult = await session.OpenStreamAsync(context, hashes[i], CancellationToken.None).ThrowIfFailure();
                        streamResult.Stream!.Dispose();
                    }

                    for (var i = 0; i < totalFingerprints - fingerprintsToKeep; i++)
                    {
                        // These fingerprints should be gone.
                        var r = await session.GetContentHashListAsync(context, fingerprints[i], CancellationToken.None).ThrowIfFailure();
                        r.ContentHashListWithDeterminism.ContentHashList.Should().BeNull();
                    }

                    for (var i = totalFingerprints - fingerprintsToKeep; i < totalFingerprints; i++)
                    {
                        // These fingerprints should be still exist.
                        var r = await session.GetContentHashListAsync(context, fingerprints[i], CancellationToken.None).ThrowIfFailure();
                        r.ContentHashListWithDeterminism.ContentHashList.Should().NotBeNull();
                    }
                });
        }
    }
}
