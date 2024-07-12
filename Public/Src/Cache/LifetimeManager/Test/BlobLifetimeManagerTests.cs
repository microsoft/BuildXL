// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs.ChangeFeed;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.BlobLifetimeManager.Library;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.BlobLifetimeManager.Test
{
    public class BlobLifetimeManagerTests : LifetimeDatabaseTestBase
    {
        public BlobLifetimeManagerTests(LocalRedisFixture redis, ITestOutputHelper output) : base(redis, output)
        {
        }

        [Fact]
        public Task BlobLifetimeManagerTest()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));
            return RunTest(context,
                async (topology, session, namespaceId, secretsProvider) =>
                {
                    var cycles = 10;
                    var fpsPerCycle = 3;
                    var contentPerFp = 2;
                    var contentSize = 10;
                    const int FpSize = 44; // This is the size of a fingerprint without counting the CHL.
                    const int HashSizeInFp = 34;
                    var fpBlobSize = FpSize + contentPerFp * HashSizeInFp;
                    var totalSizePerFp = fpBlobSize + contentPerFp * contentSize;

                    var gcLimit = totalSizePerFp * fpsPerCycle * 2;
                    cycles.Should().BeGreaterThan(2, "We want to actually garbage collect content");

                    var config = new BlobQuotaKeeperConfig
                    {
                        LastAccessTimeDeletionThreshold = TimeSpan.Zero,
                        Namespaces = new List<GarbageCollectionNamespaceConfig>
                        {
                            // 300 bytes (1 CHL + 2 pieces of content = 132 bytes)
                            new GarbageCollectionNamespaceConfig(namespaceId.Universe, namespaceId.Namespace, (1.0 / 1024 / 1024 / 1024) * gcLimit)
                        }
                    };

                    var accounts = new List<BlobCacheStorageAccountName>();
                    await foreach (var client in topology.EnumerateClientsAsync(context, BlobCacheContainerPurpose.Metadata))
                    {
                        accounts.Add(BlobCacheStorageAccountName.Parse(client.AccountName));
                    }

                    var pages = new List<Page<IBlobChangeFeedEvent>>();

                    for (var i = 0; i < cycles; i++)
                    {
                        foreach (var _ in Enumerable.Range(0, fpsPerCycle))
                        {
                            await PutRandomContentHashListAsync(context, session, contentCount: 3, contentSize: 10, topology, pages, SystemClock.Instance);
                        }

                        await new TestBlobLifetimeManager(pages).RunAsync(
                            context,
                            config,
                            PassThroughFileSystem.Default,
                            secretsProvider,
                            accounts,
                            SystemClock.Instance,
                            runId: $"run{i}",
                            contentDegreeOfParallelism: 1,
                            fingerprintDegreeOfParallelism: 1,
                            cacheInstance: "testCache",
                            buildCacheConfiguration: null,
                            dryRun: false);
                    }
                });
        }

        private static async Task PutRandomContentHashListAsync(
            OperationContext context,
            ICacheSession session,
            int contentCount,
            int contentSize,
            IBlobCacheTopology topology,
            List<Page<IBlobChangeFeedEvent>> pages,
            IClock clock)
        {
            var changes = new List<IBlobChangeFeedEvent>();
            var hashes = new ContentHash[contentCount];
            for (var i = 0; i < contentCount; i++)
            {
                var putResult = await session.PutRandomAsync(context, HashType.Vso0, provideHash: false, size: contentSize, CancellationToken.None).ThrowIfFailure();
                hashes[i] = putResult.ContentHash;

                var (blobClient, _) = await topology.GetContentBlobClientAsync(context, putResult.ContentHash);
                var fullName = $"/blobServices/default/containers/{blobClient.BlobContainerName}/blobs/{blobClient.Name}";

                var change = new MockChange()
                {
                    Subject = fullName,
                    EventTime = clock.GetUtcNow(),
                    EventType = BlobChangeFeedEventType.BlobCreated,
                    ContentLength = contentSize,
                };

                changes.Add(change);
            }

            var chl = new ContentHashListWithDeterminism(
                new MemoizationStore.Interfaces.Sessions.ContentHashList(hashes), CacheDeterminism.None);
            var fp = StrongFingerprint.Random();

            var r = await session.AddOrGetContentHashListAsync(context, fp, chl, CancellationToken.None).ThrowIfFailure();
            var (containerClient, _) = await topology.GetContainerClientAsync(context, BlobCacheShardingKey.FromWeakFingerprint(fp.WeakFingerprint));
            var blobName = AzureBlobStorageMetadataStore.GetBlobPath(fp);
            var size = (await containerClient.GetBlobClient(blobName).GetPropertiesAsync()).Value.ContentLength;
            var fpChange = new MockChange()
            {
                Subject = $"/blobServices/default/containers/{containerClient.Name}/blobs/{blobName}",
                EventTime = clock.GetUtcNow(),
                EventType = BlobChangeFeedEventType.BlobCreated,
                ContentLength = size,
            };
            changes.Add(fpChange);

            TestDispatcher.AddPage(changes, pages);
        }

        private class TestBlobLifetimeManager : Library.BlobLifetimeManager
        {
            public TestDispatcher? TestDispatcher;
            private readonly List<Page<IBlobChangeFeedEvent>> _pages;

            public TestBlobLifetimeManager(List<Page<IBlobChangeFeedEvent>> pages) => _pages = pages;

            protected override AzureStorageChangeFeedEventDispatcher CreateDispatcher(
                IBlobCacheAccountSecretsProvider secretsProvider,
                IReadOnlyList<BlobCacheStorageAccountName> accountNames,
                string metadataMatrix,
                string contentMatrix,
                RocksDbLifetimeDatabase db,
                LifetimeDatabaseUpdater updater,
                IClock clock,
                CheckpointManager checkpointManager, 
                int? changeFeedPageSize,
                BuildCacheConfiguration? buildCacheConfiguration)
            {
                return TestDispatcher = new TestDispatcher(secretsProvider, accountNames, updater, db, clock, metadataMatrix, contentMatrix, _pages, checkpointManager, changeFeedPageSize, buildCacheConfiguration);
            }
        }

        private class TestDispatcher : AzureStorageChangeFeedEventDispatcher
        {
            public readonly List<Page<IBlobChangeFeedEvent>> Pages;

            public TestDispatcher(
                IBlobCacheAccountSecretsProvider secretsProvider,
                IReadOnlyList<BlobCacheStorageAccountName> accounts,
                LifetimeDatabaseUpdater updater,
                RocksDbLifetimeDatabase db,
                IClock clock,
                string metadataMatrix,
                string contentMatrix,
                List<Page<IBlobChangeFeedEvent>> pages,
                CheckpointManager checkpointManager,
                int? changeFeedPageSize,
                BuildCacheConfiguration? buildCacheConfiguration)
                : base(secretsProvider, accounts, updater, checkpointManager, db, clock, metadataMatrix, contentMatrix, changeFeedPageSize, buildCacheConfiguration)
                => Pages = pages;

            internal override IChangeFeedClient CreateChangeFeedClient(IAzureStorageCredentials creds)
            {
                return new TestFeedClient(Pages);
            }

            public static void AddPage(List<IBlobChangeFeedEvent> changes, List<Page<IBlobChangeFeedEvent>> pages)
            {
                var page = Page<IBlobChangeFeedEvent>.FromValues(changes, continuationToken: pages.Count.ToString(), new MockResponse());
                pages.Add(page);
            }

            private class MockResponse : Response
            {
                public override int Status => 200;

                public override string ReasonPhrase => string.Empty;

                public override Stream? ContentStream { get => null; set { } }
                public override string ClientRequestId { get => string.Empty; set { } }

                public override void Dispose()
                {
                }

                protected override bool ContainsHeader(string name) => false;

                protected override IEnumerable<HttpHeader> EnumerateHeaders() => Enumerable.Empty<HttpHeader>();

                protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
                {
                    value = null;
                    return false;
                }

                protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
                {
                    values = null;
                    return false;
                }
            }
        }

        private class TestFeedClient : IChangeFeedClient
        {
            private readonly List<Page<IBlobChangeFeedEvent>> _pages;

            public TestFeedClient(List<Page<IBlobChangeFeedEvent>> pages) => _pages = pages;

            public async IAsyncEnumerable<Page<IBlobChangeFeedEvent>> GetChangesAsync(string? continuationToken, int? pageSizeHint)
            {
                // We'll ignore page sizes in tests since it's purely a perf optimization.

                if (!int.TryParse(continuationToken, out var skip))
                {
                    skip = 0;
                }

                foreach (var page in _pages.Skip(skip))
                {
                    await Task.Yield();
                    yield return page;
                }
            }

            public async IAsyncEnumerable<Page<IBlobChangeFeedEvent>> GetChangesAsync(DateTime? startTimeUtc, int? pageSizeHint)
            {
                // We'll ignore page sizes in tests since it's purely a perf optimization.

                // This is intended to skip events that happened before DB creation. In the case of these tests, there are none.
                foreach (var page in _pages)
                {
                    await Task.Yield();
                    yield return page;
                }
            }
        }

        private class MockChange : IBlobChangeFeedEvent
        {
            public DateTimeOffset EventTime { get; init; }

            public BlobChangeFeedEventType EventType { get; init; }

            public required string Subject { get; init; }

            public long ContentLength { get; init; }
        }
    }
}
