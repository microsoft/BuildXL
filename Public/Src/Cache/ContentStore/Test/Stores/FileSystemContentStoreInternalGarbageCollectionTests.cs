// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.ContentStore.Utils;
using Xunit;

namespace ContentStoreTest.Stores
{
    public class FileSystemContentStoreInternalGarbageCollectionTests : FileSystemContentStoreInternalTestBase
    {
        private readonly Context _context;
        private readonly MemoryClock _clock;

        public FileSystemContentStoreInternalGarbageCollectionTests()
            : base(() => new MemoryFileSystem(new MemoryClock(), Drives), TestGlobal.Logger)
        {
            _context = new Context(Logger);
            _clock = (MemoryClock)((MemoryFileSystem)FileSystem).Clock;
        }

        [Fact]
        public async Task EvictionAnnouncesHash()
        {
            bool batchProcessWasCalled = false;
            var nagleQueue = NagleQueue<ContentHash>.Create(
                hashes => { batchProcessWasCalled = true; return Task.FromResult(42); },
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMinutes(1),
                batchSize: 1);

            await TestStore(
                _context,
                _clock,
                async store =>
                {
                    var cas = store as IContentStoreInternal;
                    var blobSize = BlobSizeToStartSoftPurging(2);

                    using (var stream1 = new MemoryStream(ThreadSafeRandom.GetBytes(blobSize)))
                    using (var stream2 = new MemoryStream(ThreadSafeRandom.GetBytes(blobSize)))
                    {
                        await cas.PutStreamAsync(_context, stream1, ContentHashType).ShouldBeSuccess();
                        _clock.Increment();
                        await cas.PutStreamAsync(_context, stream2, ContentHashType).ShouldBeSuccess();
                        _clock.Increment();
                        await store.SyncAsync(_context);
                    }
                },
                nagleQueue);

            batchProcessWasCalled.Should().BeTrue();
        }

        [Fact]
        public async Task FileSystemContentStoreInternalWithNullGarbageCollector()
        {
            await TestStore(_context, _clock, async store =>
            {
                var cas = store as IContentStoreInternal;
                var blobSize = BlobSizeToStartSoftPurging(2);

                using (var stream1 = new MemoryStream(ThreadSafeRandom.GetBytes(blobSize)))
                using (var stream2 = new MemoryStream(ThreadSafeRandom.GetBytes(blobSize)))
                {
                    await cas.PutStreamAsync(_context, stream1, ContentHashType).ShouldBeSuccess();
                    _clock.Increment();
                    await cas.PutStreamAsync(_context, stream2, ContentHashType).ShouldBeSuccess();
                    _clock.Increment();
                    await store.SyncAsync(_context);
                }
            });
        }
    }
}
