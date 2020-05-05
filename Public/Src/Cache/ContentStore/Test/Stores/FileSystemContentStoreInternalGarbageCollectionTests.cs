// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
                });
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
