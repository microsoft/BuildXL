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
using System.IO;

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
                async (topology, session, namespaceId, secretsProvider) =>
                {
                    // Add content/fingerprints to the L3

                    var contentResults = new List<ContentHash>();
                    foreach (var _ in Enumerable.Range(0, 5))
                    {
                        var result = await session.PutRandomAsync(context, HashType.Vso0, provideHash: false, size: 16, CancellationToken.None)
                            .ThrowIfFailure();

                        contentResults.Add(result.ContentHash);
                    }

                    var sf1 = new StrongFingerprint(Fingerprint.Random(), new Selector(contentResults[3]));
                    var contentHashList1 = new ContentHashListWithDeterminism(
                        new MemoizationStore.Interfaces.Sessions.ContentHashList(
                            new ContentHash[] { contentResults[0], contentResults[1] }),
                        CacheDeterminism.SinglePhaseNonDeterministic);

                    var sf2 = new StrongFingerprint(Fingerprint.Random(), new Selector(contentResults[4]));
                    var contentHashList2 = new ContentHashListWithDeterminism(
                        new MemoizationStore.Interfaces.Sessions.ContentHashList(
                            new ContentHash[] { contentResults[1], contentResults[2] }),
                        CacheDeterminism.SinglePhaseNonDeterministic);

                    await session.AddOrGetContentHashListAsync(context, sf1, contentHashList1, CancellationToken.None).ThrowIfFailure();
                    await session.AddOrGetContentHashListAsync(context, sf2, contentHashList2, CancellationToken.None).ThrowIfFailure();

                    // Create the DB
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

                    var contentEntry = accessor.GetContentEntry(contentResults[0]);
                    contentEntry!.BlobSize.Should().Be(16);
                    contentEntry!.ReferenceCount.Should().Be(1);

                    contentEntry = accessor.GetContentEntry(contentResults[1]);
                    contentEntry!.BlobSize.Should().Be(16);
                    contentEntry!.ReferenceCount.Should().Be(2);

                    contentEntry = accessor.GetContentEntry(contentResults[2]);
                    contentEntry!.BlobSize.Should().Be(16);
                    contentEntry!.ReferenceCount.Should().Be(1);

                    contentEntry = accessor.GetContentEntry(contentResults[3]);
                    contentEntry!.BlobSize.Should().Be(16);
                    contentEntry!.ReferenceCount.Should().Be(1);

                    contentEntry = accessor.GetContentEntry(contentResults[3]);
                    contentEntry!.BlobSize.Should().Be(16);
                    contentEntry!.ReferenceCount.Should().Be(1);

                    var fpEntry = accessor.GetContentHashList(sf1, out _);
                    fpEntry!.Hashes.Should().ContainInOrder(contentHashList1.ContentHashList!.Hashes.Append(contentResults[3]));
                    fpEntry.LastAccessTime.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));

                    fpEntry = accessor.GetContentHashList(sf2, out _);
                    fpEntry!.Hashes.Should().BeEquivalentTo(contentHashList2.ContentHashList!.Hashes.Append(contentResults[4]));
                    fpEntry.LastAccessTime.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
                });
        }
    }
}
