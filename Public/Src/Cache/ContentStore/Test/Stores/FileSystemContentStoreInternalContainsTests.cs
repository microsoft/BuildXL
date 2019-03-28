// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stores
{
    public class FileSystemContentStoreInternalContainsTests : FileSystemContentStoreInternalTestBase
    {
        private readonly MemoryClock _clock;

        public FileSystemContentStoreInternalContainsTests()
            : base(() => new MemoryFileSystem(new MemoryClock()), TestGlobal.Logger)
        {
            _clock = (MemoryClock)((MemoryFileSystem)FileSystem).Clock;
        }

        [Fact]
        public Task ContainsFalse()
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                IContentStoreInternal cas = store;
                var contentHash = ContentHash.Random();
                (await cas.ContainsAsync(context, contentHash)).Should().BeFalse();
            });
        }

        [Fact]
        public Task ContainsPins()
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                var r = await store.PutRandomAsync(context, MaxSizeHard / 3);
                _clock.Increment();
                using (var pinContext = store.CreatePinContext())
                {
                    Assert.True(await store.ContainsAsync(context, r.ContentHash, new PinRequest(pinContext)));
                    _clock.Increment();
                    await store.EnsureContentIsPinned(context, _clock, r.ContentHash);
                    Assert.True(pinContext.Contains(r.ContentHash));
                }

                await store.EnsureContentIsNotPinned(context, _clock, r.ContentHash);
            });
        }
    }
}
