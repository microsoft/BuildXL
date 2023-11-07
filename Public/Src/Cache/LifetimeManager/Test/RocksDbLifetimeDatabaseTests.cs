// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.BlobLifetimeManager.Library;
using ContentStoreTest.Test;
using FluentAssertions;
using RocksDbSharp;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Cache.BlobLifetimeManager.Library.RocksDbLifetimeDatabase;
using BuildXL.Cache.ContentStore.Distributed.Blob;

namespace BuildXL.Cache.BlobLifetimeManager.Test
{
    public class RocksDbLifetimeDatabaseTests : TestWithOutput
    {
        private readonly MemoryClock _clock = new();
        private static readonly BlobNamespaceId NamespaceId = new BlobNamespaceId("universe", "namespace");

        public RocksDbLifetimeDatabaseTests(ITestOutputHelper output) : base(output)
        {
        }

        private BatchCountingDatabase CreateDatabase(Action<Configuration>? setupConfig = null)
        {
            var temp = new DisposableDirectory(PassThroughFileSystem.Default);

            _clock.UtcNow = DateTime.UtcNow;

            var config = new Configuration
            {
                DatabasePath = temp.Path.Path,
                BlobNamespaceIds = new[] { NamespaceId }
            };

            setupConfig?.Invoke(config);

            var db = BatchCountingDatabase.Create(config, _clock).ThrowIfFailure();

            return db;
        }

        [Fact]
        public void LruOrderingWorksAsync()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var chlCount = 20;

            // We want it to give us one at a time so that we can verify LRU. In reality, we wouldn't expect things to be
            // perfectly ordered.
            using var db = CreateDatabase(config => config.LruEnumerationPercentileStep = 1.0 / chlCount);

            var accessor = db.GetAccessor(NamespaceId);

            // Make distribution uniform to help with non-determinism as much as possible.
            var lastAccessTimes = Enumerable.Range(1, chlCount).Select(hoursAgo => _clock.UtcNow.AddHours(-hoursAgo)).ToList();
            ThreadSafeRandom.Shuffle(lastAccessTimes);

            var chlsWithContentLengths = Enumerable.Range(0, chlCount).Select(i => RandomContentHashList(lastAccessTimeHint: lastAccessTimes[i])).ToArray();
            var contentSize = 0L;

            // Add content to the DB to verify total size is accurate.
            foreach (var chlWithContentLength in chlsWithContentLengths)
            {
                accessor.AddContentHashList(chlWithContentLength.chl, chlWithContentLength.hashesWithLengths);
                contentSize += chlWithContentLength.hashesWithLengths.Sum(h => h.length);
            }

            var result = accessor.GetLruOrderedContentHashLists(context);
            result.TotalSize.Should().Be(chlsWithContentLengths.Sum(chl => chl.chl.BlobSize) + contentSize);
            var currentBatch = 0;
            var currentLowerLimitNonInclusive = DateTime.MinValue;
            var currentUpperLimitInclusive = DateTime.MinValue;
            foreach (var chl in result.LruOrderedContentHashLists)
            {
                if (currentBatch == 0)
                {
                    currentBatch = db.BatchCount;
                    currentLowerLimitNonInclusive = db.CurrentLowerLimitNonInclusive;
                    currentUpperLimitInclusive = db.CurrentUpperLimitInclusive;
                }

                if (currentBatch != db.BatchCount)
                {
                    db.CurrentLowerLimitNonInclusive.Should().BeOnOrAfter(currentUpperLimitInclusive);
                    db.CurrentUpperLimitInclusive.Should().BeAfter(db.CurrentLowerLimitNonInclusive);
                    currentBatch = db.BatchCount;
                    currentLowerLimitNonInclusive = db.CurrentLowerLimitNonInclusive;
                    currentUpperLimitInclusive = db.CurrentUpperLimitInclusive;
                }

                chl.LastAccessTime.Should().BeAfter(currentLowerLimitNonInclusive);
                chl.LastAccessTime.Should().BeOnOrBefore(currentUpperLimitInclusive);
            }

