// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.BlobLifetimeManager.Library;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using ContentHashList = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.ContentHashList;

namespace BuildXL.Cache.BlobLifetimeManager.Test
{
    public class LifetimeDatabaseUpdaterTests : LifetimeDatabaseTestBase
    {
        public LifetimeDatabaseUpdaterTests(LocalRedisFixture redis, ITestOutputHelper output) : base(redis, output)
        {
        }

        [Fact]
        public Task DoubleCreateUpdatesHashList()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));
            return RunTest(context,
                async (topology, session, namespaceId, secretsProvider) =>
                {
                    using var temp = new DisposableDirectory(PassThroughFileSystem.Default);
                    var db = RocksDbLifetimeDatabase.Create(
                        new RocksDbLifetimeDatabase.Configuration
                        {
                            BlobNamespaceIds = new List<BlobNamespaceId> { namespaceId },
                            DatabasePath = temp.Path.Path
                        },
                        SystemClock.Instance);

                    var topologies = new Dictionary<BlobNamespaceId, IBlobCacheTopology> { { namespaceId, topology } };
                    var accessor = db.GetAccessor(namespaceId);
                    var accessors = new Dictionary<BlobNamespaceId, RocksDbLifetimeDatabase.IAccessor> { { namespaceId, accessor } };
                    var updater = new LifetimeDatabaseUpdater(topologies, accessors, SystemClock.Instance, fingerprintsDegreeOfParallelism: 1);

                    var strongFingerprint = StrongFingerprint.Random();

                    var hashes1 = await session.PutRandomAsync(context, HashType.Vso0, provideHash: true, fileCount: 5, fileSize: 10, useExactSize: true);
                    var chl1 = new ContentHashListWithDeterminism(new ContentHashList(hashes1.ToArray()), CacheDeterminism.SinglePhaseNonDeterministic);
                    await session.AddOrGetContentHashListAsync(context, strongFingerprint, chl1, context.Token).ThrowIfFailureAsync();

                    var getReuslt1 = await session.GetContentHashListAsync(context, strongFingerprint, context.Token).ThrowIfFailureAsync();
                    getReuslt1.ContentHashListWithDeterminism.ContentHashList!.Hashes.Should().BeEquivalentTo(hashes1);

                    await updater.ContentHashListCreatedAsync(context, namespaceId, AzureBlobStorageMetadataStore.GetBlobPath(strongFingerprint), blobLength: 100).ThrowIfFailureAsync();

                    accessor.GetContentHashList(strongFingerprint, out _)!.Hashes.Should().BeEquivalentTo(hashes1);

                    foreach (var hash in hashes1)
                    {
                        accessor.GetContentEntry(hash)!.ReferenceCount.Should().Be(1);
                    }

                    // We add a second CHL with the same strong fingerprint. Since this is single-phase determinism, it should replace the old one.
                    var hashes2 = await session.PutRandomAsync(context, HashType.Vso0, provideHash: true, fileCount: 5, fileSize: 10, useExactSize: true);
                    var chl2 = new ContentHashListWithDeterminism(new ContentHashList(hashes2.ToArray()), CacheDeterminism.ViaCache(Guid.NewGuid(), DateTime.MaxValue));
                    var result = await session.AddOrGetContentHashListAsync(context, strongFingerprint, chl2, context.Token).ThrowIfFailureAsync();
                    result.ContentHashListWithDeterminism.ContentHashList.Should().BeNull(); // Null indicates we replaced the old CHL.

                    var getReuslt2 = await session.GetContentHashListAsync(context, strongFingerprint, context.Token).ThrowIfFailureAsync();
                    getReuslt2.ContentHashListWithDeterminism.ContentHashList!.Hashes.Should().BeEquivalentTo(hashes2);

                    await updater.ContentHashListCreatedAsync(context, namespaceId, AzureBlobStorageMetadataStore.GetBlobPath(strongFingerprint), blobLength: 100).ThrowIfFailureAsync();

                    accessor.GetContentHashList(strongFingerprint, out _)!.Hashes.Should().BeEquivalentTo(hashes2);

                    foreach (var hash in hashes1)
                    {
                        accessor.GetContentEntry(hash)!.ReferenceCount.Should().Be(0);
                    }

                    foreach (var hash in hashes2)
                    {
                        accessor.GetContentEntry(hash)!.ReferenceCount.Should().Be(1);
                    }
                });
        }
    }
}
