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
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.BlobLifetimeManager.Library;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.BlobLifetimeManager.Test
{
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class LifetimeDatabaseCreatorTests : LifetimeDatabaseTestBase
    {
        public LifetimeDatabaseCreatorTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(redis, output) { }

        [Fact]
        public Task CreateDb()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));
            return RunTest(context,
                async (topology, session) =>
                {
                    // Add content/fingerprints to the L3

                    var contentResults = new List<ContentHash>();
                    foreach (var _ in Enumerable.Range(0, 3))
                    {
                        var result = await session.PutRandomAsync(context, HashType.Vso0, provideHash: false, size: 16, CancellationToken.None)
                            .ThrowIfFailure();

                        contentResults.Add(result.ContentHash);
                    }

                    var sf1 = StrongFingerprint.Random();
                    var contentHashList1 = new ContentHashListWithDeterminism(
                        new MemoizationStore.Interfaces.Sessions.ContentHashList(
                            new ContentHash[] { contentResults[0], contentResults[1] }),
                        CacheDeterminism.SinglePhaseNonDeterministic);

                    var sf2 = StrongFingerprint.Random();
                    var contentHashList2 = new ContentHashListWithDeterminism(
                        new MemoizationStore.Interfaces.Sessions.ContentHashList(
                            new ContentHash[] { contentResults[1], contentResults[2] }),
                        CacheDeterminism.SinglePhaseNonDeterministic);

                    await session.AddOrGetContentHashListAsync(context, sf1, contentHashList1, CancellationToken.None).ThrowIfFailure();
                    await session.AddOrGetContentHashListAsync(context, sf2, contentHashList2, CancellationToken.None).ThrowIfFailure();

                    // Create the DB
                    var creator = new LifetimeDatabaseCreator(SystemClock.Instance, topology);
                    using var db = await creator.CreateAsync(context, contentDegreeOfParallelism: 1, fingerprintDegreeOfParallelism: 1);

                    var contentEntry = db.GetContentEntry(contentResults[0]);
                    contentEntry!.BlobSize.Should().Be(16);
                    contentEntry!.ReferenceCount.Should().Be(1);

                    contentEntry = db.GetContentEntry(contentResults[1]);
                    contentEntry!.BlobSize.Should().Be(16);
                    contentEntry!.ReferenceCount.Should().Be(2);

                    contentEntry = db.GetContentEntry(contentResults[2]);
                    contentEntry!.BlobSize.Should().Be(16);
                    contentEntry!.ReferenceCount.Should().Be(1);

                    var fpEntry = db.TryGetContentHashList(sf1, out _);
                    fpEntry!.Hashes.Should().ContainInOrder(contentHashList1.ContentHashList!.Hashes);
                    fpEntry.LastAccessTime.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));

                    fpEntry = db.TryGetContentHashList(sf2, out _);
                    fpEntry!.Hashes.Should().BeEquivalentTo(contentHashList2.ContentHashList!.Hashes);
                    fpEntry.LastAccessTime.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
                });
        }
    }
}