            // Now check that deletion works
            foreach (var chlWithContentLengths in chlsWithContentLengths)
            {
                accessor.DeleteContentHashList(chlWithContentLengths.chl.BlobName, chlWithContentLengths.chl.Hashes);
                foreach (var hash in chlWithContentLengths.hashesWithLengths)
                {
                    accessor.DeleteContent(hash.hash);
                }
            }

            result = accessor.GetLruOrderedContentHashLists(context);
            result.TotalSize.Should().Be(0);
            result.LruOrderedContentHashLists.Count().Should().Be(0);
        }

        private (ContentHashList chl, (ContentHash hash, long length)[] hashesWithLengths) RandomContentHashList((ContentHash, long)? includeHash = null, DateTime? lastAccessTimeHint = null)
        {
            var hashes = Enumerable.Range(0, ThreadSafeRandom.Uniform(1, 200)).Select(i => (ContentHash.Random(), (long)ThreadSafeRandom.Uniform(1,100))).ToArray();

            if (includeHash is not null)
            {
                hashes = hashes.Append(includeHash.Value).ToArray();
            }

            var chl = new ContentHashList(
                BlobName: ThreadSafeRandom.RandomAlphanumeric(length: 32),
                LastAccessTime: lastAccessTimeHint ?? _clock.UtcNow.AddHours(ThreadSafeRandom.Generator.NextDouble() * -48),
                Hashes: hashes.Select(h => h.Item1).ToArray(),
                BlobSize: hashes.LongLength * 33);

            return (chl, hashes);
        }

        [Fact]
        public void LruEnumerationIsDoneInBatches()
        {
            var context = new OperationContext(new Context(TestGlobal.Logger));

            var chlCount = 10;
            var batchSize = 2;

            using var db = CreateDatabase(config =>
            {
                config.LruEnumerationBatchSize = batchSize;

                // Make percentile irrelevant. We're not testing for ordering.
                config.LruEnumerationPercentileStep = config.LruEnumerationPercentileStep = 1.0;
            });

            var accessor = db.GetAccessor(NamespaceId);

            var chlsWithContentLengths = Enumerable.Range(0, chlCount).Select(_ => RandomContentHashList()).ToArray();

            foreach (var chlWithContentLengths in chlsWithContentLengths)
            {
                accessor.AddContentHashList(chlWithContentLengths.chl, chlWithContentLengths.hashesWithLengths);
            }

            var result = accessor.GetLruOrderedContentHashLists(context);
            var current = 0;
            foreach (var chl in result.LruOrderedContentHashLists)
            {
                // A new batch should only be executed when we request the next item.
                db.BatchCount.Should().Be(current++ / batchSize + 1);
            }
        }

        [Fact]
        public void IncrementAndDecrementRefCount()
        {
            using var db = CreateDatabase();
            var accessor = db.GetAccessor(NamespaceId);

            var hash = ContentHash.Random();

            var chlWithContentLengths1 = RandomContentHashList((hash, 1L));
            accessor.AddContentHashList(chlWithContentLengths1.chl, chlWithContentLengths1.hashesWithLengths);
            accessor.GetContentEntry(hash)!.ReferenceCount.Should().Be(1);
            accessor.GetContentEntry(hash)!.BlobSize.Should().Be(1);

            var chlWithContentLengths2 = RandomContentHashList((hash, 0L));
            accessor.AddContentHashList(chlWithContentLengths2.chl, chlWithContentLengths2.hashesWithLengths);
            accessor.GetContentEntry(hash)!.ReferenceCount.Should().Be(2);
            accessor.GetContentEntry(hash)!.BlobSize.Should().Be(1);

            accessor.DeleteContentHashList(chlWithContentLengths1.chl.BlobName, chlWithContentLengths1.chl.Hashes);
            accessor.GetContentEntry(hash)!.ReferenceCount.Should().Be(1);
            accessor.GetContentEntry(hash)!.BlobSize.Should().Be(1);

            accessor.DeleteContentHashList(chlWithContentLengths2.chl.BlobName, chlWithContentLengths2.chl.Hashes);
            accessor.GetContentEntry(hash)!.ReferenceCount.Should().Be(0);
            accessor.GetContentEntry(hash)!.BlobSize.Should().Be(1);
        }

        [Fact]
        public void DeleteContent()
        {
            using var db = CreateDatabase();
            var accessor = db.GetAccessor(NamespaceId);

            var chlWithContentLengths = RandomContentHashList();
            accessor.AddContentHashList(chlWithContentLengths.chl, chlWithContentLengths.hashesWithLengths);
            var fpSize = chlWithContentLengths.chl.BlobSize + chlWithContentLengths.hashesWithLengths.Sum(h => h.length);

            var hash = ContentHash.Random();

            accessor.AddContent(hash, 1);
            var context = new OperationContext(new Context(TestGlobal.Logger));
            var result = accessor.GetLruOrderedContentHashLists(context);
            result.TotalSize.Should().Be(fpSize + 1);

            accessor.DeleteContent(hash);
            result = accessor.GetLruOrderedContentHashLists(context);
            result.TotalSize.Should().Be(fpSize);
        }

        [Fact]
        public void CursorSetAndGet()
        {
            using var db = CreateDatabase();

            db.SetCursor("account1", "c1");
            db.SetCursor("account2", "c2");

            db.GetCursor("account1").Should().Be("c1");
            db.GetCursor("account2").Should().Be("c2");

            db.SetCursor("account1", "c3");
            db.SetCursor("account2", "c4");

            db.GetCursor("account1").Should().Be("c3");
            db.GetCursor("account2").Should().Be("c4");
        }

        [Fact]
        public void NamespaceLastAccessTimeSetAndGet()
        {
            using var db = CreateDatabase();

            var namespacesAndMatrixes = new[]
            {
                (new BlobNamespaceId("u1", "n1"), "m1"),
                (new BlobNamespaceId("u2", "n2"), "m2"),
            };

            var now = DateTime.UtcNow;
            var past = now.AddDays(-1);
            var future = now.AddDays(1);

            foreach (var (namespaceId, matrix) in namespacesAndMatrixes)
            {
                // Should not exist initially
                db.GetNamespaceLastAccessTime(namespaceId, matrix).Should().BeNull();

                // Setting should work.
                db.SetNamespaceLastAccessTime(namespaceId, matrix, now);
                db.GetNamespaceLastAccessTime(namespaceId, matrix).Should().Be(now);

                // Setting to older date does nothing.
                db.SetNamespaceLastAccessTime(namespaceId, matrix, past);
                db.GetNamespaceLastAccessTime(namespaceId, matrix).Should().Be(now);

                // Setting to newer date overrides.
                db.SetNamespaceLastAccessTime(namespaceId, matrix, future);
                db.GetNamespaceLastAccessTime(namespaceId, matrix).Should().Be(future);
            }

            foreach (var (namespaceId, matrix) in namespacesAndMatrixes)
            {
                // Should be deleteable.
                db.SetNamespaceLastAccessTime(namespaceId, matrix, lastAccessTimeUtc: null);
                db.GetNamespaceLastAccessTime(namespaceId, matrix).Should().BeNull();
            }
        }
    }

    public class BatchCountingDatabase : RocksDbLifetimeDatabase
    {
        private BatchCountingDatabase(
            Configuration configuration,
            IClock clock,
            RocksDb db)
            : base(configuration, clock, db)
        {
        }

        public static new Result<BatchCountingDatabase> Create(
            Configuration configuration,
            IClock clock)
        {
            var db = CreateDb(configuration);
            return new BatchCountingDatabase(configuration, clock, db);
        }

        public int BatchCount { get; set; }
        public DateTime CurrentUpperLimitInclusive { get; set; }
        public DateTime CurrentLowerLimitNonInclusive { get; set; }

        protected override (IReadOnlyList<ContentHashList> batch, bool reachedEnd, byte[]? next) GetEnumerateContentHashListsBatch(OperationContext context, DateTime lowerLimitNonInclusive, DateTime upperLimitInclusive, int limit, byte[]? startKey, ColumnFamilyHandle fingerprintsCf)
        {
            BatchCount++;
            CurrentLowerLimitNonInclusive = lowerLimitNonInclusive;
            CurrentUpperLimitInclusive = upperLimitInclusive;
            return base.GetEnumerateContentHashListsBatch(context, lowerLimitNonInclusive, upperLimitInclusive, limit, startKey, fingerprintsCf);
        }
    }
}
